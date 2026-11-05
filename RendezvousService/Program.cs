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

// The host looks for appsettings.json in its content root, and that is the
// working directory unless told otherwise: a systemd unit without a
// WorkingDirectory, or a 'dotnet run --project' from the repository, would
// silently run on the defaults instead. The settings file is copied next to
// the binary, so that is where the content root belongs.
//
// Both other steps live in their own types, so that the tests compose the very
// same host: RendezvousCommandLine and RendezvousServiceCollectionExtensions.
var builder  = Host.CreateApplicationBuilder(
                   new HostApplicationBuilderSettings {
                       Args             = args,
                       ContentRootPath  = AppContext.BaseDirectory
                   }
               );

builder.Configuration.AddRendezvousCommandLine(args);
builder.Services.     AddRendezvousService    (builder.Configuration);

// 'using' because the console logger writes from a queue of its own, on a
// background thread: a process that ends on a bad configuration below must
// flush it first, or the one line saying what was wrong can be lost. Disposing
// the host is what flushes it. That is a race, and the logger usually wins it,
// so no test can show this line missing - which is why it is written down.
using var host = builder.Build();
var logger     = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Rendezvous");

#region Log what this was built from

// Before the configuration is read, so that a log of a service that refused
// its configuration still says which code refused it.
logger.LogInformation("Built from {BuiltFrom}.",
                      String.Join(", ", BuildProvenance.BuiltFrom()));

#endregion

#region Read the configuration, or say what is wrong with it

// The first read binds and validates, and either can fail on what an operator
// wrote: a value the binder can not convert (a port of 70000) throws an
// InvalidOperationException naming that one value, a value the validator
// rejects throws an OptionsValidationException listing every problem at once.
//
// Both are a mistake in the configuration, not in the code, so they end the
// process with one log line per problem and exit code 1, not with an unhandled
// exception and a stack trace nobody needs. The try holds this one read and
// nothing else, so that is all an InvalidOperationException here can be about.
RendezvousOptions options;

try
{
    options = host.Services.GetRequiredService<IOptions<RendezvousOptions>>().Value;
}
catch (OptionsValidationException e)
{

    foreach (var failure in e.Failures)
        logger.LogCritical("Invalid configuration: {Problem}", failure);

    return 1;

}
catch (InvalidOperationException e)
{

    logger.LogCritical("Invalid configuration: {Problem}",
                       e.InnerException is null
                           ? e.Message
                           : $"{e.Message} {e.InnerException.Message}");

    return 1;

}

#endregion

#region Log the effective configuration

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

#region Start, and run until stopped

// What RunAsync does, in two steps, so that only the start is caught.
//
// A control port that is already taken fails the start of the hosted service.
// The host has logged that already, with the exception and its stack trace
// ("Hosting failed to start"); letting it escape as well would print the very
// same stack trace a second time as an unhandled exception, and end the
// process as a crash instead of with an exit code.
//
// Nothing after the start is caught: a failure while stopping or disposing is
// a different kind of failure, and must stay an unhandled exception rather
// than vanish behind an exit code.
try
{
    await host.StartAsync();
}
catch (Exception)
{
    return 1;
}

await host.WaitForShutdownAsync();

#endregion

return 0;
