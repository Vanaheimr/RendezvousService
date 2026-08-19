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

namespace org.GraphDefined.Vanaheimr.Rendezvous
{

    /// <summary>
    /// Closes all rendezvous that ran into their rendezvous or idle timeout.
    /// </summary>
    public sealed class SessionJanitor : BackgroundService
    {

        #region Data

        private readonly RendezvousManager        manager;
        private readonly RendezvousOptions        options;
        private readonly TimeProvider             timeProvider;
        private readonly ILogger<SessionJanitor>  logger;
        private readonly TaskCompletionSource     runningSource  = new (TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Properties

        /// <summary>
        /// A task that completes as soon as this janitor watches the clock.
        /// Without it a test using a controllable clock could advance the time
        /// before the periodic timer was even created.
        /// </summary>
        internal Task Running
            => runningSource.Task;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a new session janitor.
        /// </summary>
        /// <param name="Manager">The rendezvous manager.</param>
        /// <param name="Options">The configuration of the rendezvous service.</param>
        /// <param name="TimeProvider">A time provider.</param>
        /// <param name="Logger">A logger.</param>
        public SessionJanitor(RendezvousManager            Manager,
                              IOptions<RendezvousOptions>  Options,
                              TimeProvider                 TimeProvider,
                              ILogger<SessionJanitor>      Logger)
        {

            this.manager       = Manager;
            this.options       = Options.Value;
            this.timeProvider  = TimeProvider;
            this.logger        = Logger;

        }

        #endregion


        #region ExecuteAsync(StoppingToken)

        /// <summary>
        /// Look for timed out rendezvous, again and again.
        /// </summary>
        /// <param name="StoppingToken">A token to stop this background service.</param>
        protected override async Task ExecuteAsync(CancellationToken StoppingToken)
        {

            logger.LogDebug("The session janitor is running every {Interval}.", options.MaintenanceInterval);

            using var timer = new PeriodicTimer(options.MaintenanceInterval, timeProvider);

            runningSource.TrySetResult();

            try
            {

                while (await timer.WaitForNextTickAsync(StoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        manager.Sweep();
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "The session janitor failed!");
                    }
                }

            }
            catch (OperationCanceledException)
            { }
            finally
            {
                runningSource.TrySetResult();
            }

        }

        #endregion

    }

}
