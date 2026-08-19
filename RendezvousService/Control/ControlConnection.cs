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

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.Logging;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// One control connection: a sequence of UTF-8 commands, one per line,
    /// each answered by exactly one UTF-8 response line.
    ///
    /// Lines are terminated by LF or CR LF, responses always end with CR LF.
    /// A command longer than the configured maximum command length closes
    /// the connection, so that a broken client can not exhaust the memory
    /// of the service.
    /// </summary>
    internal sealed class ControlConnection
    {

        #region Data

        /// <summary>
        /// Commands must be valid UTF-8, therefore invalid byte sequences are
        /// rejected instead of being silently replaced.
        /// </summary>
        private static readonly UTF8Encoding utf8 = new (encoderShouldEmitUTF8Identifier: false,
                                                         throwOnInvalidBytes:             true);

        /// <summary>
        /// How long the rest of a rejected command is read and thrown away,
        /// before the connection is closed.
        /// </summary>
        private static readonly TimeSpan LingerTimeout = TimeSpan.FromMilliseconds(500);

        private readonly Socket                     socket;
        private readonly RendezvousManager          manager;
        private readonly RendezvousOptions          options;
        private readonly ILogger<ControlConnection> logger;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a new control connection.
        /// </summary>
        /// <param name="Socket">The accepted TCP socket.</param>
        /// <param name="Manager">The rendezvous manager.</param>
        /// <param name="Options">The configuration of the rendezvous service.</param>
        /// <param name="Logger">A logger.</param>
        public ControlConnection(Socket                      Socket,
                                 RendezvousManager           Manager,
                                 RendezvousOptions           Options,
                                 ILogger<ControlConnection>  Logger)
        {

            this.socket   = Socket;
            this.manager  = Manager;
            this.options  = Options;
            this.logger   = Logger;

        }

        #endregion


        #region RunAsync(CancellationToken)

        /// <summary>
        /// Read and execute commands until the client disconnects.
        /// </summary>
        /// <param name="CancellationToken">A token to stop this connection.</param>
        public async Task RunAsync(CancellationToken CancellationToken)
        {

            var remoteSocket = socket.RemoteEndPoint as IPEndPoint;
            var buffer       = ArrayPool<Byte>.Shared.Rent(options.MaxCommandLength + 2);
            var used         = 0;

            logger.LogDebug("Control connection from {RemoteSocket} accepted.", remoteSocket);

            try
            {

                socket.NoDelay = true;

                await using var stream = new NetworkStream(socket, ownsSocket: false);

                while (!CancellationToken.IsCancellationRequested)
                {

                    #region Read, but not forever

                    using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                    idleTimeout.CancelAfter(options.ControlIdleTimeout);

                    // Cancelling a pending socket read is a best effort operation on
                    // some platforms, but shutting down the socket always ends it.
                    using var cancellation = idleTimeout.Token.Register(
                                                 static socket => ((Socket) socket!).ShutdownSafely(SocketShutdown.Both),
                                                 socket
                                             );

                    Int32 received;

                    try
                    {
                        received = await stream.ReadAsync(buffer.AsMemory(used, buffer.Length - used),
                                                          idleTimeout.Token).
                                                ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!CancellationToken.IsCancellationRequested)
                    {

                        logger.LogDebug("Control connection from {RemoteSocket} was idle for more than {Timeout}.",
                                        remoteSocket, options.ControlIdleTimeout);

                        return;

                    }

                    if (received == 0)
                    {

                        if (idleTimeout.IsCancellationRequested && !CancellationToken.IsCancellationRequested)
                            logger.LogDebug("Control connection from {RemoteSocket} was idle for more than {Timeout}.",
                                            remoteSocket, options.ControlIdleTimeout);

                        return;

                    }

                    used += received;

                    #endregion

                    #region Execute every complete command

                    while (TryTakeLine(buffer, ref used, out var line, out var invalidEncoding))
                    {

                        if (invalidEncoding)
                        {

                            await SendAsync(stream,
                                            CommandResponse.Error(ResponseCode.InvalidSyntax,
                                                                  "The command is not valid UTF-8!"),
                                            CancellationToken).
                                  ConfigureAwait(false);

                            continue;

                        }

                        // Ignore empty lines and a UTF-8 byte order mark.
                        var command = line!.TrimStart((Char) 0xFEFF).Trim();

                        if (command.Length == 0)
                            continue;

                        var response = manager.Execute(command);

                        if (!response.IsSuccess)
                            logger.LogDebug("Control connection from {RemoteSocket}: {Response}",
                                            remoteSocket, response.ToProtocolLine());

                        await SendAsync(stream, response, CancellationToken).ConfigureAwait(false);

                    }

                    #endregion

                    #region A command must not be longer than the configured maximum

                    if (used > options.MaxCommandLength)
                    {

                        await SendAsync(stream,
                                        CommandResponse.Error(ResponseCode.CommandTooLong,
                                                              $"A command must not be longer than {options.MaxCommandLength} bytes!"),
                                        CancellationToken).
                              ConfigureAwait(false);

                        await LingeringCloseAsync(socket, stream, CancellationToken).ConfigureAwait(false);

                        return;

                    }

                    #endregion

                }

            }
            catch (OperationCanceledException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (SocketException e)
            {
                logger.LogDebug("Control connection from {RemoteSocket} was reset ({SocketError}).",
                                remoteSocket, e.SocketErrorCode);
            }
            catch (IOException e)
            {
                logger.LogDebug("Control connection from {RemoteSocket} failed: {Message}",
                                remoteSocket, e.Message);
            }
            finally
            {

                ArrayPool<Byte>.Shared.Return(buffer);

                socket.ShutdownSafely(SocketShutdown.Both);

                logger.LogDebug("Control connection from {RemoteSocket} closed.", remoteSocket);

            }

        }

        #endregion


        #region (private, static) TryTakeLine(Buffer, ref Used, out Line, out InvalidEncoding)

        /// <summary>
        /// Try to take the next complete line out of the given buffer.
        /// </summary>
        /// <param name="Buffer">The receive buffer.</param>
        /// <param name="Used">The number of valid bytes within the receive buffer.</param>
        /// <param name="Line">The next line, or null when the line was not valid UTF-8.</param>
        /// <param name="InvalidEncoding">Whether the line was not valid UTF-8.</param>
        private static Boolean TryTakeLine(Byte[]       Buffer,
                                           ref Int32    Used,
                                           out String?  Line,
                                           out Boolean  InvalidEncoding)
        {

            Line             = null;
            InvalidEncoding  = false;

            var index = Array.IndexOf(Buffer, (Byte) '\n', 0, Used);

            if (index < 0)
                return false;

            var length = index > 0 && Buffer[index - 1] == (Byte) '\r'
                             ? index - 1
                             : index;

            try
            {
                Line = utf8.GetString(Buffer, 0, length);
            }
            catch (DecoderFallbackException)
            {
                InvalidEncoding = true;
            }

            var remaining = Used - (index + 1);

            Buffer.AsSpan(index + 1, remaining).CopyTo(Buffer.AsSpan(0, remaining));
            Used = remaining;

            return true;

        }

        #endregion

        #region (private, static) LingeringCloseAsync(Socket, Stream, CancellationToken)

        /// <summary>
        /// Close a connection that still has data on its way.
        ///
        /// Closing a TCP connection while unread data is waiting within the receive
        /// buffer makes the operating system send a reset - which would also discard
        /// the error response that was just sent. Therefore the rest of the request
        /// is read and thrown away first.
        /// </summary>
        /// <param name="Socket">The socket to close.</param>
        /// <param name="Stream">The stream of the socket.</param>
        /// <param name="CancellationToken">A token to stop this connection.</param>
        private static async Task LingeringCloseAsync(Socket             Socket,
                                                      Stream             Stream,
                                                      CancellationToken  CancellationToken)
        {

            Socket.ShutdownSafely(SocketShutdown.Send);

            var buffer = ArrayPool<Byte>.Shared.Rent(4096);

            try
            {

                using var lingerTimeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                lingerTimeout.CancelAfter(LingerTimeout);

                using var cancellation = lingerTimeout.Token.Register(
                                             static socket => ((Socket) socket!).ShutdownSafely(SocketShutdown.Both),
                                             Socket
                                         );

                while (await Stream.ReadAsync(buffer, lingerTimeout.Token).ConfigureAwait(false) > 0)
                { }

            }
            catch (OperationCanceledException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (SocketException)
            { }
            catch (IOException)
            { }
            finally
            {
                ArrayPool<Byte>.Shared.Return(buffer);
            }

        }

        #endregion

        #region (private, static) SendAsync(Stream, Response, CancellationToken)

        /// <summary>
        /// Send the given response.
        /// </summary>
        private static async Task SendAsync(Stream             Stream,
                                            CommandResponse    Response,
                                            CancellationToken  CancellationToken)
        {

            var bytes = utf8.GetBytes($"{Response.ToProtocolLine()}\r\n");

            await Stream.WriteAsync(bytes, CancellationToken).ConfigureAwait(false);
            await Stream.FlushAsync(CancellationToken).      ConfigureAwait(false);

        }

        #endregion

    }

}
