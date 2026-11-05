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

using System.Reflection;

using org.GraphDefined.Vanaheimr.Hermod.Rendezvous;
using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous.Service
{

    /// <summary>
    /// What an assembly was built from: the repository and the commit
    /// Directory.Build.props stamped into it while it was being compiled.
    ///
    /// That is the tree the code was compiled from, not the one checked out when
    /// the service starts - build, check out another commit, start without
    /// rebuilding, and only the stamp still tells the truth.
    /// </summary>
    /// <param name="Repository">The repository, or the assembly name when the assembly carries no stamp.</param>
    /// <param name="Commit">The commit, followed by "-dirty" when the working tree had changes, or "unknown".</param>
    public sealed record BuildProvenance(String  Repository,
                                         String  Commit)
    {

        #region (static) Of(Assembly)

        /// <summary>
        /// Read what the given assembly was built from.
        /// </summary>
        /// <param name="Assembly">An assembly.</param>
        public static BuildProvenance Of(Assembly Assembly)
        {

            var metadata = Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();

            return new (
                       metadata.FirstOrDefault(entry => entry.Key == "GitRepository")?.Value ?? Assembly.GetName().Name ?? "unknown",
                       metadata.FirstOrDefault(entry => entry.Key == "GitCommit")?.    Value ?? "unknown"
                   );

        }

        #endregion

        #region (static) BuiltFrom()

        /// <summary>
        /// The service and the two libraries beneath it, each stamped by the
        /// build from a repository of its own.
        /// </summary>
        public static IReadOnlyList<BuildProvenance> BuiltFrom()

            => [
                   Of(typeof(BuildProvenance).  Assembly),
                   Of(typeof(RendezvousManager).Assembly),
                   Of(typeof(I18NString).       Assembly)
               ];

        #endregion


        #region (override) ToString()

        /// <summary>
        /// Return a text representation of this object.
        /// </summary>
        public override String ToString()

            => $"{Repository} {Commit}";

        #endregion

    }

}
