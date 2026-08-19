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

using Microsoft.Extensions.Time.Testing;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Tests
{

    /// <summary>
    /// Tests for using the library on its own: no hosting, no dependency
    /// injection, no configuration binder and no control endpoint - just
    /// "new RendezvousManager(...)" and a few method calls.
    /// </summary>
    [TestFixture]
    public class LibraryUsageTests
    {

        #region A manager without anything around it

        [Test]
        public async Task AManagerNeedsNoArgumentsAtAll()
        {

            // No options, no time provider, no logger factory, no host.
            await using var manager = new RendezvousManager();

            Assert.Multiple(() => {
                Assert.That(manager.Count,      Is.Zero);
                Assert.That(manager.Sessions,   Is.Empty);
                Assert.That(manager.Janitor,    Is.Not.Null, "The manager looks after its timeouts by default.");
            });

        }

        [Test]
        public async Task AManagerRelaysDataWithoutAnyFramework()
        {

            await using var manager = new RendezvousManager(
                                          new RendezvousOptions {
                                              DataAddress = "127.0.0.1"
                                          }
                                      );

            if (!manager.TryConnectPorts(
                     new ConnectPortsCommand(
                         [PortSpecification.Random, PortSpecification.Random],
                         "s3cret",
                         TransferProfile.Interactive
                     ),
                     out var session,
                     out var response))
            {
                Assert.Fail(response.ToProtocolLine());
                return;
            }

            using var alice = await TestNet.ConnectAsync(session.Ports[0]);
            using var bob   = await TestNet.ConnectAsync(session.Ports[1]);

            await TestNet.WaitUntilAsync(() => session.State == SessionState.Established,
                                         "The rendezvous was not established!");

            await TestNet.SendAsync(alice, "Hello Bob!");
            Assert.That(await TestNet.ReceiveAsync(bob, 10), Is.EqualTo("Hello Bob!"));

            manager.DisconnectPorts(new DisconnectPortsCommand([session.Ports[0]], "s3cret"));

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.That(session.State, Is.EqualTo(SessionState.Closed));

        }

        [Test]
        public void AnInvalidConfigurationIsRejectedRightAway()
        {

            var exception = Assert.Throws<ArgumentException>(
                                () => new RendezvousManager(
                                          new RendezvousOptions { DataAddress = "not an IP address" }
                                      )
                            );

            Assert.That(exception!.Message, Does.Contain("DataAddress"));

        }

        #endregion

        #region The automatic maintenance

        [Test]
        public async Task AManagerClosesTimedOutRendezvousOnItsOwn()
        {

            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

            await using var manager = new RendezvousManager(
                                          new RendezvousOptions {
                                              DataAddress          = "127.0.0.1",
                                              RendezvousTimeout    = TimeSpan.FromMinutes(5),
                                              MaintenanceInterval  = TimeSpan.FromSeconds(15)
                                              // AutoMaintenance is enabled by default
                                          },
                                          time
                                      );

            manager.TryConnectPorts(
                new ConnectPortsCommand([PortSpecification.Random, PortSpecification.Random], "s3cret"),
                out var session,
                out _
            );

            Assert.That(manager.Janitor, Is.Not.Null);

            // The janitor was started by the manager, nobody else had to.
            await manager.Janitor!.Running.WaitAsync(TestNet.Timeout);

            time.Advance(TimeSpan.FromMinutes(6));

            await session!.Completion.WaitAsync(TestNet.Timeout);

            Assert.Multiple(() => {
                Assert.That(session.CloseReason,                  Is.EqualTo(SessionCloseReason.RendezvousTimeout));
                Assert.That(manager.Count,                        Is.Zero);
                Assert.That(TestNet.IsPortFree(session.Ports[0]), Is.True);
            });

        }

        [Test]
        public async Task TheAutomaticMaintenanceCanBeTurnedOff()
        {

            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

            await using var manager = new RendezvousManager(
                                          new RendezvousOptions {
                                              DataAddress      = "127.0.0.1",
                                              AutoMaintenance  = false
                                          },
                                          time
                                      );

            manager.TryConnectPorts(
                new ConnectPortsCommand([PortSpecification.Random, PortSpecification.Random], "s3cret"),
                out var session,
                out _
            );

            Assert.That(manager.Janitor, Is.Null, "Nobody asked for a janitor!");

            time.Advance(TimeSpan.FromHours(1));
            await Task.Delay(250);

            Assert.That(session!.State, Is.EqualTo(SessionState.Pending),
                        "Without maintenance nothing closes by itself.");

            // The caller decides when to look for timeouts.
            manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.RendezvousTimeout));

        }

        [Test]
        public async Task ADisposedManagerStopsItsJanitor()
        {

            var manager = new RendezvousManager(new RendezvousOptions { DataAddress = "127.0.0.1" });
            var janitor = manager.Janitor;

            Assert.That(janitor,           Is.Not.Null);
            Assert.That(janitor!.IsRunning, Is.True);

            await manager.DisposeAsync();

            Assert.That(janitor.IsRunning, Is.False);

        }

        #endregion

        #region A janitor on its own

        [Test]
        public async Task AJanitorCanBeDrivenByTheCaller()
        {

            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

            await using var manager = new RendezvousManager(
                                          new RendezvousOptions {
                                              DataAddress      = "127.0.0.1",
                                              AutoMaintenance  = false
                                          },
                                          time
                                      );

            manager.TryConnectPorts(
                new ConnectPortsCommand([PortSpecification.Random, PortSpecification.Random], "s3cret"),
                out var session,
                out _
            );

            await using var janitor = new SessionJanitor(manager, TimeSpan.FromSeconds(15), time);

            Assert.That(janitor.IsRunning, Is.False, "A janitor does nothing before it was started.");

            janitor.Start();
            janitor.Start();   // starting twice is harmless

            await janitor.Running.WaitAsync(TestNet.Timeout);

            time.Advance(TimeSpan.FromMinutes(6));

            await session!.Completion.WaitAsync(TestNet.Timeout);

            Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.RendezvousTimeout));

            await janitor.StopAsync();

            Assert.That(janitor.IsRunning, Is.False);

        }

        [Test]
        public async Task AJanitorRejectsANonPositiveInterval()
        {

            await using var manager = new RendezvousManager(new RendezvousOptions { AutoMaintenance = false });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SessionJanitor(manager, TimeSpan.Zero)
            );

        }

        #endregion

    }

}
