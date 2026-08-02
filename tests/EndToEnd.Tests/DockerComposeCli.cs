using System.Diagnostics;

namespace EndToEnd.Tests;

/// <summary>Shells out to the local <c>docker compose</c> CLI against this repo's stack — used by
/// tests that need to manipulate or inspect the running containers directly (stopping a service
/// to test resilience, reading its logs to verify observability).</summary>
internal static class DockerComposeCli
{
    public static async Task RunAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo("docker", $"compose {arguments}")
        {
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'docker compose {arguments}'.");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"'docker compose {arguments}' failed (exit {process.ExitCode}): {stderr}");
        }
    }

    public static async Task<string> LogsAsync(string service, int tailLines = 300)
    {
        var startInfo = new ProcessStartInfo("docker", $"compose logs {service} --tail={tailLines}")
        {
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'docker compose logs {service}'.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate docker-compose.yml above the test binary.");
    }
}
