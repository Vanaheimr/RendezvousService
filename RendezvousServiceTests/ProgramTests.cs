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

using System.Diagnostics;

using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for Program.cs itself, run as the process it is.
    ///
    /// Everything Program.cs registers is tested without a process, see
    /// RendezvousServiceCollectionExtensionsTests. What only a process can show
    /// is where the host looks for its files.
    /// </summary>
    [TestFixture]
    public class ProgramTests
    {

        #region (private) ReadContentRootAsync(Output)

        /// <summary>
        /// Read the console log of the service until the host reports its content root.
        /// </summary>
        /// <param name="Output">The standard output of the service.</param>
        private static async Task<String?> ReadContentRootAsync(StreamReader Output)
        {

            const String prefix = "Content root path:";

            while (await Output.ReadLineAsync() is String line)
            {

                var text = line.Trim();

                if (text.StartsWith(prefix, StringComparison.Ordinal))
                    return text[prefix.Length..].Trim();

            }

            return null;

        }

        #endregion

        #region (private) Normalize(Path)

        /// <summary>
        /// A path as the file system sees it: full, without a trailing separator,
        /// and on Windows regardless of case.
        /// </summary>
        /// <param name="Path">A path.</param>
        private static String Normalize(String Path)
        {

            var fullPath = System.IO.Path.GetFullPath(Path).
                                          TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                                  System.IO.Path.AltDirectorySeparatorChar);

            return OperatingSystem.IsWindows()
                       ? fullPath.ToUpperInvariant()
                       : fullPath;

        }

        #endregion


        [Test]
        public async Task TheSettingsFileIsFoundFromAnyWorkingDirectory()
        {

            var application = typeof(ControlServerService).Assembly.Location;

            // A systemd unit without WorkingDirectory starts in '/', a shell in the
            // repository starts wherever it happens to be. The appsettings.json
            // next to the binary must be found either way, and the host only looks
            // for it in its content root.
            var startInfo   = new ProcessStartInfo(
                                  Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                                  [
                                      application,
                                      "--control-port",                                  "0",
                                      "--Logging:LogLevel:Microsoft.Hosting.Lifetime",   "Information"
                                  ]
                              ) {
                                  WorkingDirectory        = Path.GetTempPath(),
                                  RedirectStandardOutput  = true,
                                  UseShellExecute         = false
                              };

            using var process = Process.Start(startInfo)
                                    ?? throw new InvalidOperationException("The service could not be started!");

            try
            {

                var contentRoot = await ReadContentRootAsync(process.StandardOutput).
                                            WaitAsync(TimeSpan.FromSeconds(30));

                Assert.That(contentRoot, Is.Not.Null,
                            "The service ended before the host reported its content root.");

                Assert.That(Normalize(contentRoot!),
                            Is.EqualTo(Normalize(Path.GetDirectoryName(application)!)),
                            "The content root must be where the service and its appsettings.json are, not the working directory.");

            }
            finally
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

        }

    }

}
