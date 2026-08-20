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

using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;
using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for the hosted service of the application: the ten lines that tie
    /// the control endpoint of the library into the life cycle of the generic host.
    ///
    /// The control endpoint itself is tested within HermodTests.
    /// </summary>
    [TestFixture]
    public class ControlServerServiceTests
    {

        #region (private) CreateService(out Manager)

        private static ControlServerService CreateService(out RendezvousManager  Manager,
                                                          out ControlSigner      Signer)
        {

            // The key travels through the configuration, exactly as it would
            // within appsettings.json.
            Signer = ControlSigner.GenerateEd25519("test-operator");

            var configuration = new RendezvousOptions {
                                    ControlAddress   = "127.0.0.1",
                                    ControlPort      = IPPort.Parse(0),
                                    DataAddress      = "127.0.0.1",
                                    AutoMaintenance  = false,
                                    ControlKeys      = [
                                        new ControlKeyOptions {
                                            Id           = Signer.KeyId,
                                            KeyType      = Signer.KeyType,
                                            PublicKey    = Convert.ToBase64String(Signer.PublicKey),
                                            Description  = "The test operator"
                                        }
                                    ]
                                };

            Manager = new RendezvousManager(configuration,
                                            TimeProvider.System,
                                            NullLoggerFactory.Instance);

            return new ControlServerService(Manager,
                                            Options.Create(configuration),
                                            NullLoggerFactory.Instance);

        }

        #endregion


        [Test]
        public async Task TheHostStartsAndStopsTheControlEndpoint()
        {

            await using var service = CreateService(out var manager, out var signer);
            await using var _       = manager;

            Assert.That(service.ControlServer.IsRunning, Is.False,
                        "Nothing may listen before the host started the service!");

            await ((IHostedService) service).StartAsync(CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(service.ControlServer.IsRunning,           Is.True);
                Assert.That(service.ControlServer.LocalSocket,       Is.Not.Null);
                Assert.That(service.ControlServer.LocalSocket!.Value.Port.ToUInt16(), Is.GreaterThan(0));
            });

            await ((IHostedService) service).StopAsync(CancellationToken.None);

            Assert.That(service.ControlServer.IsRunning, Is.False);

        }

        [Test]
        public async Task TheHostedControlEndpointExecutesCommands()
        {

            await using var service = CreateService(out var manager, out var signer);
            await using var _       = manager;
            using var       __      = signer;

            await ((IHostedService) service).StartAsync(CancellationToken.None);

            try
            {

                var serverKeys = new ControlKeyRing();
                serverKeys.Add(service.ControlServer.ResponseKey);

                using var client = new RendezvousControlClient(
                                       service.ControlServer.LocalSocket!.Value,
                                       signer,
                                       serverKeys,
                                       TimeSpan.FromSeconds(10)
                                   );

                var response = await client.ConnectPortsAsync(
                                         [PortSpecification.Random, PortSpecification.Random],
                                         Description: "A rendezvous of the hosted service"
                                     );

                Assert.Multiple(() => {
                    Assert.That(response.IsSuccess,    Is.True, response.Message);
                    Assert.That(response.Ports,        Has.Count.EqualTo(2), "The configured key must authorize the command.");
                    Assert.That(response.SignedBy,     Has.Count.EqualTo(1));
                    Assert.That(response.Description,  Is.EqualTo("A rendezvous of the hosted service"));
                    Assert.That(manager.Count,         Is.EqualTo(1));
                });

            }
            finally
            {
                await ((IHostedService) service).StopAsync(CancellationToken.None);
            }

        }

        [Test]
        public async Task AnOccupiedControlPortFailsTheStartup()
        {

            var occupied = new TcpListener(System.Net.IPAddress.Loopback, 0);
            occupied.Start();

            try
            {

                var configuration = new RendezvousOptions {
                                        ControlAddress   = "127.0.0.1",
                                        ControlPort      = IPPort.Parse(((IPEndPoint) occupied.LocalEndpoint).Port),
                                        DataAddress      = "127.0.0.1",
                                        AutoMaintenance  = false
                                    };

                await using var manager = new RendezvousManager(configuration,
                                                                TimeProvider.System,
                                                                NullLoggerFactory.Instance);

                await using var service = new ControlServerService(manager,
                                                                   Options.Create(configuration),
                                                                   NullLoggerFactory.Instance);

                // The host must not come up believing everything is fine.
                Assert.ThrowsAsync<SocketException>(
                    async () => await ((IHostedService) service).StartAsync(CancellationToken.None)
                );

            }
            finally
            {
                occupied.Stop();
                occupied.Dispose();
            }

        }

    }

}
