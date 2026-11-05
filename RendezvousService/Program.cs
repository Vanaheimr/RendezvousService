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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;
using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

// Both steps live in their own types, so that the tests compose the very
// same host: RendezvousCommandLine and RendezvousServiceCollectionExtensions.
var builder  = Host.CreateApplicationBuilder(args);

builder.Configuration.AddRendezvousCommandLine(args);
builder.Services.     AddRendezvousService    (builder.Configuration);

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
