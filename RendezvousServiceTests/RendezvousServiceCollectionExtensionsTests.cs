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
using Microsoft.Extensions.Options;

using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;
using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for the composition of the generic host: what Program.cs registers,
    /// put together and started the way Program.cs does it.
    /// </summary>
    [TestFixture]
    public class RendezvousServiceCollectionExtensionsTests
    {

        #region (private) BuildHost(params Settings)

        /// <summary>
        /// Build a host from the given settings alone. An empty application builder
        /// reads neither appsettings.json nor the environment, so that nothing on
        /// the test machine can change what a test sees.
        /// </summary>
        private static IHost BuildHost(params (String Key, String Value)[] Settings)
        {

            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

            // Nothing may listen on a fixed TCP port, nor on any other interface.
            // A test overrides these with a source of its own, added later.
            builder.Configuration.AddInMemoryCollection(
                                      [
                                          new ("Rendezvous:ControlAddress",   "127.0.0.1"),
                                          new ("Rendezvous:ControlPort",      "0"),
                                          new ("Rendezvous:DataAddress",      "127.0.0.1"),
                                          new ("Rendezvous:AutoMaintenance",  "false")
                                      ]
                                  ).
                                  AddInMemoryCollection(
                                      Settings.Select(setting => new KeyValuePair<String, String?>(setting.Key, setting.Value))
                                  );

            builder.Services.AddRendezvousService(builder.Configuration);

            return builder.Build();

        }

        #endregion


        [Test]
        public async Task TheHostStartsAndStopsTheControlEndpoint()
        {

            using var host = BuildHost();

            var service = host.Services.GetServices<IHostedService>().OfType<ControlServerService>().Single();

            Assert.That(service.ControlServer.IsRunning, Is.False,
                        "Nothing may listen before the host was started!");

            await host.StartAsync();

            Assert.Multiple(() => {
                Assert.That(service.ControlServer.IsRunning,    Is.True);
                Assert.That(service.ControlServer.LocalSocket,  Is.Not.Null);
            });

            await host.StopAsync();

            Assert.That(service.ControlServer.IsRunning, Is.False);

        }

        [Test]
        public async Task AnInvalidConfigurationFailsTheStartWithEveryProblem()
        {

            using var host = BuildHost(("Rendezvous:ControlAddress", "nonsense"),
                                       ("Rendezvous:MaxSessions",    "0"));

            var exception = await Assert.ThrowsAsync<OptionsValidationException>(
                                      async () => await host.StartAsync()
                                  );

            Assert.That(exception!.Failures, Has.Exactly(2).Items,
                        "The host shall see all problems at once, not one per restart.");

        }

        [Test]
        public void TheConfigurationIsValidatedOnStart()
        {

            using var host = BuildHost(("Rendezvous:MaxSessions", "0"));

            // The test above would pass without ValidateOnStart as well: the control
            // endpoint reads - and thereby validates - the configuration in its
            // constructor. ValidateOnStart is what keeps that true when it no longer
            // does, so it is asked for here directly.
            var validator = host.Services.GetService<IStartupValidator>();

            Assert.That(validator, Is.Not.Null, "ValidateOnStart is missing!");
            Assert.Throws<OptionsValidationException>(validator!.Validate);

        }

        [Test]
        public void TheManagerIsBuiltFromTheConfiguration()
        {

            using var host = BuildHost(("Rendezvous:MaxPortsPerSession", "2"));

            var manager = host.Services.GetRequiredService<RendezvousManager>();

            // 16 ports per rendezvous by default, so only the configured
            // value can turn three of them down.
            var success = manager.TryConnectPorts(
                              new ConnectPortsCommand([PortSpecification.Random, PortSpecification.Random, PortSpecification.Random]),
                              out var session,
                              out var response
                          );

            Assert.Multiple(() => {
                Assert.That(success,        Is.False);
                Assert.That(session,        Is.Null);
                Assert.That(response.Code,  Is.EqualTo(ResponseCode.TooManyPorts), response.Text);
            });

        }

        [Test]
        public void TheManagerIsASingleton()
        {

            using var host = BuildHost();

            var first  = host.Services.GetRequiredService<RendezvousManager>();
            var second = host.Services.GetRequiredService<RendezvousManager>();

            Assert.That(second, Is.SameAs(first),
                        "The control endpoint and everybody else must see the same rendezvous.");

        }

    }

}
