using System.Diagnostics;
using System.Runtime.Versioning;

namespace DotnetScope.IntegrationTests;

[UnsupportedOSPlatform("windows")]
[Trait("Platform", "Unix")]
public sealed class ValidationScriptTests
{
    [Theory]
    [InlineData("docker", "restore", 0)]
    [InlineData("docker", "format", 0)]
    [InlineData("docker", "build", 0)]
    [InlineData("docker", "test", 0)]
    [InlineData("docker", "test", 19)]
    [InlineData("docker", "", 19)]
    [InlineData("docker", "", 0)]
    [InlineData("podman", "test", 0)]
    [InlineData("podman", "", 0)]
    public async Task Container_PreservaFalhaEInterrompeEtapas(string engine, string failingStep, int cleanupExit)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await WriteExecutableAsync(engine, """
                #!/bin/bash
                exec /bin/bash -c "${@: -1}"
                """);
            await WriteExecutableAsync("dotnet", """
                #!/bin/bash
                printf '%s\n' "$*" >> "$VALIDATION_CALLS"
                if [ "$1" = "$VALIDATION_FAIL" ]; then
                    echo "Falha sintetica de $1" >&2
                    exit 37
                fi
                """);
            await WriteExecutableAsync("chown", """
                #!/bin/bash
                echo cleanup >> "$VALIDATION_CALLS"
                exit "$VALIDATION_CLEANUP_EXIT"
                """);
            await WriteExecutableAsync("find", """
                #!/bin/bash
                exit 0
                """);

            var startInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Invoke-Validation.ps1"), "-Engine", engine })
            {
                startInfo.ArgumentList.Add(arg);
            }

            var callsPath = Path.Combine(root, "calls");
            startInfo.Environment["PATH"] = root + Path.PathSeparator + startInfo.Environment["PATH"];
            startInfo.Environment["VALIDATION_CALLS"] = callsPath;
            startInfo.Environment["VALIDATION_FAIL"] = failingStep;
            startInfo.Environment["VALIDATION_CLEANUP_EXIT"] = cleanupExit.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var process = Process.Start(startInfo)!;
            var outputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }

            var output = await outputTask;
            var error = await errorTask;
            var expectedExit = failingStep.Length > 0 ? 37 : engine == "docker" ? cleanupExit : 0;
            Assert.True(process.ExitCode == expectedExit, $"Saída {process.ExitCode}, esperada {expectedExit}.\n{output}\n{error}");
            var calls = await File.ReadAllLinesAsync(callsPath, TestContext.Current.CancellationToken);
            string[] steps = ["restore", "format", "build", "test"];
            var count = failingStep.Length == 0 ? steps.Length : Array.IndexOf(steps, failingStep) + 1;
            Assert.Equal(steps.Take(count), calls.Where(call => call != "cleanup").Select(call => call.Split(' ')[0]));
            Assert.Equal(engine == "docker", calls.Contains("cleanup", StringComparer.Ordinal));
            if (calls.Any(call => call.StartsWith("test ", StringComparison.Ordinal)))
            {
                Assert.Contains("test --solution DotnetScope.slnx --configuration Release --no-build", calls);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        async Task WriteExecutableAsync(string name, string contents)
        {
            var path = Path.Combine(root, name);
            await File.WriteAllTextAsync(path, contents + "\n", TestContext.Current.CancellationToken);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
