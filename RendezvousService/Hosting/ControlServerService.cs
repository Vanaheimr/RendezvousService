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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// Ties the control endpoint of the library into the life cycle of the
    /// generic host: the host starts it after the application was built and
    /// stops it on Ctrl+C or SIGTERM.
    ///
    /// This is what a hosted service is actually for - being called by the host.
    /// A BackgroundService would add nothing here, as the control endpoint
    /// already knows how to start and stop itself.
    /// </summary>
    public sealed class ControlServerService : IHostedService, IAsyncDisposable
    {

        #region Data

        private readonly ControlServer controlServer;

        #endregion

        #region Properties

        /// <summary>
        /// The hosted control endpoint.
        /// </summary>
        public ControlServer ControlServer
            => controlServer;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a new hosted control endpoint.
        /// </summary>
        /// <param name="Manager">The rendezvous manager to control.</param>
        /// <param name="Options">The configuration of the rendezvous service.</param>
        /// <param name="LoggerFactory">A logger factory.</param>
        public ControlServerService(RendezvousManager            Manager,
                                    IOptions<RendezvousOptions>  Options,
                                    ILoggerFactory               LoggerFactory)
        {

            controlServer = new ControlServer(Manager,
                                              Options.Value,
                                              LoggerFactory);

        }

        #endregion


        #region StartAsync(CancellationToken)

        /// <summary>
        /// Start the control endpoint.
        /// </summary>
        /// <param name="CancellationToken">A token to cancel starting.</param>
        public Task StartAsync(CancellationToken CancellationToken)
        {

            controlServer.Start();

            return Task.CompletedTask;

        }

        #endregion

        #region StopAsync(CancellationToken)

        /// <summary>
        /// Stop the control endpoint.
        /// </summary>
        /// <param name="CancellationToken">A token to cancel stopping.</param>
        public Task StopAsync(CancellationToken CancellationToken)

            => controlServer.StopAsync().WaitAsync(CancellationToken);

        #endregion

        #region DisposeAsync()

        /// <summary>
        /// Dispose the control endpoint.
        /// </summary>
        public ValueTask DisposeAsync()

            => controlServer.DisposeAsync();

        #endregion

    }

}
