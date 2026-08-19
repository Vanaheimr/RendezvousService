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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using org.GraphDefined.Vanaheimr.Rendezvous;
using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

var builder = Host.CreateApplicationBuilder(args);

#region Command line switches

builder.Configuration.AddCommandLine(
    args,
    new Dictionary<String, String> {
        { "--control-address",     $"{RendezvousOptions.SectionName}:ControlAddress"     },
        { "--control-port",        $"{RendezvousOptions.SectionName}:ControlPort"        },
        { "--data-address",        $"{RendezvousOptions.SectionName}:DataAddress"        },
        { "--min-port",            $"{RendezvousOptions.SectionName}:MinDataPort"        },
        { "--max-port",            $"{RendezvousOptions.SectionName}:MaxDataPort"        },
        { "--rendezvous-timeout",  $"{RendezvousOptions.SectionName}:RendezvousTimeout"  },
        { "--idle-timeout",        $"{RendezvousOptions.SectionName}:IdleTimeout"        },
        { "--profile",             $"{RendezvousOptions.SectionName}:DefaultProfile"     }
    }
);

#endregion

#region Services

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IValidateOptions<RendezvousOptions>, RendezvousOptionsValidator>();

builder.Services.AddOptions<RendezvousOptions>().
                 Bind(builder.Configuration.GetSection(RendezvousOptions.SectionName)).
                 ValidateOnStart();

// The library knows nothing about IOptions or dependency injection, the
// application hands it what it needs. The manager looks after its own
// timeouts, therefore there is no janitor to register here.
builder.Services.AddSingleton(serviceProvider => new RendezvousManager(
                                                     serviceProvider.GetRequiredService<IOptions<RendezvousOptions>>().Value,
                                                     serviceProvider.GetRequiredService<TimeProvider>(),
                                                     serviceProvider.GetRequiredService<ILoggerFactory>()
                                                 ));

builder.Services.AddHostedService<ControlServerService>();

#endregion

var host     = builder.Build();

#region Log the effective configuration

var options  = host.Services.GetRequiredService<IOptions<RendezvousOptions>>().Value;
var logger   = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Rendezvous");

logger.LogInformation("Rendezvous ports {MinDataPort} - {MaxDataPort} on {DataAddress}, " +
                      "rendezvous timeout {RendezvousTimeout}, idle timeout {IdleTimeout}, " +
                      "default transfer profile {DefaultProfile}.",
                      options.MinDataPort,
                      options.MaxDataPort,
                      options.DataAddress,
                      options.RendezvousTimeout,
                      options.IdleTimeout,
                      options.DefaultProfile.AsText());

#endregion

await host.RunAsync();
