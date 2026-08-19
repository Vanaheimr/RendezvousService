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

namespace org.GraphDefined.Vanaheimr.Rendezvous.Tests
{

    /// <summary>
    /// Tests for the rendezvous and idle timeouts, using a controllable clock.
    /// </summary>
    [TestFixture]
    public class TimeoutTests
    {

        #region The rendezvous timeout

        [Test]
        public async Task RendezvousTimeout_ClosesARendezvousWhereNobodyArrived()
        {

            await using var host = RendezvousTestHost.Create();

            var ports    = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));
            var session  = host.Session;

            host.Time.Advance(TimeSpan.FromMinutes(4));
            host.Manager.Sweep();

            Assert.That(session.State, Is.EqualTo(SessionState.Pending),
                        "The default rendezvous timeout is 5 minutes!");

            host.Time.Advance(TimeSpan.FromMinutes(1));
            host.Manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.Multiple(() => {
                Assert.That(session.State,                Is.EqualTo(SessionState.Closed));
                Assert.That(session.CloseReason,          Is.EqualTo(SessionCloseReason.RendezvousTimeout));
                Assert.That(host.Manager.Count,           Is.Zero);
                Assert.That(TestNet.IsPortFree(ports[0]), Is.True, "The listeners must be removed!");
                Assert.That(TestNet.IsPortFree(ports[1]), Is.True, "The listeners must be removed!");
            });

        }

        [Test]
        public async Task RendezvousTimeout_ClosesARendezvousWhereOnlyOneClientArrived()
        {

            await using var host = RendezvousTestHost.Create();

            var ports    = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));
            var session  = host.Session;

            using var alice = await TestNet.ConnectAsync(ports[0]);

            await TestNet.WaitUntilAsync(() => session.ConnectedClients == 1,
                                         "The first client did not arrive!");

            host.Time.Advance(TimeSpan.FromMinutes(5));
            host.Manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            await TestNet.ExpectEndOfStreamAsync(alice);

            Assert.Multiple(() => {
                Assert.That(session.State,       Is.EqualTo(SessionState.Closed));
                Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.RendezvousTimeout));
            });

        }

        [Test]
        public async Task RendezvousTimeout_IsConfigurable()
        {

            await using var host = RendezvousTestHost.Create(options => options.RendezvousTimeout = TimeSpan.FromSeconds(30));

            host.ExecuteOk("ConnectPorts([?,?], s3cret)");

            var session = host.Session;

            host.Time.Advance(TimeSpan.FromSeconds(29));
            host.Manager.Sweep();

            Assert.That(session.State, Is.EqualTo(SessionState.Pending));

            host.Time.Advance(TimeSpan.FromSeconds(1));
            host.Manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.RendezvousTimeout));

        }

        [Test]
        public async Task RendezvousTimeout_DoesNotCloseAnEstablishedRendezvous()
        {

            await using var host = RendezvousTestHost.Create();

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));

            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Session.State == SessionState.Established,
                                         "The rendezvous was not established!");

            host.Time.Advance(TimeSpan.FromMinutes(9));
            host.Manager.Sweep();

            Assert.That(host.Session.State, Is.EqualTo(SessionState.Established),
                        "An established rendezvous is only limited by the idle timeout!");

        }

        #endregion

        #region The idle timeout

        [Test]
        public async Task IdleTimeout_ClosesAnEstablishedRendezvousWithoutPayload()
        {

            await using var host = RendezvousTestHost.Create();

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));

            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Session.State == SessionState.Established,
                                         "The rendezvous was not established!");

            var session = host.Session;

            host.Time.Advance(TimeSpan.FromMinutes(10));
            host.Manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            await TestNet.ExpectEndOfStreamAsync(alice);
            await TestNet.ExpectEndOfStreamAsync(bob);

            Assert.Multiple(() => {
                Assert.That(session.State,                Is.EqualTo(SessionState.Closed));
                Assert.That(session.CloseReason,          Is.EqualTo(SessionCloseReason.IdleTimeout));
                Assert.That(TestNet.IsPortFree(ports[0]), Is.True, "The listeners must be removed!");
            });

        }

        [Test]
        public async Task IdleTimeout_IsResetByPayload()
        {

            await using var host = RendezvousTestHost.Create();

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));

            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Session.State == SessionState.Established,
                                         "The rendezvous was not established!");

            var session = host.Session;

            #region 9 minutes without payload are fine

            host.Time.Advance(TimeSpan.FromMinutes(9));
            host.Manager.Sweep();

            Assert.That(session.State, Is.EqualTo(SessionState.Established));

            #endregion

            #region A single byte resets the idle timeout

            await TestNet.SendAsync(alice, "?");
            Assert.That(await TestNet.ReceiveAsync(bob, 1), Is.EqualTo("?"));

            host.Time.Advance(TimeSpan.FromMinutes(9));
            host.Manager.Sweep();

            Assert.That(session.State, Is.EqualTo(SessionState.Established),
                        "The idle timeout must be measured from the last payload!");

            #endregion

            #region ...and 10 minutes later it is over

            host.Time.Advance(TimeSpan.FromMinutes(1));
            host.Manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.IdleTimeout));

            #endregion

        }

        [Test]
        public async Task IdleTimeout_IsConfigurable()
        {

            await using var host = RendezvousTestHost.Create(options => options.IdleTimeout = TimeSpan.FromMinutes(1));

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));

            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Session.State == SessionState.Established,
                                         "The rendezvous was not established!");

            var session = host.Session;

            host.Time.Advance(TimeSpan.FromMinutes(1));
            host.Manager.Sweep();

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.IdleTimeout));

        }

        #endregion

        #region The session janitor

        [Test]
        public async Task SessionJanitor_SweepsPeriodically()
        {

            await using var host = RendezvousTestHost.Create(options => options.MaintenanceInterval = TimeSpan.FromSeconds(15));

            host.ExecuteOk("ConnectPorts([?,?], s3cret)");

            var session   = host.Session;

            var janitor   = new SessionJanitor(host.Manager,
                                               Microsoft.Extensions.Options.Options.Create(host.Configuration),
                                               host.Time,
                                               Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionJanitor>.Instance);

            await janitor.StartAsync(CancellationToken.None);

            try
            {

                // Wait until the janitor really watches the clock, otherwise
                // the time below would be advanced before its timer exists.
                await janitor.Running.WaitAsync(TestNet.Timeout);

                // The janitor is running on the same controllable clock.
                host.Time.Advance(TimeSpan.FromMinutes(6));

                await session.Completion.WaitAsync(TestNet.Timeout);

                Assert.That(session.CloseReason, Is.EqualTo(SessionCloseReason.RendezvousTimeout));

            }
            finally
            {
                await janitor.StopAsync(CancellationToken.None);
                janitor.Dispose();
            }

        }

        #endregion

    }

}
