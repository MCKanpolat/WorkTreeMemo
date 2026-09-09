using System.ComponentModel;
using System.Diagnostics;

namespace WorkTreeMemo.Core.Git;

public sealed class GitInstallationChecker(string executable = "git")
{
    public async Task<GitInstallationStatus> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var start = new ProcessStartInfo(executable)
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start);
            if (process is null) return new(false, "Git CLI could not be started.");
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) return new(false, "Git CLI did not return a successful version check.");
            var version = (await process.StandardOutput.ReadToEndAsync(timeout.Token)).Trim();
            return new(true, version);
        }
        catch (Win32Exception)
        {
            return new(false, "Git is not installed or is not available on PATH.");
        }
        catch (FileNotFoundException)
        {
            return new(false, "Git is not installed or is not available on PATH.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "Git version check timed out.");
        }
    }
}

public sealed record GitInstallationStatus(bool IsAvailable, string Detail);
