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

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Tests
{

    /// <summary>
    /// Tests for opening and closing rendezvous.
    /// </summary>
    [TestFixture]
    public class RendezvousManagerTests
    {

        #region ConnectPorts

        [Test]
        public async Task ConnectPorts_WithTwoRandomPorts_OpensTwoListeners()
        {

            await using var host = RendezvousTestHost.Create();

            var response  = host.ExecuteOk("ConnectPorts([?,?], s3cret)");
            var ports     = TestNet.ParsePorts(response);

            Assert.Multiple(() => {
                Assert.That(ports,          Has.Length.EqualTo(2));
                Assert.That(ports[0],       Is.Not.EqualTo(ports[1]), "Both endpoints need their own TCP port!");
                Assert.That(response.Text,  Does.EndWith(", Balanced)"), "The response echoes the effective transfer profile.");
                Assert.That(host.Manager.Count, Is.EqualTo(1));
            });

            // Both TCP ports are accepting connections.
            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Session.State == SessionState.Established,
                                         "The rendezvous was not established!");

        }

        [Test]
        public async Task ConnectPorts_WithTwoFixedPorts_UsesExactlyThosePorts()
        {

            await using var host = RendezvousTestHost.Create();

            var freePorts  = TestNet.GetFreePorts(2);
            var response   = host.ExecuteOk($"ConnectPorts([{freePorts[0]}, {freePorts[1]}], s3cret)");

            Assert.That(response.Text, Is.EqualTo($"ConnectPorts([{freePorts[0]}, {freePorts[1]}], Balanced)"));

            using var alice = await TestNet.ConnectAsync(freePorts[0]);
            using var bob   = await TestNet.ConnectAsync(freePorts[1]);

        }

        [Test]
        public async Task ConnectPorts_WithOneFixedAndOneRandomPort()
        {

            await using var host = RendezvousTestHost.Create();

            var fixedPort  = TestNet.GetFreePorts(1)[0];
            var response   = host.ExecuteOk($"ConnectPorts([{fixedPort}, ?], s3cret)");
            var ports      = TestNet.ParsePorts(response);

            Assert.Multiple(() => {
                Assert.That(ports[0], Is.EqualTo(fixedPort), "The fixed port must keep its position within the list!");
                Assert.That(ports[1], Is.Not.EqualTo(fixedPort));
            });

        }

        [Test]
        public async Task ConnectPorts_WithThreePorts_OpensThreeListeners()
        {

            await using var host = RendezvousTestHost.Create();

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?,?], s3cret)"));

            Assert.Multiple(() => {
                Assert.That(ports,                     Has.Length.EqualTo(3));
                Assert.That(ports.Distinct().Count(),  Is.EqualTo(3));
                Assert.That(host.Session.Endpoints,    Has.Count.EqualTo(3));
            });

        }

        [Test]
        public async Task ConnectPorts_WithARestrictedPortRange_PicksPortsWithinThatRange()
        {

            await using var host = RendezvousTestHost.Create(options => {
                                       options.MinDataPort  = 45000;
                                       options.MaxDataPort  = 45999;
                                   });

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));

            Assert.Multiple(() => {
                Assert.That(ports[0], Is.InRange((UInt16) 45000, (UInt16) 45999));
                Assert.That(ports[1], Is.InRange((UInt16) 45000, (UInt16) 45999));
            });

        }

        #endregion

        #region ConnectPorts errors

        [Test]
        public async Task ConnectPorts_WithAnAlreadyUsedPort_Fails()
        {

            await using var host = RendezvousTestHost.Create();

            var occupied = new TcpListener(IPAddress.Loopback, 0);
            occupied.Start();

            try
            {

                var port      = (UInt16) ((IPEndPoint) occupied.LocalEndpoint).Port;
                var response  = host.Execute($"ConnectPorts([{port}, ?], s3cret)");

                Assert.Multiple(() => {
                    Assert.That(response.Code,      Is.EqualTo(ResponseCode.PortInUse));
                    Assert.That(host.Manager.Count, Is.Zero, "A failed ConnectPorts must not leak a rendezvous!");
                });

            }
            finally
            {
                occupied.Stop();
                occupied.Dispose();
            }

        }

        [Test]
        public async Task ConnectPorts_WhenOnePortFails_ReleasesTheOtherPortsAgain()
        {

            await using var host = RendezvousTestHost.Create();

            var freePort  = TestNet.GetFreePorts(1)[0];
            var occupied  = new TcpListener(IPAddress.Loopback, 0);
            occupied.Start();

            try
            {

                var occupiedPort  = (UInt16) ((IPEndPoint) occupied.LocalEndpoint).Port;
                var response      = host.Execute($"ConnectPorts([{freePort}, {occupiedPort}], s3cret)");

                Assert.Multiple(() => {
                    Assert.That(response.Code,                 Is.EqualTo(ResponseCode.PortInUse));
                    Assert.That(host.Manager.Count,            Is.Zero);
                    Assert.That(TestNet.IsPortFree(freePort),  Is.True, "The already bound TCP port must be released again!");
                });

            }
            finally
            {
                occupied.Stop();
                occupied.Dispose();
            }

        }

        [Test]
        public async Task ConnectPorts_WithAPortOfAnotherRendezvous_Fails()
        {

            await using var host = RendezvousTestHost.Create();

            var ports     = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));
            var response  = host.Execute($"ConnectPorts([{ports[0]}, ?], anotherSecret)");

            Assert.Multiple(() => {
                Assert.That(response.Code,      Is.EqualTo(ResponseCode.PortInUse));
                Assert.That(host.Manager.Count, Is.EqualTo(1));
            });

        }

        [Test]
        public async Task ConnectPorts_WithAPortOutsideOfThePortRange_Fails()
        {

            await using var host = RendezvousTestHost.Create(options => {
                                       options.MinDataPort  = 40000;
                                       options.MaxDataPort  = 41000;
                                   });

            var response = host.Execute("ConnectPorts([20000, ?], s3cret)");

            Assert.Multiple(() => {
                Assert.That(response.Code,      Is.EqualTo(ResponseCode.PortNotAllowed));
                Assert.That(response.Text,      Does.Contain("40000"));
                Assert.That(host.Manager.Count, Is.Zero);
            });

        }

        [Test]
        public async Task ConnectPorts_WithTooManyPorts_Fails()
        {

            await using var host = RendezvousTestHost.Create(options => options.MaxPortsPerSession = 2);

            var response = host.Execute("ConnectPorts([?,?,?], s3cret)");

            Assert.That(response.Code, Is.EqualTo(ResponseCode.TooManyPorts));

        }

        [Test]
        public async Task ConnectPorts_WithTooManySessions_Fails()
        {

            await using var host = RendezvousTestHost.Create(options => options.MaxSessions = 1);

            host.ExecuteOk("ConnectPorts([?,?], s3cret)");

            var response = host.Execute("ConnectPorts([?,?], s3cret)");

            Assert.Multiple(() => {
                Assert.That(response.Code,      Is.EqualTo(ResponseCode.TooManySessions));
                Assert.That(host.Manager.Count, Is.EqualTo(1));
            });

        }

        #endregion

        #region DisconnectPorts

        [Test]
        public async Task DisconnectPorts_WithTheCorrectSecret_ClosesTheRendezvous()
        {

            await using var host = RendezvousTestHost.Create();

            var ports    = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));
            var session  = host.Session;

            var response = host.ExecuteOk($"DisconnectPorts({ports[0]}, {ports[1]}, s3cret)");

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.Multiple(() => {
                Assert.That(response.Text,               Is.EqualTo($"DisconnectPorts([{ports[0]}, {ports[1]}])"));
                Assert.That(session.State,               Is.EqualTo(SessionState.Closed));
                Assert.That(session.CloseReason,         Is.EqualTo(SessionCloseReason.DisconnectRequested));
                Assert.That(host.Manager.Count,          Is.Zero);
                Assert.That(TestNet.IsPortFree(ports[0]), Is.True, "The TCP ports must be free again!");
                Assert.That(TestNet.IsPortFree(ports[1]), Is.True, "The TCP ports must be free again!");
            });

        }

        [Test]
        public async Task DisconnectPorts_WithASinglePort_ClosesTheWholeRendezvous()
        {

            await using var host = RendezvousTestHost.Create();

            var ports     = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?,?], s3cret)"));
            var session   = host.Session;

            var response  = host.ExecuteOk($"DisconnectPorts({ports[1]}, s3cret)");

            await session.Completion.WaitAsync(TestNet.Timeout);

            Assert.Multiple(() => {
                Assert.That(response.Text,      Is.EqualTo($"DisconnectPorts([{ports[0]}, {ports[1]}, {ports[2]}])"));
                Assert.That(session.State,      Is.EqualTo(SessionState.Closed));
                Assert.That(host.Manager.Count, Is.Zero);
            });

        }

        [Test]
        public async Task DisconnectPorts_DisconnectsTheConnectedClients()
        {

            await using var host = RendezvousTestHost.Create();

            var ports = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));

            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Session.State == SessionState.Established,
                                         "The rendezvous was not established!");

            host.ExecuteOk($"DisconnectPorts({ports[0]}, {ports[1]}, s3cret)");

            await TestNet.ExpectEndOfStreamAsync(alice);
            await TestNet.ExpectEndOfStreamAsync(bob);

        }

        [Test]
        public async Task DisconnectPorts_WithAWrongSecret_Fails()
        {

            await using var host = RendezvousTestHost.Create();

            var ports     = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], s3cret)"));
            var response  = host.Execute($"DisconnectPorts({ports[0]}, {ports[1]}, wrongSecret)");

            Assert.Multiple(() => {
                Assert.That(response.Code,        Is.EqualTo(ResponseCode.Unauthorized));
                Assert.That(host.Manager.Count,   Is.EqualTo(1), "An unauthorized command must not close the rendezvous!");
                Assert.That(host.Session.State,   Is.EqualTo(SessionState.Pending));
            });

        }

        [Test]
        public async Task DisconnectPorts_WithAnUnknownPort_Fails()
        {

            await using var host = RendezvousTestHost.Create();

            var response = host.Execute("DisconnectPorts(20000, s3cret)");

            Assert.Multiple(() => {
                Assert.That(response.Code, Is.EqualTo(ResponseCode.UnknownSession));
                Assert.That(response.Text, Does.Contain("20000"));
            });

        }

        [Test]
        public async Task DisconnectPorts_WithPortsOfTwoRendezvous_Fails()
        {

            await using var host = RendezvousTestHost.Create();

            var ports1 = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], secret1)"));
            var ports2 = TestNet.ParsePorts(host.ExecuteOk("ConnectPorts([?,?], secret2)"));

            var response = host.Execute($"DisconnectPorts({ports1[0]}, {ports2[0]}, secret1)");

            Assert.Multiple(() => {
                Assert.That(response.Code,      Is.EqualTo(ResponseCode.UnknownSession));
                Assert.That(host.Manager.Count, Is.EqualTo(2), "Both rendezvous must stay open!");
            });

        }

        #endregion

        #region Unknown commands

        [Test]
        public async Task UnknownCommand_IsReportedAsAnError()
        {

            await using var host = RendezvousTestHost.Create();

            var response = host.Execute("DoSomethingWeird(42)");

            Assert.Multiple(() => {
                Assert.That(response.IsSuccess,        Is.False);
                Assert.That(response.Code,             Is.EqualTo(ResponseCode.UnknownCommand));
                Assert.That(response.ToProtocolLine(), Does.StartWith("ERROR UnknownCommand "));
            });

        }

        #endregion

    }

}
