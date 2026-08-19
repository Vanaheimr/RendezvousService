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
    /// Close an existing rendezvous and remove its TCP listeners.
    /// All given ports must belong to the same rendezvous.
    ///
    ///     DisconnectPorts(20000, 30000, secret)
    ///     DisconnectPorts([20000, 30000], secret)
    ///     DisconnectPorts(20000, secret)
    ///
    /// </summary>
    /// <param name="Ports">One or more TCP ports of the rendezvous.</param>
    /// <param name="Secret">The secret given when the rendezvous was opened.</param>
    public sealed record DisconnectPortsCommand(IReadOnlyList<UInt16>  Ports,
                                                String                 Secret)

        : RendezvousCommand(Secret)

    {

        /// <summary>
        /// The name of this command.
        /// </summary>
        public const String Name = "DisconnectPorts";

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

            => $"{Name}([{String.Join(", ", Ports)}], ***)";

    }

}
