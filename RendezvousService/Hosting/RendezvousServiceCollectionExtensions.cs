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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// Registers the rendezvous service within a generic host.
    ///
    /// Program.cs calls this and nothing else, so that a test can compose the
    /// very same host - which top-level statements would not allow.
    /// </summary>
    public static class RendezvousServiceCollectionExtensions
    {

        #region AddRendezvousService(this Services, Configuration)

        /// <summary>
        /// Register everything the rendezvous service consists of: its configuration,
        /// validated during startup, the rendezvous manager, and the hosted control endpoint.
        /// </summary>
        /// <param name="Services">A service collection.</param>
        /// <param name="Configuration">The configuration holding the section 'Rendezvous'.</param>
        public static IServiceCollection AddRendezvousService(this IServiceCollection  Services,
                                                              IConfiguration           Configuration)
        {

            Services.AddSingleton(TimeProvider.System);

            Services.AddSingleton<IValidateOptions<RendezvousOptions>, RendezvousOptionsValidator>();

            Services.AddOptions<RendezvousOptions>().
                     Bind(Configuration.GetSection(RendezvousOptions.SectionName)).
                     ValidateOnStart();

            // The library knows nothing about IOptions or dependency injection, the
            // application hands it what it needs. The manager looks after its own
            // timeouts, therefore there is no janitor to register here.
            Services.AddSingleton(serviceProvider => new RendezvousManager(
                                                         serviceProvider.GetRequiredService<IOptions<RendezvousOptions>>().Value,
                                                         serviceProvider.GetRequiredService<TimeProvider>(),
                                                         serviceProvider.GetRequiredService<ILoggerFactory>()
                                                     ));

            Services.AddHostedService<ControlServerService>();

            return Services;

        }

        #endregion

    }

}
