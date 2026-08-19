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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous
{

    /// <summary>
    /// Creates, tracks and closes all rendezvous of this service.
    /// </summary>
    public sealed class RendezvousManager : IAsyncDisposable
    {

        #region Data

        /// <summary>
        /// How often a free TCP port is looked for, when the operating system
        /// hands out ports outside of the configured port range.
        /// </summary>
        private const Int32 RandomPortAttempts = 64;

        private readonly RendezvousOptions                                options;
        private readonly TimeProvider                                     timeProvider;
        private readonly ILoggerFactory                                   loggerFactory;
        private readonly ILogger<RendezvousManager>                       logger;

        private readonly ConcurrentDictionary<Guid,   RendezvousSession>  sessions    = [];
        private readonly ConcurrentDictionary<UInt16, RendezvousSession>  portOwners  = [];
        private readonly Lock                                             createLock  = new();

        #endregion

        #region Properties

        /// <summary>
        /// All currently known rendezvous.
        /// </summary>
        public IReadOnlyCollection<RendezvousSession> Sessions
            => [.. sessions.Values];

        /// <summary>
        /// The number of currently known rendezvous.
        /// </summary>
        public Int32 Count
            => sessions.Count;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a new rendezvous manager.
        /// </summary>
        /// <param name="Options">The configuration of the rendezvous service.</param>
        /// <param name="TimeProvider">A time provider.</param>
        /// <param name="LoggerFactory">A logger factory.</param>
        public RendezvousManager(IOptions<RendezvousOptions>  Options,
                                 TimeProvider                 TimeProvider,
                                 ILoggerFactory               LoggerFactory)
        {

            this.options        = Options.Value;
            this.timeProvider   = TimeProvider;
            this.loggerFactory  = LoggerFactory;
            this.logger         = LoggerFactory.CreateLogger<RendezvousManager>();

        }

        #endregion


        #region Execute(CommandLine)

        /// <summary>
        /// Parse and execute the given control command.
        /// </summary>
        /// <param name="CommandLine">A text representation of a control command.</param>
        public CommandResponse Execute(String CommandLine)

            => CommandParser.TryParse(CommandLine, out var command, out var error)
                   ? Execute(command)
                   : CommandResponse.Error(error);

        #endregion

        #region Execute(Command)

        /// <summary>
        /// Execute the given control command.
        /// </summary>
        /// <param name="Command">A control command.</param>
        public CommandResponse Execute(RendezvousCommand Command)
        {
            try
            {

                return Command switch {
                           ConnectPortsCommand     connectPorts     => ConnectPorts   (connectPorts),
                           DisconnectPortsCommand  disconnectPorts  => DisconnectPorts(disconnectPorts),
                           _                                        => CommandResponse.Error(ResponseCode.UnknownCommand,
                                                                                             $"Unknown command '{Command.CommandName}'!")
                       };

            }
            catch (Exception e)
            {

                logger.LogError(e, "The command {Command} failed!", Command);

                return CommandResponse.Error(ResponseCode.InternalError,
                                             "The command could not be executed!");

            }
        }

        #endregion

        #region ConnectPorts(Command)

        /// <summary>
        /// Open a new rendezvous and return the protocol response.
        /// Use <see cref="TryConnectPorts"/> when the rendezvous itself is needed.
        /// </summary>
        /// <param name="Command">A ConnectPorts command.</param>
        public CommandResponse ConnectPorts(ConnectPortsCommand Command)

            => OpenSession(Command).Response;

        #endregion

        #region TryConnectPorts(Command, out Session, out Response)

        /// <summary>
        /// Open a new rendezvous and return it.
        ///
        /// This is the typed variant for callers within the same process: it hands
        /// out the rendezvous itself, so that the chosen TCP ports of a '?' do not
        /// have to be parsed out of the response text.
        /// </summary>
        /// <param name="Command">A ConnectPorts command.</param>
        /// <param name="Session">The new rendezvous, when successful.</param>
        /// <param name="Response">The protocol response, successful or not.</param>
        public Boolean TryConnectPorts(ConnectPortsCommand                        Command,
                                       [NotNullWhen(true)] out RendezvousSession?  Session,
                                                           out CommandResponse     Response)
        {

            (Response, Session) = OpenSession(Command);

            return Response.IsSuccess;

        }

        #endregion

        #region DisconnectPorts(Command)

        /// <summary>
        /// Close an existing rendezvous and return the protocol response.
        /// Use <see cref="TryDisconnectPorts"/> when the rendezvous itself is needed.
        /// </summary>
        /// <param name="Command">A DisconnectPorts command.</param>
        public CommandResponse DisconnectPorts(DisconnectPortsCommand Command)

            => CloseSession(Command).Response;

        #endregion

        #region TryDisconnectPorts(Command, out Session, out Response)

        /// <summary>
        /// Close an existing rendezvous and return it.
        ///
        /// Closing is asynchronous: await <see cref="RendezvousSession.Completion"/>
        /// of the returned rendezvous to know when its TCP ports are free again.
        /// Looking the rendezvous up afterwards would be a race, as a closed
        /// rendezvous is unregistered.
        /// </summary>
        /// <param name="Command">A DisconnectPorts command.</param>
        /// <param name="Session">The closed rendezvous, when successful.</param>
        /// <param name="Response">The protocol response, successful or not.</param>
        public Boolean TryDisconnectPorts(DisconnectPortsCommand                     Command,
                                          [NotNullWhen(true)] out RendezvousSession?  Session,
                                                              out CommandResponse     Response)
        {

            (Response, Session) = CloseSession(Command);

            return Response.IsSuccess;

        }

        #endregion


        #region (private) OpenSession(Command)

        /// <summary>
        /// Open a new rendezvous: validate the request, bind all TCP ports and
        /// start waiting for the clients.
        /// </summary>
        /// <param name="Command">A ConnectPorts command.</param>
        private (CommandResponse Response, RendezvousSession? Session)

            OpenSession(ConnectPortsCommand Command)

        {

            #region Validate the request

            // The parser rejects all of this as well, but a caller using the
            // typed API directly never went through the parser.

            if (Command.Ports.Count < CommandParser.MinPortsPerSession)
                return (CommandResponse.Error(ResponseCode.InvalidSyntax,
                                              $"A rendezvous requires at least {CommandParser.MinPortsPerSession} ports!"), null);

            if (Command.Ports.Count > options.MaxPortsPerSession)
                return (CommandResponse.Error(ResponseCode.TooManyPorts,
                                              $"A rendezvous must not have more than {options.MaxPortsPerSession} ports!"), null);

            if (Command.Secret.Length == 0)
                return (CommandResponse.Error(ResponseCode.InvalidSyntax,
                                              "The secret must not be empty!"), null);

            var requestedPorts = new HashSet<UInt16>();

            foreach (var port in Command.Ports)
            {

                if (port.Port is not UInt16 fixedPort)
                    continue;

                if (!options.IsAllowedDataPort(fixedPort))
                    return (CommandResponse.Error(ResponseCode.PortNotAllowed,
                                                  $"TCP port {fixedPort} is not within the allowed port range {options.MinDataPort} - {options.MaxDataPort}!"), null);

                if (!requestedPorts.Add(fixedPort))
                    return (CommandResponse.Error(ResponseCode.InvalidSyntax,
                                                  $"Duplicate TCP port {fixedPort}!"), null);

            }

            #endregion

            var profile   = Command.Profile ?? options.DefaultProfile;
            var settings  = options.Profiles[profile];
            var address   = options.DataIPAddress;

            lock (createLock)
            {

                if (sessions.Count >= options.MaxSessions)
                    return (CommandResponse.Error(ResponseCode.TooManySessions,
                                                  $"The service is already running {options.MaxSessions} rendezvous!"), null);

                foreach (var port in Command.Ports)
                {
                    if (port.Port is UInt16 fixedPort && portOwners.ContainsKey(fixedPort))
                        return (CommandResponse.Error(ResponseCode.PortInUse,
                                                      $"TCP port {fixedPort} is already used by another rendezvous!"), null);
                }

                var endpoints  = new SessionEndpoint?[Command.Ports.Count];
                var usedPorts  = new HashSet<UInt16>();

                try
                {

                    #region Bind all fixed ports first...

                    for (var i = 0; i < Command.Ports.Count; i++)
                    {

                        if (Command.Ports[i].Port is not UInt16 fixedPort)
                            continue;

                        endpoints[i] = SessionEndpoint.Bind(address,
                                                            fixedPort,
                                                            Command.Ports[i],
                                                            settings.ListenBacklog);

                        usedPorts.Add(fixedPort);

                    }

                    #endregion

                    #region ...and afterwards all random ports

                    for (var i = 0; i < Command.Ports.Count; i++)
                    {

                        if (!Command.Ports[i].IsRandom)
                            continue;

                        endpoints[i] = BindRandomPort(address,
                                                      settings.ListenBacklog,
                                                      usedPorts);

                    }

                    #endregion

                }
                catch (SocketException e)
                {

                    foreach (var endpoint in endpoints)
                        endpoint?.Dispose();

                    logger.LogWarning("Could not open the requested TCP ports [{Ports}]: {Message}",
                                      String.Join(", ", Command.Ports), e.Message);

                    return (CommandResponse.Error(ResponseCode.PortInUse,
                                                  $"Could not open the requested TCP ports: {e.SocketErrorCode}!"), null);

                }

                #region Register and start the new rendezvous

                var session = new RendezvousSession(
                                  Guid.CreateVersion7(timeProvider.GetUtcNow()),
                                  [.. endpoints.Select(endpoint => endpoint!)],
                                  Command.Secret,
                                  profile,
                                  settings,
                                  timeProvider,
                                  loggerFactory.CreateLogger<RendezvousSession>()
                              );

                sessions.TryAdd(session.Id, session);

                foreach (var port in session.Ports)
                    portOwners[port] = session;

                session.OnClosed += SessionClosed;

                session.Start();

                logger.LogInformation("Rendezvous {SessionId} opened on TCP/[{Ports}] using the {Profile} profile.",
                                      session.Id, String.Join(", ", session.Ports), profile.AsText());

                #endregion

                return (CommandResponse.Ok($"{ConnectPortsCommand.Name}([{String.Join(", ", session.Ports)}], {profile.AsText()})"), session);

            }

        }

        #endregion

        #region (private) CloseSession(Command)

        /// <summary>
        /// Close an existing rendezvous: all given TCP ports must belong to the
        /// same rendezvous and the secret must authorize the command.
        /// </summary>
        /// <param name="Command">A DisconnectPorts command.</param>
        private (CommandResponse Response, RendezvousSession? Session)

            CloseSession(DisconnectPortsCommand Command)

        {

            if (Command.Ports.Count == 0)
                return (CommandResponse.Error(ResponseCode.InvalidSyntax,
                                              "At least one TCP port is required!"), null);

            if (!portOwners.TryGetValue(Command.Ports[0], out var session))
                return (CommandResponse.Error(ResponseCode.UnknownSession,
                                              $"There is no rendezvous on TCP port {Command.Ports[0]}!"), null);

            foreach (var port in Command.Ports)
            {
                if (!portOwners.TryGetValue(port, out var owner) ||
                    !ReferenceEquals(owner, session))
                {
                    return (CommandResponse.Error(ResponseCode.UnknownSession,
                                                  $"TCP port {port} does not belong to the same rendezvous!"), null);
                }
            }

            if (!session.Authorize(Command.Secret))
            {

                logger.LogWarning("Rejected an unauthorized {Command} for rendezvous {SessionId}!",
                                  DisconnectPortsCommand.Name, session.Id);

                // Not even the rendezvous itself is handed out here.
                return (CommandResponse.Error(ResponseCode.Unauthorized,
                                              "The given secret does not authorize this command!"), null);

            }

            session.Close(SessionCloseReason.DisconnectRequested);

            return (CommandResponse.Ok($"{DisconnectPortsCommand.Name}([{String.Join(", ", session.Ports)}])"), session);

        }

        #endregion


        #region Sweep()

        /// <summary>
        /// Close all rendezvous that ran into one of their timeouts.
        /// </summary>
        public void Sweep()
        {

            var now = timeProvider.GetUtcNow();

            foreach (var session in sessions.Values)
            {

                if (session.State == SessionState.Pending &&
                    now - session.CreatedUtc >= options.RendezvousTimeout)
                {

                    logger.LogInformation("Rendezvous {SessionId} on TCP/[{Ports}] timed out: only {Connected} of {Expected} clients arrived within {Timeout}.",
                                          session.Id, String.Join(", ", session.Ports), session.ConnectedClients, session.Ports.Count, options.RendezvousTimeout);

                    session.Close(SessionCloseReason.RendezvousTimeout);

                }

                else if (session.State == SessionState.Established &&
                         now - session.LastActivityUtc >= options.IdleTimeout)
                {

                    logger.LogInformation("Rendezvous {SessionId} on TCP/[{Ports}] was idle for more than {Timeout}.",
                                          session.Id, String.Join(", ", session.Ports), options.IdleTimeout);

                    session.Close(SessionCloseReason.IdleTimeout);

                }

            }

        }

        #endregion

        #region TryGetSessionByPort(Port, out Session)

        /// <summary>
        /// Try to get the rendezvous owning the given TCP port.
        /// </summary>
        /// <param name="Port">A TCP port.</param>
        /// <param name="Session">The rendezvous owning the given TCP port.</param>
        public Boolean TryGetSessionByPort(UInt16 Port, out RendezvousSession? Session)

            => portOwners.TryGetValue(Port, out Session);

        #endregion


        #region (private) BindRandomPort(Address, Backlog, UsedPorts)

        /// <summary>
        /// Bind a free TCP port.
        /// </summary>
        /// <param name="Address">The IP address to bind to.</param>
        /// <param name="Backlog">The TCP listen backlog.</param>
        /// <param name="UsedPorts">The TCP ports already used by this rendezvous.</param>
        /// <exception cref="SocketException">When no free TCP port could be found.</exception>
        private SessionEndpoint BindRandomPort(IPAddress       Address,
                                               Int32           Backlog,
                                               HashSet<UInt16> UsedPorts)
        {

            #region Let the operating system pick a free port...

            var endpoint = SessionEndpoint.Bind(Address, 0, PortSpecification.Random, Backlog);

            if ( options.IsAllowedDataPort(endpoint.Port) &&
                !portOwners.ContainsKey(endpoint.Port)    &&
                 UsedPorts.Add(endpoint.Port))
            {
                return endpoint;
            }

            // The operating system handed out a port outside of the configured
            // port range, or a port that is about to be released by another rendezvous.
            endpoint.Dispose();

            #endregion

            #region ...or look for one within the configured port range

            for (var attempt = 0; attempt < RandomPortAttempts; attempt++)
            {

                var candidate = (UInt16) Random.Shared.Next(options.MinDataPort,
                                                            options.MaxDataPort + 1);

                if (portOwners.ContainsKey(candidate) ||
                    UsedPorts.Contains(candidate)     ||
                   !options.IsAllowedDataPort(candidate))
                {
                    continue;
                }

                try
                {

                    var randomEndpoint = SessionEndpoint.Bind(Address, candidate, PortSpecification.Random, Backlog);
                    UsedPorts.Add(candidate);

                    return randomEndpoint;

                }
                catch (SocketException)
                {
                    // Somebody else is using this port: try the next one.
                }

            }

            #endregion

            throw new SocketException((Int32) SocketError.AddressAlreadyInUse);

        }

        #endregion

        #region (private) SessionClosed(Session)

        /// <summary>
        /// Remove a closed rendezvous and release its TCP ports.
        /// </summary>
        private void SessionClosed(RendezvousSession Session)
        {

            sessions.TryRemove(new KeyValuePair<Guid, RendezvousSession>(Session.Id, Session));

            foreach (var port in Session.Ports)
                portOwners.TryRemove(new KeyValuePair<UInt16, RendezvousSession>(port, Session));

        }

        #endregion


        #region DisposeAsync()

        /// <summary>
        /// Close all rendezvous and wait until all TCP ports are free again.
        /// </summary>
        public async ValueTask DisposeAsync()
        {

            var openSessions = sessions.Values.ToArray();

            foreach (var session in openSessions)
                session.Close(SessionCloseReason.ServiceShutdown);

            foreach (var session in openSessions)
            {
                try
                {
                    await session.Completion.ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Rendezvous {SessionId}: shutdown error.", session.Id);
                }
            }

            sessions.Clear();
            portOwners.Clear();

        }

        #endregion

    }

}
