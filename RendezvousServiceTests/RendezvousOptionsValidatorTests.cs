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

using Microsoft.Extensions.Options;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;
using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for the options validator of the application.
    ///
    /// The rules themselves belong to the library and are tested within
    /// HermodTests - this is only about the IValidateOptions adapter that
    /// reports them to the generic host.
    /// </summary>
    [TestFixture]
    public class RendezvousOptionsValidatorTests
    {

        [Test]
        public void AValidConfigurationPasses()
        {

            var result = new RendezvousOptionsValidator().Validate(null, new RendezvousOptions());

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Failed,    Is.False);
            });

        }

        [Test]
        public void AnInvalidConfigurationReportsEveryProblem()
        {

            var result = new RendezvousOptionsValidator().Validate(null,
                                                                   new RendezvousOptions {
                                                                       ControlAddress  = "nonsense",
                                                                       MaxSessions     = 0
                                                                   });

            Assert.Multiple(() => {
                Assert.That(result.Failed,   Is.True);
                Assert.That(result.Failures, Is.Not.Null.And.Exactly(2).Items,
                            "The host shall see all problems at once, not one per restart.");
            });

        }

        [Test]
        public void TheValidatorIgnoresTheOptionsName()
        {

            var validator = new RendezvousOptionsValidator();

            Assert.Multiple(() => {
                Assert.That(validator.Validate(null,           new RendezvousOptions()).Succeeded, Is.True);
                Assert.That(validator.Validate("SomeName",     new RendezvousOptions()).Succeeded, Is.True);
                Assert.That(validator.Validate(Options.DefaultName, new RendezvousOptions()).Succeeded, Is.True);
            });

        }

    }

}
