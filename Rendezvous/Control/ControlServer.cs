/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Vanaheimr RendezvousService <https://www.github.com/Vanaheimr/RendezvousService>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous
{

    /// <summary>
    /// The control endpoint of the rendezvous service: a TCP listener accepting
    /// UTF-8 commands, one command per line.
    ///
    /// This is a plain object with Start() and StopAsync(), not a hosted service.
    /// Whoever wants it within a generic host wraps it into an IHostedService of
    /// ten lines - the other way around a library would force a hosting framework
    /// onto everybody who just wants a control endpoint.
    /// </summary>
    public sealed class ControlServer : IAsyncDisposable
    {

        #region Data

        private readonly RendezvousManager                   manager;
        private readonly RendezvousOptions                   options;
        private readonly ILoggerFactory                      loggerFactory;
        private readonly ILogger<ControlServer>              logger;
        private readonly ConcurrentDictionary<Int64, Task>   connections   = [];
        private readonly CancellationTokenSource             stopSource    = new();

        private          TcpListener?                        listener;
        private          Task?                               acceptTask;
        private          Int32                               connectionCount;
        private          Int64                               connectionCounter;

        #endregion

        #region Properties

        /// <summary>
        /// The TCP socket this control endpoint is listening on.
        /// Only available after <see cref="Start"/> was called.
        /// </summary>
        public IPEndPoint? LocalEndPoint
            => listener?.LocalEndpoint as IPEndPoint;

        /// <summary>
        /// Whether this control endpoint is accepting connections.
        /// </summary>
        public Boolean IsRunning
            => acceptTask is not null && !stopSource.IsCancellationRequested;

        /// <summary>
        /// The number of currently open control connections.
        /// </summary>
        public Int32 OpenConnections
            => Volatile.Read(ref connectionCount);

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a new control endpoint.
        /// </summary>
        /// <param name="Manager">The rendezvous manager to control.</param>
        /// <param name="Options">An optional configuration, defaults are used otherwise.</param>
        /// <param name="LoggerFactory">An optional logger factory.</param>
        public ControlServer(RendezvousManager   Manager,
                             RendezvousOptions?  Options         = null,
                             ILoggerFactory?     LoggerFactory   = null)
        {

            ArgumentNullException.ThrowIfNull(Manager);

            this.manager        = Manager;
            this.options        = Options        ?? new RendezvousOptions();
            this.loggerFactory  = LoggerFactory  ?? NullLoggerFactory.Instance;
            this.logger         = this.loggerFactory.CreateLogger<ControlServer>();

        }

        #endregion


        #region Start()

        /// <summary>
        /// Bind the control endpoint and start accepting connections.
        /// The TCP port is bound synchronously, so that <see cref="LocalEndPoint"/>
        /// is known - and a port conflict is reported - as soon as this returns.
        /// </summary>
        /// <exception cref="SocketException">When the TCP port could not be bound.</exception>
        public void Start()
        {

            if (acceptTask is not null)
                return;

            var tcpListener = new TcpListener(options.ControlIPAddress,
                                              options.ControlPort);

            // Do not allow other processes to bind the same port via SO_REUSEADDR.
            if (OperatingSystem.IsWindows())
                tcpListener.ExclusiveAddressUse = true;

            tcpListener.Start();

            listener    = tcpListener;
            acceptTask  = AcceptLoopAsync(tcpListener, stopSource.Token);

            logger.LogInformation("The rendezvous control endpoint is listening on {LocalEndPoint}.", LocalEndPoint);

        }

        #endregion

        #region StopAsync()

        /// <summary>
        /// Stop accepting new connections and wait for the open ones.
        /// </summary>
        public async Task StopAsync()
        {

            if (acceptTask is null)
                return;

            listener?.Stop();

            try
            {
                await stopSource.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            { }

            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { }

            try
            {
                await Task.WhenAll(connections.Values).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "A control connection did not shut down cleanly.");
            }

            listener?.Dispose();

            logger.LogInformation("The rendezvous control endpoint was stopped.");

        }

        #endregion


        #region (private) AcceptLoopAsync(Listener, CancellationToken)

        /// <summary>
        /// Accept control connections until this control endpoint is stopped.
        /// </summary>
        private async Task AcceptLoopAsync(TcpListener        Listener,
                                           CancellationToken  CancellationToken)
        {

            // Do not run the accept loop within Start().
            await Task.Yield();

            while (!CancellationToken.IsCancellationRequested)
            {

                Socket socket;

                try
                {
                    socket = await Listener.AcceptSocketAsync(CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException e)
                {

                    if (!CancellationToken.IsCancellationRequested)
                        logger.LogError("The control endpoint stopped accepting connections ({SocketError})!", e.SocketErrorCode);

                    break;

                }

                #region Do not accept an unlimited number of control connections

                if (Interlocked.Increment(ref connectionCount) > options.MaxControlConnections)
                {

                    Interlocked.Decrement(ref connectionCount);

                    logger.LogWarning("Rejected the control connection from {RemoteSocket}: more than {MaxControlConnections} open control connections!",
                                      socket.RemoteEndPoint, options.MaxControlConnections);

                    socket.ShutdownSafely(SocketShutdown.Both);
                    socket.Dispose();

                    continue;

                }

                #endregion

                var connectionId = Interlocked.Increment(ref connectionCounter);

                connections[connectionId] = HandleConnectionAsync(connectionId, socket, CancellationToken);

            }

        }

        #endregion

        #region (private) HandleConnectionAsync(ConnectionId, Socket, CancellationToken)

        /// <summary>
        /// Serve one control connection.
        /// </summary>
        private async Task HandleConnectionAsync(Int64              ConnectionId,
                                                 Socket             Socket,
                                                 CancellationToken  CancellationToken)
        {

            // Do not block the accept loop.
            await Task.Yield();

            try
            {

                var connection = new ControlConnection(Socket,
                                                       manager,
                                                       options,
                                                       loggerFactory.CreateLogger<ControlConnection>());

                await connection.RunAsync(CancellationToken).ConfigureAwait(false);

            }
            catch (Exception e)
            {
                logger.LogError(e, "A control connection failed!");
            }
            finally
            {

                Socket.Dispose();

                Interlocked.Decrement(ref connectionCount);
                connections.TryRemove(ConnectionId, out _);

            }

        }

        #endregion


        #region DisposeAsync()

        /// <summary>
        /// Stop this control endpoint.
        /// </summary>
        public async ValueTask DisposeAsync()
        {

            await StopAsync().ConfigureAwait(false);

            stopSource.Dispose();

        }

        #endregion

    }

}
