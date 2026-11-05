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
using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for the command line switches of the rendezvous service.
    /// </summary>
    [TestFixture]
    public class RendezvousCommandLineTests
    {

        #region (private) Bind(Args, params Settings)

        /// <summary>
        /// Bind the command line on top of the given settings, in the order
        /// Program.cs adds them: the settings file first, the command line last.
        /// </summary>
        private static RendezvousOptions Bind(String[]                          Args,
                                              params (String Key, String Value)[] Settings)
        {

            var configuration = new ConfigurationBuilder().
                                    AddInMemoryCollection(
                                        Settings.Select(setting => new KeyValuePair<String, String?>(setting.Key, setting.Value))
                                    ).
                                    AddRendezvousCommandLine(Args).
                                    Build();

            var options = new RendezvousOptions();
            configuration.GetSection(RendezvousOptions.SectionName).Bind(options);

            return options;

        }

        #endregion


        [Test]
        public void EverySwitchSetsItsSetting()
        {

            String[] args = [
                "--control-address",     "10.0.0.1",
                "--control-port",        "9000",
                "--data-address",        "10.0.0.2",
                "--min-port",            "20000",
                "--max-port",            "30000",
                "--rendezvous-timeout",  "00:03:00",
                "--idle-timeout",        "00:07:00",
                "--profile",             "Bulk"
            ];

            var options = Bind(args);

            Assert.Multiple(() => {

                Assert.That(RendezvousCommandLine.Switches.Keys,  Is.EquivalentTo(args.Where((arg, index) => index % 2 == 0)),
                            "A new switch needs its line in this test!");

                Assert.That(options.ControlAddress,     Is.EqualTo("10.0.0.1"));
                Assert.That(options.ControlPort,        Is.EqualTo(IPPort.Parse( 9000)));
                Assert.That(options.DataAddress,        Is.EqualTo("10.0.0.2"));
                Assert.That(options.MinDataPort,        Is.EqualTo(IPPort.Parse(20000)));
                Assert.That(options.MaxDataPort,        Is.EqualTo(IPPort.Parse(30000)));
                Assert.That(options.RendezvousTimeout,  Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(options.IdleTimeout,        Is.EqualTo(TimeSpan.FromMinutes(7)));
                Assert.That(options.DefaultProfile,     Is.EqualTo(TransferProfile.Bulk));

            });

        }

        [Test]
        public void TheCommandLineHasTheLastWord()
        {

            var options = Bind(["--control-port", "9000"],
                               ("Rendezvous:ControlPort", "8500"),
                               ("Rendezvous:MaxSessions", "100"));

            Assert.Multiple(() => {
                Assert.That(options.ControlPort,  Is.EqualTo(IPPort.Parse(9000)), "The command line must override the settings file.");
                Assert.That(options.MaxSessions,  Is.EqualTo(100),                "What the command line does not mention must stay as it is.");
            });

        }

        [Test]
        public void EverySettingKeepsItsLongForm()
        {

            var options = Bind(["--Rendezvous:MaxSessions",              "42",
                                "--Rendezvous:Profiles:Bulk:NoDelay",    "true"]);

            Assert.Multiple(() => {
                Assert.That(options.MaxSessions,            Is.EqualTo(42));
                Assert.That(options.Profiles.Bulk.NoDelay,  Is.True);
            });

        }

    }

}
