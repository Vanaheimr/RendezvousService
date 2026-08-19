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

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Tests
{

    /// <summary>
    /// A rendezvous manager using a controllable clock, bound to the loopback
    /// interface, for tests.
    /// </summary>
    public sealed class RendezvousTestHost : IAsyncDisposable
    {

        #region Properties

        /// <summary>
        /// The configuration of this test host.
        /// </summary>
        public RendezvousOptions   Configuration    { get; }

        /// <summary>
        /// The controllable clock of this test host.
        /// </summary>
        public FakeTimeProvider    Time             { get; }

        /// <summary>
        /// The rendezvous manager under test.
        /// </summary>
        public RendezvousManager   Manager          { get; }

        /// <summary>
        /// The one and only rendezvous of this test host.
        /// </summary>
        public RendezvousSession   Session
            => Manager.Sessions.Single();

        #endregion

        #region Constructor(s)

        private RendezvousTestHost(RendezvousOptions  Configuration,
                                   FakeTimeProvider   Time,
                                   RendezvousManager  Manager)
        {

            this.Configuration  = Configuration;
            this.Time           = Time;
            this.Manager        = Manager;

        }

        #endregion


        #region (static) Create(Configure = null)

        /// <summary>
        /// Create a new test host.
        /// </summary>
        /// <param name="Configure">An optional delegate to change the default configuration.</param>
        public static RendezvousTestHost Create(Action<RendezvousOptions>? Configure = null)
        {

            var configuration = new RendezvousOptions {
                                    ControlAddress  = "127.0.0.1",
                                    ControlPort     = 0,
                                    DataAddress     = "127.0.0.1"
                                };

            Configure?.Invoke(configuration);

            var errors = configuration.Validate();
            Assert.That(errors, Is.Empty, $"Invalid test configuration: {String.Join(" ", errors)}");

            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

            return new RendezvousTestHost(
                       configuration,
                       time,
                       new RendezvousManager(
                           Microsoft.Extensions.Options.Options.Create(configuration),
                           time,
                           NullLoggerFactory.Instance
                       )
                   );

        }

        #endregion

        #region Execute(CommandLine)

        /// <summary>
        /// Execute the given control command.
        /// </summary>
        /// <param name="CommandLine">A text representation of a control command.</param>
        public CommandResponse Execute(String CommandLine)
            => Manager.Execute(CommandLine);

        #endregion

        #region ExecuteOk(CommandLine)

        /// <summary>
        /// Execute the given control command and expect it to succeed.
        /// </summary>
        /// <param name="CommandLine">A text representation of a control command.</param>
        public CommandResponse ExecuteOk(String CommandLine)
        {

            var response = Manager.Execute(CommandLine);
            Assert.That(response.IsSuccess, Is.True, $"'{CommandLine}' failed: {response.ToProtocolLine()}");

            return response;

        }

        #endregion


        #region DisposeAsync()

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
        }

        #endregion

    }

}
