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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// The control endpoint of the rendezvous service: a TCP listener accepting
    /// UTF-8 commands, one command per line.
    /// </summary>
    public sealed class ControlServer : BackgroundService
    {

        #region Data

        private readonly RendezvousManager               manager;
        private readonly RendezvousOptions               options;
        private readonly ILoggerFactory                  loggerFactory;
        private readonly ILogger<ControlServer>          logger;
        private readonly ConcurrentDictionary<Int64, Task>  connections = [];

        private          TcpListener?                    listener;
        private          Int32                           connectionCount;
        private          Int64                           connectionCounter;

        #endregion

        #region Properties

        /// <summary>
        /// The TCP socket the control endpoint is listening on.
        /// Only available after the service was started.
        /// </summary>
        public IPEndPoint? LocalEndPoint
            => listener?.LocalEndpoint as IPEndPoint;

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
        /// <param name="Manager">The rendezvous manager.</param>
        /// <param name="Options">The configuration of the rendezvous service.</param>
        /// <param name="LoggerFactory">A logger factory.</param>
        public ControlServer(RendezvousManager            Manager,
                             IOptions<RendezvousOptions>  Options,
                             ILoggerFactory               LoggerFactory)
        {

            this.manager        = Manager;
            this.options        = Options.Value;
            this.loggerFactory  = LoggerFactory;
            this.logger         = LoggerFactory.CreateLogger<ControlServer>();

        }

        #endregion


        #region StartAsync(CancellationToken)

        /// <summary>
        /// Bind the control endpoint, so that its TCP port is reserved
        /// (and known) as soon as this service was started.
        /// </summary>
        /// <param name="CancellationToken">A token to cancel starting.</param>
        public override Task StartAsync(CancellationToken CancellationToken)
        {

            var tcpListener = new TcpListener(options.ControlIPAddress,
                                              options.ControlPort);

            // Do not allow other processes to bind the same port via SO_REUSEADDR.
            if (OperatingSystem.IsWindows())
                tcpListener.ExclusiveAddressUse = true;

            tcpListener.Start();

            listener = tcpListener;

            logger.LogInformation("The rendezvous control endpoint is listening on {LocalEndPoint}.", LocalEndPoint);

            return base.StartAsync(CancellationToken);

        }

        #endregion

        #region ExecuteAsync(StoppingToken)

        /// <summary>
        /// Accept control connections until the service is stopped.
        /// </summary>
        /// <param name="StoppingToken">A token to stop this background service.</param>
        protected override async Task ExecuteAsync(CancellationToken StoppingToken)
        {

            var tcpListener = listener ?? throw new InvalidOperationException("The control endpoint was not started!");

            while (!StoppingToken.IsCancellationRequested)
            {

                Socket socket;

                try
                {
                    socket = await tcpListener.AcceptSocketAsync(StoppingToken).ConfigureAwait(false);
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

                    if (!StoppingToken.IsCancellationRequested)
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

                connections[connectionId] = HandleConnectionAsync(connectionId, socket, StoppingToken);

            }

        }

        #endregion

        #region StopAsync(CancellationToken)

        /// <summary>
        /// Stop accepting new control connections and wait for the open ones.
        /// </summary>
        /// <param name="CancellationToken">A token to cancel stopping.</param>
        public override async Task StopAsync(CancellationToken CancellationToken)
        {

            listener?.Stop();

            await base.StopAsync(CancellationToken).ConfigureAwait(false);

            try
            {
                await Task.WhenAll(connections.Values).WaitAsync(CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { }
            catch (Exception e)
            {
                logger.LogDebug(e, "A control connection did not shut down cleanly.");
            }

            listener?.Dispose();

            logger.LogInformation("The rendezvous control endpoint was stopped.");

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

    }

}
