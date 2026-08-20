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

using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// Validates the rendezvous configuration during startup,
    /// so that a misconfigured service fails fast and loudly.
    /// </summary>
    public sealed class RendezvousOptionsValidator : IValidateOptions<RendezvousOptions>
    {

        /// <summary>
        /// Validate the given rendezvous configuration.
        /// </summary>
        /// <param name="Name">The name of the options instance.</param>
        /// <param name="Options">The rendezvous configuration.</param>
        public ValidateOptionsResult Validate(String? Name, RendezvousOptions Options)
        {

            var errors = Options.Validate();

            return errors.Count > 0
                       ? ValidateOptionsResult.Fail(errors)
                       : ValidateOptionsResult.Success;

        }

    }

}
