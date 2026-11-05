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

using System.ComponentModel;
using System.Diagnostics;

using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for the commits Directory.Build.props stamps into the assemblies.
    ///
    /// The file is the same in this repository, in Hermod and in Styx, and each
    /// copy imports the one above it. Whether that chain still gives every
    /// assembly the commit of its own repository - and not the one of the
    /// repository it happens to be built in - is what is asked here.
    /// </summary>
    [TestFixture]
    public class BuildProvenanceTests
    {

        #region (private) Git(Directory, params Arguments)

        /// <summary>
        /// Ask git within the given directory.
        /// </summary>
        /// <param name="Directory">The directory to run git in.</param>
        /// <param name="Arguments">The arguments.</param>
        /// <returns>The output when git answered with exit code 0, otherwise the reason it did not.</returns>
        private static (String? Output, String? Error) Git(String           Directory,
                                                           params String[]  Arguments)
        {

            var startInfo = new ProcessStartInfo("git") {
                                RedirectStandardOutput  = true,
                                RedirectStandardError   = true,
                                UseShellExecute         = false
                            };

            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(Directory);

            foreach (var argument in Arguments)
                startInfo.ArgumentList.Add(argument);

            try
            {

                using var process = Process.Start(startInfo)
                                        ?? throw new InvalidOperationException("git could not be started!");

                var error  = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();

                process.WaitForExit();

                return process.ExitCode == 0
                           ? (output.Trim(), null)
                           : (null,          error.Result.Trim());

            }
            catch (Win32Exception exception)
            {
                return (null, $"There is no git to ask: {exception.Message}");
            }

        }

        #endregion


        [Test]
        public void EveryAssemblyNamesTheCommitOfItsOwnRepository()
        {

            var (root, reason) = Git(AppContext.BaseDirectory, "rev-parse", "--show-toplevel");

            // A source archive has no history to stamp, so the stamp reads "unknown"
            // by design. CI fails a run with a skipped test, so this can not go
            // unnoticed there - not even when git refuses a checkout it does not trust.
            if (root is null)
                Assert.Ignore($"Not built within a git working tree: {reason}");

            var builtFrom = BuildProvenance.BuiltFrom();

            var expected  = new[] {
                                (Directory: root,                                  Provenance: builtFrom[0]),
                                (Directory: Path.Combine(root, "libs", "Hermod"),  Provenance: builtFrom[1]),
                                (Directory: Path.Combine(root, "libs", "Styx"),    Provenance: builtFrom[2])
                            };

            Assert.Multiple(() => {

                foreach (var (directory, provenance) in expected)
                {

                    var (toplevel, toplevelError) = Git(directory, "rev-parse", "--show-toplevel");
                    var (head,     headError)     = Git(directory, "rev-parse", "HEAD");

                    Assert.That(toplevel,  Is.Not.Null, toplevelError);
                    Assert.That(head,      Is.Not.Null, headError);

                    Assert.That(provenance.Repository,  Is.EqualTo(Path.GetFileName(toplevel)));
                    Assert.That(provenance.Commit,      Is.EqualTo(head).Or.EqualTo($"{head}-dirty"),
                                $"{provenance.Repository} was built from another commit than the one checked out in {directory}: rebuild.");

                }

            });

        }

        [Test]
        public void AnAssemblyWithoutAStampIsNamedAfterItself()
        {

            // NUnit is not built here, so nothing stamped it.
            var provenance = BuildProvenance.Of(typeof(TestAttribute).Assembly);

            Assert.Multiple(() => {
                Assert.That(provenance.Repository,  Is.EqualTo(typeof(TestAttribute).Assembly.GetName().Name));
                Assert.That(provenance.Commit,      Is.EqualTo("unknown"));
            });

        }

    }

}
