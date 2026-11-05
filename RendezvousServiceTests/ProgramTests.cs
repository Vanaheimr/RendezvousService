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
using System.Net;
using System.Net.Sockets;

using org.GraphDefined.Vanaheimr.Rendezvous.Service;

#endregion

namespace org.GraphDefined.Vanaheimr.RendezvousService.Tests
{

    /// <summary>
    /// Tests for Program.cs itself, run as the process it is.
    ///
    /// Everything Program.cs registers is tested without a process, see
    /// RendezvousServiceCollectionExtensionsTests. What only a process can show
    /// is where the host looks for its files, and how the process ends when it
    /// can not start: with what log, and with what exit code.
    /// </summary>
    [TestFixture]
    public class ProgramTests
    {

        #region (private) DotNet, Application

        /// <summary>
        /// The dotnet the tests run on, so that the service runs on the same one.
        /// </summary>
        private static String DotNet
            => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

        /// <summary>
        /// The service, next to its appsettings.json.
        /// </summary>
        private static String Application
            => typeof(ControlServerService).Assembly.Location;

        #endregion

        #region (private) RunToExitAsync(params Arguments)

        /// <summary>
        /// Run the service from a foreign working directory until it ends on its
        /// own, and return its exit code together with everything it wrote.
        /// </summary>
        /// <param name="Arguments">The command line arguments of the service.</param>
        private static async Task<(Int32 ExitCode, String Output)> RunToExitAsync(params String[] Arguments)
        {

            var startInfo = new ProcessStartInfo(DotNet, [ Application, .. Arguments ]) {
                                WorkingDirectory        = Path.GetTempPath(),
                                RedirectStandardOutput  = true,
                                RedirectStandardError   = true,
                                UseShellExecute         = false
                            };

            using var process = Process.Start(startInfo)
                                    ?? throw new InvalidOperationException("The service could not be started!");

            // Both at once, or a full pipe on the one not read blocks the service.
            var standardOutput  = process.StandardOutput.ReadToEndAsync();
            var standardError   = process.StandardError. ReadToEndAsync();

            var stillRunning = false;

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                stillRunning = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            var output = await standardOutput + await standardError;

            Assert.That(stillRunning, Is.False,
                        $"The service was still running after 30 seconds, but it must not have started at all:{Environment.NewLine}{output}");

            return (process.ExitCode, output);

        }

        #endregion

        #region (private) ProblemsIn(Output)

        /// <summary>
        /// The configuration problems the service logged, one per line it wrote.
        /// </summary>
        /// <param name="Output">Everything the service wrote.</param>
        private static IReadOnlyList<String> ProblemsIn(String Output)
        {

            const String prefix = "Invalid configuration: ";

            return Output.Split('\n').
                          Select(line => line.Trim()).
                          Where (line => line.StartsWith(prefix, StringComparison.Ordinal)).
                          Select(line => line[prefix.Length..]).
                          ToArray();

        }

        #endregion

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

            // A systemd unit without WorkingDirectory starts in '/', a shell in the
            // repository starts wherever it happens to be. The appsettings.json
            // next to the binary must be found either way, and the host only looks
            // for it in its content root.
            var startInfo   = new ProcessStartInfo(
                                  DotNet,
                                  [
                                      Application,
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
                            Is.EqualTo(Normalize(Path.GetDirectoryName(Application)!)),
                            "The content root must be where the service and its appsettings.json are, not the working directory.");

            }
            finally
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

        }

        [Test]
        public async Task AValueThatCanNotBeReadEndsTheServiceWithExitCode1()
        {

            // The binder gives up on the first value it can not convert, so this
            // is one problem by nature - and it must name the setting, or the
            // operator is left guessing which of the ports is meant.
            var (exitCode, output) = await RunToExitAsync("--min-port",      "70000",
                                                          "--control-port",  "0");

            var problems = ProblemsIn(output);

            Assert.Multiple(() => {
                Assert.That(exitCode,     Is.EqualTo(1),                                 output);
                Assert.That(problems,     Has.Count.EqualTo(1),                          output);
                Assert.That(problems.FirstOrDefault(),
                                          Does.Contain("Rendezvous:MinDataPort").
                                           And.Contain("70000"),                         output);
                Assert.That(output,       Does.Not.Contain("Unhandled exception"),       output);
                Assert.That(output,       Does.Not.Match("(?m)^\\s+at "),
                            "A mistake in the configuration needs no stack trace!");
            });

        }

        [Test]
        public async Task EveryRejectedValueIsReportedAndEndsTheServiceWithExitCode1()
        {

            // The validator, unlike the binder, sees all values at once, so the
            // operator gets every problem in one go rather than one per restart.
            var (exitCode, output) = await RunToExitAsync("--min-port",      "2000",
                                                          "--max-port",      "1500",
                                                          "--idle-timeout",  "00:00:00",
                                                          "--control-port",  "0");

            var problems = ProblemsIn(output);

            Assert.Multiple(() => {
                Assert.That(exitCode,  Is.EqualTo(1),                                    output);
                Assert.That(problems,  Has.Count.EqualTo(2),                             output);
                Assert.That(problems,  Has.Exactly(1).Contains("MinDataPort"),           output);
                Assert.That(problems,  Has.Exactly(1).Contains("IdleTimeout"),           output);
                Assert.That(output,    Does.Not.Contain("Unhandled exception"),          output);
                Assert.That(output,    Does.Not.Match("(?m)^\\s+at "),
                            "A mistake in the configuration needs no stack trace!");
            });

        }

        [Test]
        public async Task ATakenControlPortEndsTheServiceWithExitCode1()
        {

            using var occupied = new TcpListener(IPAddress.Loopback, 0);
            occupied.Start();

            var (exitCode, output) = await RunToExitAsync("--control-port",
                                                          ((IPEndPoint) occupied.LocalEndpoint).Port.ToString());

            // The host logs why it could not start, with the exception. It must
            // not escape Program.cs as well: that is a crash with an exit code of
            // its own, and the same stack trace a second time.
            Assert.Multiple(() => {
                Assert.That(exitCode,  Is.EqualTo(1),                                    output);
                Assert.That(output,    Does.Contain("SocketException"),
                            "The log must say why the service could not start!");
                Assert.That(output,    Does.Not.Contain("Unhandled exception"),          output);
            });

        }

    }

}
