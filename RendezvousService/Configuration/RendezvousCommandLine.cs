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

using Microsoft.Extensions.Configuration;

using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// The command line switches of the rendezvous service: short names for the
    /// settings an operator changes most often. Every other setting still has its
    /// long form, e.g. --Rendezvous:MaxSessions 42.
    ///
    /// The configuration keys are built with nameof, because the configuration
    /// binder silently ignores a key that names no property: a switch for a
    /// property the library renamed would otherwise still be accepted, and do
    /// nothing at all. This way it no longer compiles.
    /// </summary>
    public static class RendezvousCommandLine
    {

        #region Switches

        /// <summary>
        /// The command line switches and the configuration keys they set.
        /// </summary>
        public static IReadOnlyDictionary<String, String> Switches { get; }

            = new Dictionary<String, String> {
                  { "--control-address",     $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.ControlAddress)}"     },
                  { "--control-port",        $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.ControlPort)}"        },
                  { "--data-address",        $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.DataAddress)}"        },
                  { "--min-port",            $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.MinDataPort)}"        },
                  { "--max-port",            $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.MaxDataPort)}"        },
                  { "--rendezvous-timeout",  $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.RendezvousTimeout)}"  },
                  { "--idle-timeout",        $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.IdleTimeout)}"        },
                  { "--profile",             $"{RendezvousOptions.SectionName}:{nameof(RendezvousOptions.DefaultProfile)}"     }
              }.AsReadOnly();

        #endregion


        #region AddRendezvousCommandLine(this ConfigurationBuilder, Args)

        /// <summary>
        /// Add the command line arguments, with the switches of the rendezvous service.
        ///
        /// Added last, the command line has the last word over appsettings.json
        /// and the environment variables.
        /// </summary>
        /// <param name="ConfigurationBuilder">A configuration builder.</param>
        /// <param name="Args">The command line arguments.</param>
        public static IConfigurationBuilder AddRendezvousCommandLine(this IConfigurationBuilder  ConfigurationBuilder,
                                                                     String[]                    Args)

            => ConfigurationBuilder.AddCommandLine(
                   Args,
                   new Dictionary<String, String>(Switches)
               );

        #endregion

    }

}
