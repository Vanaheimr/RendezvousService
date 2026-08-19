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
    /// Tests for the control command parser.
    /// </summary>
    [TestFixture]
    public class CommandParserTests
    {

        #region (private) ParseConnect   (Text)

        private static ConnectPortsCommand ParseConnect(String Text)
        {

            var success = CommandParser.TryParse(Text, out var command, out var error);

            Assert.That(success, Is.True, $"Could not parse '{Text}': {error}");
            Assert.That(command, Is.InstanceOf<ConnectPortsCommand>());

            return (ConnectPortsCommand) command!;

        }

        #endregion

        #region (private) ParseDisconnect(Text)

        private static DisconnectPortsCommand ParseDisconnect(String Text)
        {

            var success = CommandParser.TryParse(Text, out var command, out var error);

            Assert.That(success, Is.True, $"Could not parse '{Text}': {error}");
            Assert.That(command, Is.InstanceOf<DisconnectPortsCommand>());

            return (DisconnectPortsCommand) command!;

        }

        #endregion

        #region (private) ParseError     (Text)

        private static CommandError ParseError(String Text)
        {

            var success = CommandParser.TryParse(Text, out var command, out var error);

            Assert.That(success, Is.False, $"'{Text}' should not be parsable, but was parsed as '{command}'!");
            Assert.That(error.Message, Is.Not.Empty, "A parser error must have a message!");

            return error;

        }

        #endregion


        #region ConnectPorts

        [Test]
        public void ConnectPorts_WithTwoFixedPorts()
        {

            var command = ParseConnect("ConnectPorts([20000, 30000], s3cret)");

            Assert.Multiple(() => {
                Assert.That(command.Ports.Count,      Is.EqualTo(2));
                Assert.That(command.Ports[0].Port,    Is.EqualTo((UInt16) 20000));
                Assert.That(command.Ports[1].Port,    Is.EqualTo((UInt16) 30000));
                Assert.That(command.Ports[0].IsRandom, Is.False);
                Assert.That(command.Secret,           Is.EqualTo("s3cret"));
                Assert.That(command.Profile,          Is.Null, "An unspecified transfer profile must stay unspecified!");
            });

        }

        [Test]
        public void ConnectPorts_WithRandomFirstPort()
        {

            var command = ParseConnect("ConnectPorts([?,30000], s3cret)");

            Assert.Multiple(() => {
                Assert.That(command.Ports[0].IsRandom, Is.True);
                Assert.That(command.Ports[1].Port,     Is.EqualTo((UInt16) 30000));
            });

        }

        [Test]
        public void ConnectPorts_WithRandomSecondPort()
        {

            var command = ParseConnect("ConnectPorts([20000, ?], s3cret)");

            Assert.Multiple(() => {
                Assert.That(command.Ports[0].Port,     Is.EqualTo((UInt16) 20000));
                Assert.That(command.Ports[1].IsRandom, Is.True);
            });

        }

        [Test]
        public void ConnectPorts_WithTwoRandomPorts()
        {

            var command = ParseConnect("ConnectPorts([?,?], s3cret)");

            Assert.Multiple(() => {
                Assert.That(command.Ports.Count,       Is.EqualTo(2));
                Assert.That(command.Ports[0].IsRandom, Is.True);
                Assert.That(command.Ports[1].IsRandom, Is.True);
            });

        }

        [Test]
        public void ConnectPorts_WithThreePorts_IsAChat()
        {

            var command = ParseConnect("ConnectPorts([?,?,?], s3cret)");

            Assert.That(command.Ports.Count, Is.EqualTo(3));

        }

        [Test]
        public void ConnectPorts_IsCaseInsensitive()
        {

            var command = ParseConnect("connectPORTS([?,?], s3cret)");

            Assert.That(command.CommandName, Is.EqualTo("ConnectPorts"));

        }

        [Test]
        public void ConnectPorts_IgnoresWhitespace()
        {

            var command = ParseConnect("   ConnectPorts (  [ 20000 ,  ? ] ,  s3cret )   ");

            Assert.Multiple(() => {
                Assert.That(command.Ports[0].Port,     Is.EqualTo((UInt16) 20000));
                Assert.That(command.Ports[1].IsRandom, Is.True);
                Assert.That(command.Secret,            Is.EqualTo("s3cret"));
            });

        }

        [Test]
        public void ConnectPorts_KeepsSpecialCharactersWithinTheSecret()
        {

            var command = ParseConnect("ConnectPorts([?,?], $ecret-42_%&/{}=!?)");

            Assert.That(command.Secret, Is.EqualTo("$ecret-42_%&/{}=!?"));

        }

        [Test]
        public void ConnectPorts_MasksTheSecretWithinToString()
        {

            var command = ParseConnect("ConnectPorts([20000, ?], TopSecret, Bulk)");

            Assert.Multiple(() => {
                Assert.That(command.ToString(), Does.Not.Contain("TopSecret"), "The secret must never be logged!");
                Assert.That(command.ToString(), Is.EqualTo("ConnectPorts([20000, ?], ***, Bulk)"));
            });

        }

        #endregion

        #region ConnectPorts with transfer profiles

        [Test]
        [TestCase("Balanced",     TransferProfile.Balanced)]
        [TestCase("default",      TransferProfile.Balanced)]
        [TestCase("Interactive",  TransferProfile.Interactive)]
        [TestCase("interactive",  TransferProfile.Interactive)]
        [TestCase("low-latency",  TransferProfile.Interactive)]
        [TestCase("LOW_LATENCY",  TransferProfile.Interactive)]
        [TestCase("chat",         TransferProfile.Interactive)]
        [TestCase("SSH",          TransferProfile.Interactive)]
        [TestCase("Bulk",         TransferProfile.Bulk)]
        [TestCase("bulk-transfer",TransferProfile.Bulk)]
        [TestCase("throughput",   TransferProfile.Bulk)]
        [TestCase("backup",       TransferProfile.Bulk)]
        public void ConnectPorts_WithTransferProfile(String Text, TransferProfile Expected)
        {

            var command = ParseConnect($"ConnectPorts([?,?], s3cret, {Text})");

            Assert.That(command.Profile, Is.EqualTo(Expected));

        }

        [Test]
        public void ConnectPorts_WithUnknownTransferProfile_Fails()
        {

            var error = ParseError("ConnectPorts([?,?], s3cret, Telepathy)");

            Assert.Multiple(() => {
                Assert.That(error.Code,    Is.EqualTo(ResponseCode.InvalidSyntax));
                Assert.That(error.Message, Does.Contain("Telepathy"));
            });

        }

        #endregion

        #region DisconnectPorts

        [Test]
        public void DisconnectPorts_WithTwoPorts()
        {

            var command = ParseDisconnect("DisconnectPorts(20000, 30000, s3cret)");

            Assert.Multiple(() => {
                Assert.That(command.Ports, Is.EqualTo(new UInt16[] { 20000, 30000 }));
                Assert.That(command.Secret, Is.EqualTo("s3cret"));
            });

        }

        [Test]
        public void DisconnectPorts_WithASinglePort()
        {

            var command = ParseDisconnect("DisconnectPorts(20000, s3cret)");

            Assert.Multiple(() => {
                Assert.That(command.Ports,  Is.EqualTo(new UInt16[] { 20000 }));
                Assert.That(command.Secret, Is.EqualTo("s3cret"));
            });

        }

        [Test]
        public void DisconnectPorts_WithABracketedPortList()
        {

            var command = ParseDisconnect("DisconnectPorts([20000, 30000], s3cret)");

            Assert.That(command.Ports, Is.EqualTo(new UInt16[] { 20000, 30000 }));

        }

        [Test]
        public void DisconnectPorts_WithANumericSecret()
        {

            // The last argument is always the secret, never a port.
            var command = ParseDisconnect("DisconnectPorts(20000, 30000, 12345)");

            Assert.Multiple(() => {
                Assert.That(command.Ports,  Is.EqualTo(new UInt16[] { 20000, 30000 }));
                Assert.That(command.Secret, Is.EqualTo("12345"));
            });

        }

        [Test]
        public void DisconnectPorts_MasksTheSecretWithinToString()
        {

            var command = ParseDisconnect("DisconnectPorts(20000, 30000, TopSecret)");

            Assert.Multiple(() => {
                Assert.That(command.ToString(), Does.Not.Contain("TopSecret"));
                Assert.That(command.ToString(), Is.EqualTo("DisconnectPorts([20000, 30000], ***)"));
            });

        }

        #endregion

        #region Invalid commands

        [Test]
        public void UnknownCommand_Fails()
        {

            var error = ParseError("ReticulateSplines([?,?], s3cret)");

            Assert.That(error.Code, Is.EqualTo(ResponseCode.UnknownCommand));

        }

        [Test]
        [TestCase("",                                    Description = "Empty")]
        [TestCase("   ",                                 Description = "Whitespace only")]
        [TestCase("ConnectPorts",                        Description = "No arguments at all")]
        [TestCase("ConnectPorts([?,?]",                  Description = "Missing closing parenthesis")]
        [TestCase("ConnectPorts([?,?, s3cret)",          Description = "Missing closing bracket")]
        [TestCase("ConnectPorts([?,?])",                 Description = "Missing secret")]
        [TestCase("ConnectPorts([?,?],)",                Description = "Empty secret")]
        [TestCase("ConnectPorts([?], s3cret)",           Description = "A rendezvous needs at least two ports")]
        [TestCase("ConnectPorts([], s3cret)",            Description = "No ports at all")]
        [TestCase("ConnectPorts(20000, 30000, s3cret)",  Description = "ConnectPorts requires a bracketed port list")]
        [TestCase("ConnectPorts([20000, 20000], s3cret)",Description = "Duplicate ports")]
        [TestCase("ConnectPorts([0, 30000], s3cret)",    Description = "TCP port zero")]
        [TestCase("ConnectPorts([70000, 30000], s3cret)",Description = "TCP port above 65535")]
        [TestCase("ConnectPorts([-1, 30000], s3cret)",   Description = "Negative TCP port")]
        [TestCase("ConnectPorts([2e4, 30000], s3cret)",  Description = "Not a number")]
        [TestCase("ConnectPorts([?,?], my secret)",      Description = "Whitespace within the secret")]
        [TestCase("ConnectPorts([?,?], s3cret) extra",   Description = "Trailing text")]
        [TestCase("ConnectPorts([?,?], s3cret, Bulk, x)",Description = "Too many arguments")]
        [TestCase("ConnectPorts([?,?,], s3cret)",        Description = "Trailing comma within the port list")]
        [TestCase("DisconnectPorts(s3cret)",             Description = "No ports at all")]
        [TestCase("DisconnectPorts(20000)",              Description = "No secret")]
        [TestCase("DisconnectPorts(?, s3cret)",          Description = "Random ports can not be disconnected")]
        [TestCase("DisconnectPorts([?], s3cret)",        Description = "Random ports can not be disconnected")]
        [TestCase("DisconnectPorts(20000, 20000, s3cret)", Description = "Duplicate ports")]
        public void InvalidCommand_Fails(String Text)
        {

            var error = ParseError(Text);

            Assert.That(error.Code, Is.AnyOf(ResponseCode.InvalidSyntax,
                                             ResponseCode.UnknownCommand));

        }

        [Test]
        public void TooLongSecret_Fails()
        {

            var error = ParseError($"ConnectPorts([?,?], {new String('x', CommandParser.MaxSecretLength + 1)})");

            Assert.Multiple(() => {
                Assert.That(error.Code,    Is.EqualTo(ResponseCode.InvalidSyntax));
                Assert.That(error.Message, Does.Contain(CommandParser.MaxSecretLength.ToString()));
            });

        }

        [Test]
        public void MaximumLengthSecret_IsAccepted()
        {

            var secret   = new String('x', CommandParser.MaxSecretLength);
            var command  = ParseConnect($"ConnectPorts([?,?], {secret})");

            Assert.That(command.Secret, Is.EqualTo(secret));

        }

        #endregion

    }

}
