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

using Microsoft.Extensions.Logging.Abstractions;

using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Tests
{

    /// <summary>
    /// End-to-end tests of the control endpoint: real TCP, real commands.
    /// </summary>
    [TestFixture]
    public class ControlServerTests
    {

        #region (class) ControlTestHost

        /// <summary>
        /// A running control endpoint on a free TCP port of the loopback interface.
        /// </summary>
        private sealed class ControlTestHost : IAsyncDisposable
        {

            public RendezvousManager  Manager   { get; }
            public ControlServer      Server    { get; }
            public UInt16             Port      { get; }

            private ControlTestHost(RendezvousManager  Manager,
                                    ControlServer      Server)
            {

                this.Manager  = Manager;
                this.Server   = Server;
                this.Port     = (UInt16) Server.LocalEndPoint!.Port;

            }

            public static async Task<ControlTestHost> StartAsync(Action<RendezvousOptions>? Configure = null)
            {

                var configuration = new RendezvousOptions {
                                        ControlAddress  = "127.0.0.1",
                                        ControlPort     = 0,
                                        DataAddress     = "127.0.0.1"
                                    };

                Configure?.Invoke(configuration);

                // The library takes the configuration as it is, the application
                // wraps it into IOptions for its own dependency injection.
                var manager  = new RendezvousManager(configuration, TimeProvider.System, NullLoggerFactory.Instance);

                var server   = new ControlServer(manager,
                                                 Microsoft.Extensions.Options.Options.Create(configuration),
                                                 NullLoggerFactory.Instance);

                await server.StartAsync(CancellationToken.None);

                return new ControlTestHost(manager, server);

            }

            public async ValueTask DisposeAsync()
            {

                await Server.StopAsync(CancellationToken.None).WaitAsync(TestNet.Timeout);
                Server.Dispose();

                await Manager.DisposeAsync();

            }

        }

        #endregion

        #region (class) ControlClient

        /// <summary>
        /// A line based client of the control endpoint.
        /// </summary>
        private sealed class ControlClient : IDisposable
        {

            private readonly TcpClient      client;
            private readonly NetworkStream  stream;
            private readonly StreamReader   reader;

            public ControlClient(TcpClient Client)
            {

                this.client  = Client;
                this.stream  = Client.GetStream();
                this.reader  = new StreamReader(stream, new UTF8Encoding(false));

            }

            public static async Task<ControlClient> ConnectAsync(UInt16 Port)
            {

                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, Port).WaitAsync(TestNet.Timeout);

                return new ControlClient(client);

            }

            public Task SendRawAsync(String Text)
                => SendRawBytesAsync(Encoding.UTF8.GetBytes(Text));

            public async Task SendRawBytesAsync(Byte[] Bytes)
            {
                await stream.WriteAsync(Bytes).AsTask().WaitAsync(TestNet.Timeout);
                await stream.FlushAsync().              WaitAsync(TestNet.Timeout);
            }

            public Task SendCommandAsync(String Command)
                => SendRawAsync($"{Command}\r\n");

            public async Task<String> ReadResponseAsync()
            {

                var line = await reader.ReadLineAsync().WaitAsync(TestNet.Timeout);

                Assert.That(line, Is.Not.Null, "The control endpoint closed the connection unexpectedly!");

                return line!;

            }

            public async Task<String> ExecuteAsync(String Command)
            {
                await SendCommandAsync(Command);
                return await ReadResponseAsync();
            }

            public async Task<String?> ReadResponseOrNullAsync()
                => await reader.ReadLineAsync().WaitAsync(TestNet.Timeout);

            public void Dispose()
            {
                reader.Dispose();
                client.Dispose();
            }

        }

        #endregion


        #region The control endpoint

        [Test]
        public async Task ControlEndpoint_IsListeningAfterStart()
        {

            await using var host = await ControlTestHost.StartAsync();

            Assert.Multiple(() => {
                Assert.That(host.Server.LocalEndPoint,          Is.Not.Null);
                Assert.That(host.Port,                          Is.GreaterThan(0), "A control port of zero must be replaced by a free port!");
                Assert.That(host.Server.LocalEndPoint!.Address, Is.EqualTo(IPAddress.Loopback));
            });

            using var client = await ControlClient.ConnectAsync(host.Port);

        }

        #endregion

        #region The full round trip

        [Test]
        public async Task ControlEndpoint_OpensAndClosesARendezvous()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            #region ConnectPorts

            var response = await control.ExecuteAsync("ConnectPorts([?,?], s3cret, Interactive)");

            Assert.That(response, Does.StartWith("OK ConnectPorts([").And.EndWith(", Interactive)"));

            var ports = TestNet.ParsePorts(response);

            #endregion

            #region The clients meet each other

            using var alice = await TestNet.ConnectAsync(ports[0]);
            using var bob   = await TestNet.ConnectAsync(ports[1]);

            await TestNet.WaitUntilAsync(() => host.Manager.Sessions.Single().State == SessionState.Established,
                                         "The rendezvous was not established!");

            await TestNet.SendAsync(alice, "Hello Bob!");
            Assert.That(await TestNet.ReceiveAsync(bob, 10), Is.EqualTo("Hello Bob!"));

            #endregion

            #region DisconnectPorts

            var session = host.Manager.Sessions.Single();

            Assert.That(await control.ExecuteAsync($"DisconnectPorts({ports[0]}, {ports[1]}, s3cret)"),
                        Is.EqualTo($"OK DisconnectPorts([{ports[0]}, {ports[1]}])"));

            await session.Completion.WaitAsync(TestNet.Timeout);

            await TestNet.ExpectEndOfStreamAsync(alice);
            await TestNet.ExpectEndOfStreamAsync(bob);

            Assert.That(host.Manager.Count, Is.Zero);

            #endregion

        }

        #endregion

        #region Errors and edge cases

        [Test]
        public async Task ControlEndpoint_AnswersEveryCommandOnTheSameConnection()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            Assert.That(await control.ExecuteAsync("ConnectPorts([?,?], secret1)"), Does.StartWith("OK "));
            Assert.That(await control.ExecuteAsync("Nonsense()"),                   Does.StartWith("ERROR UnknownCommand "));
            Assert.That(await control.ExecuteAsync("ConnectPorts([?,?], secret2)"), Does.StartWith("OK "));

            Assert.That(host.Manager.Count, Is.EqualTo(2), "An invalid command must not close the control connection!");

        }

        [Test]
        public async Task ControlEndpoint_AcceptsTwoCommandsWithinOneSegment()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            await control.SendRawAsync("ConnectPorts([?,?], secret1)\r\nConnectPorts([?,?], secret2)\r\n");

            Assert.That(await control.ReadResponseAsync(), Does.StartWith("OK ConnectPorts(["));
            Assert.That(await control.ReadResponseAsync(), Does.StartWith("OK ConnectPorts(["));

            Assert.That(host.Manager.Count, Is.EqualTo(2));

        }

        [Test]
        public async Task ControlEndpoint_AcceptsACommandSplitIntoTwoSegments()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            await control.SendRawAsync("ConnectPorts([?,?], s3c");
            await Task.Delay(100);
            await control.SendRawAsync("ret)\n");

            Assert.That(await control.ReadResponseAsync(), Does.StartWith("OK ConnectPorts(["));

        }

        [Test]
        public async Task ControlEndpoint_AcceptsLinefeedOnlyLineEndings()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            await control.SendRawAsync("ConnectPorts([?,?], s3cret)\n");

            Assert.That(await control.ReadResponseAsync(), Does.StartWith("OK "));

        }

        [Test]
        public async Task ControlEndpoint_IgnoresEmptyLines()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            await control.SendRawAsync("\r\n\r\nConnectPorts([?,?], s3cret)\r\n");

            Assert.That(await control.ReadResponseAsync(), Does.StartWith("OK "));

        }

        [Test]
        public async Task ControlEndpoint_RejectsInvalidUTF8()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            // 0xC3 0x28 is not a valid UTF-8 sequence.
            await control.SendRawBytesAsync([
                       .. "ConnectPorts([?,?], "u8.ToArray(),
                          (Byte) 0xC3, (Byte) 0x28,
                       .. ")\r\n"u8.ToArray()
                   ]);

            Assert.That(await control.ReadResponseAsync(), Does.StartWith("ERROR InvalidSyntax "));

        }

        [Test]
        public async Task ControlEndpoint_RejectsTooLongCommands()
        {

            await using var host    = await ControlTestHost.StartAsync(options => options.MaxCommandLength = 64);
            using var       control = await ControlClient.ConnectAsync(host.Port);

            await control.SendRawAsync($"ConnectPorts([?,?], {new String('x', 512)})\r\n");

            Assert.That(await control.ReadResponseAsync(),       Does.StartWith("ERROR CommandTooLong "));
            Assert.That(await control.ReadResponseOrNullAsync(), Is.Null, "The connection must be closed afterwards!");

        }

        [Test]
        public async Task ControlEndpoint_RejectsAnUnauthorizedDisconnect()
        {

            await using var host    = await ControlTestHost.StartAsync();
            using var       control = await ControlClient.ConnectAsync(host.Port);

            var response  = await control.ExecuteAsync("ConnectPorts([?,?], s3cret)");
            var ports     = TestNet.ParsePorts(response);

            Assert.That(await control.ExecuteAsync($"DisconnectPorts({ports[0]}, {ports[1]}, wrong)"),
                        Does.StartWith("ERROR Unauthorized "));

            Assert.That(host.Manager.Count, Is.EqualTo(1));

        }

        [Test]
        public async Task ControlEndpoint_LimitsTheNumberOfControlConnections()
        {

            await using var host = await ControlTestHost.StartAsync(options => options.MaxControlConnections = 1);

            using var first = await ControlClient.ConnectAsync(host.Port);
            Assert.That(await first.ExecuteAsync("ConnectPorts([?,?], s3cret)"), Does.StartWith("OK "));

            using var second = await ControlClient.ConnectAsync(host.Port);
            Assert.That(await second.ReadResponseOrNullAsync(), Is.Null, "The second control connection must be rejected!");

        }

        #endregion

    }

}
