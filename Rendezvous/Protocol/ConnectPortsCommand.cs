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

namespace org.GraphDefined.Vanaheimr.Rendezvous
{

    /// <summary>
    /// Open a new rendezvous: The service starts one TCP listener per given port
    /// and connects the arriving clients with each other.
    ///
    ///     ConnectPorts([20000, 30000], secret)
    ///     ConnectPorts([?, 30000],     secret)
    ///     ConnectPorts([20000, ?],     secret)
    ///     ConnectPorts([?, ?],         secret)
    ///     ConnectPorts([?, ?, ?],      secret, Interactive)
    ///
    /// </summary>
    /// <param name="Ports">Two or more requested TCP ports. '?' asks the service to pick a free port.</param>
    /// <param name="Secret">The secret authorizing a later DisconnectPorts command.</param>
    /// <param name="Profile">An optional transfer profile, null uses the configured default.</param>
    public sealed record ConnectPortsCommand(IReadOnlyList<PortSpecification>  Ports,
                                             String                           Secret,
                                             TransferProfile?                 Profile = null)

        : RendezvousCommand(Secret)

    {

        /// <summary>
        /// The name of this command.
        /// </summary>
        public const String Name = "ConnectPorts";

        /// <summary>
        /// The name of this command.
        /// </summary>
        public override String CommandName
            => Name;

        /// <summary>
        /// Return a text representation of this command.
        /// The secret is masked, as commands are written to the log.
        /// </summary>
        public override String ToString()

            => $"{Name}([{String.Join(", ", Ports)}], ***{(Profile.HasValue ? $", {Profile.Value.AsText()}" : "")})";

    }

}
