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

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for binding appsettings.json onto the rendezvous configuration.
    ///
    /// Every configuration value is a string, therefore every non-primitive
    /// property needs a TypeConverter. IPPort has one - without it the whole
    /// section would fail to bind, and these tests are what notices.
    /// </summary>
    [TestFixture]
    public class ConfigurationBindingTests
    {

        #region (private) Bind(Values)

        private static RendezvousOptions Bind(params (String Key, String Value)[] Values)
        {

            var configuration = new ConfigurationBuilder().
                                    AddInMemoryCollection(
                                        Values.Select(value => new KeyValuePair<String, String?>(value.Key, value.Value))
                                    ).
                                    Build();

            var options = new RendezvousOptions();
            configuration.GetSection(RendezvousOptions.SectionName).Bind(options);

            return options;

        }

        #endregion


        [Test]
        public void TcpPortsAreBoundFromConfiguration()
        {

            var options = Bind(("Rendezvous:ControlPort", "8500"),
                               ("Rendezvous:MinDataPort", "20000"),
                               ("Rendezvous:MaxDataPort", "30000"));

            Assert.Multiple(() => {
                Assert.That(options.ControlPort, Is.EqualTo(IPPort.Parse( 8500)));
                Assert.That(options.MinDataPort, Is.EqualTo(IPPort.Parse(20000)));
                Assert.That(options.MaxDataPort, Is.EqualTo(IPPort.Parse(30000)));
            });

        }

        [Test]
        public void EverythingElseIsBoundAsWell()
        {

            var options = Bind(("Rendezvous:ControlAddress",                    "10.0.0.1"),
                               ("Rendezvous:DataAddress",                       "10.0.0.2"),
                               ("Rendezvous:RendezvousTimeout",                 "00:03:00"),
                               ("Rendezvous:IdleTimeout",                       "00:07:00"),
                               ("Rendezvous:AutoMaintenance",                   "false"),
                               ("Rendezvous:MaxSessions",                       "42"),
                               ("Rendezvous:DefaultProfile",                    "Bulk"),
                               ("Rendezvous:Profiles:Interactive:NoDelay",      "false"),
                               ("Rendezvous:Profiles:Interactive:RelayBufferSize", "4096"));

            Assert.Multiple(() => {
                Assert.That(options.ControlAddress,                        Is.EqualTo("10.0.0.1"));
                Assert.That(options.ControlIPAddress.ToString(),           Is.EqualTo("10.0.0.1"));
                Assert.That(options.DataIPAddress.ToString(),              Is.EqualTo("10.0.0.2"));
                Assert.That(options.RendezvousTimeout,                     Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(options.IdleTimeout,                           Is.EqualTo(TimeSpan.FromMinutes(7)));
                Assert.That(options.AutoMaintenance,                       Is.False);
                Assert.That(options.MaxSessions,                           Is.EqualTo(42));
                Assert.That(options.DefaultProfile,                        Is.EqualTo(TransferProfile.Bulk));
                Assert.That(options.Profiles.Interactive.NoDelay,          Is.False);
                Assert.That(options.Profiles.Interactive.RelayBufferSize,  Is.EqualTo(4096));
            });

        }

        [Test]
        public void ControlKeysAreBoundFromConfiguration()
        {

            using var signer = ControlSigner.GenerateEd25519("operator-1");

            // Three different timestamps, so that none of them can stand in for
            // another unnoticed. They are only bound here, never compared with
            // the clock, so a date in the past does not age this test.
            var options = Bind(("Rendezvous:ControlKeys:0:Id",              "operator-1"),
                               ("Rendezvous:ControlKeys:0:KeyType",         "Ed25519"),
                               ("Rendezvous:ControlKeys:0:PublicKey",       Convert.ToBase64String(signer.PublicKey)),
                               ("Rendezvous:ControlKeys:0:NotBefore",       "2026-01-01T00:00:00Z"),
                               ("Rendezvous:ControlKeys:0:NotAfter",        "2026-12-31T23:59:59Z"),
                               ("Rendezvous:ControlKeys:0:Description",     "The operator on duty"),
                               ("Rendezvous:ControlKeys:0:IsAdministrator", "true"),
                               ("Rendezvous:ControlKeys:0:Created",         "2025-12-15T10:30:00Z"),
                               ("Rendezvous:ControlKeys:0:CreatedBy",       "achim"));

            Assert.That(options.ControlKeys,        Has.Count.EqualTo(1));
            Assert.That(options.Validate(),         Is.Empty);

            var bound   = options.ControlKeys[0];
            var success = bound.TryToControlKey(out var key, out var errorResponse);

            Assert.That(success, Is.True, errorResponse);

            Assert.Multiple(() => {
                Assert.That(key!.Id,               Is.EqualTo("operator-1"));
                Assert.That(key!.KeyType,          Is.EqualTo(SignatureKeyType.Ed25519));
                Assert.That(key!.PublicKey,        Is.EqualTo(signer.PublicKey));
                Assert.That(key!.IsAdministrator,  Is.True, "An administrator key must be recognizable from the configuration!");
                Assert.That(key!.NotBefore,        Is.EqualTo(new DateTimeOffset(2026,  1,  1,  0,  0,  0, TimeSpan.Zero)));
                Assert.That(key!.NotAfter,         Is.EqualTo(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero)),
                            "A key meant to expire must not be bound as one that never does!");
                Assert.That(key!.Created,          Is.EqualTo(new DateTimeOffset(2025, 12, 15, 10, 30,  0, TimeSpan.Zero)));
                Assert.That(key!.CreatedBy,        Is.EqualTo("achim"));
                Assert.That(key!.Description,      Is.EqualTo("The operator on duty"));
            });

        }

        [Test]
        public void AnInvalidTcpPortIsReported()
        {

            Assert.Throws<InvalidOperationException>(
                () => Bind(("Rendezvous:ControlPort", "no-such-port"))
            );

        }

        [Test]
        public void TheShippedAppSettingsAreValid()
        {

            var configuration = new ConfigurationBuilder().
                                    SetBasePath(AppContext.BaseDirectory).
                                    AddJsonFile("appsettings.json", optional: false).
                                    Build();

            var options = new RendezvousOptions();
            configuration.GetSection(RendezvousOptions.SectionName).Bind(options);

            Assert.Multiple(() => {
                Assert.That(options.Validate(),  Is.Empty);
                Assert.That(options.ControlPort, Is.EqualTo(IPPort.Parse(8500)), "The shipped appsettings.json must really be read.");
            });

        }

    }

}
