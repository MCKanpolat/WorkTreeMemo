using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.Core.Git;

public sealed class GitCliReader(TimeSpan? commandTimeout = null)
{
    private readonly TimeSpan _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(20);

    public async Task<RepoSnapshot> ReadAsync(RepoRef repo, IReadOnlyList<string> baseBranches, CancellationToken ct)
    {
        try
        {
            var worktreePaths = await ReadWorktreePathsAsync(repo.Path, ct);
            var branches = await ReadBranchesAsync(repo.Path, baseBranches, ct);
            var worktrees = new List<WorktreeState>();
            foreach (var path in worktreePaths) worktrees.Add(await ReadWorktreeAsync(path, ct));
            return new(repo, branches, worktrees, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            return new(repo, [], [], DateTimeOffset.UtcNow, ex.Message);
        }
        catch (Win32Exception ex)
        {
            return new(repo, [], [], DateTimeOffset.UtcNow, ex.Message);
        }
    }

    private async Task<List<BranchState>> ReadBranchesAsync(string workingDirectory, IReadOnlyList<string> baseBranches,
        CancellationToken ct)
    {
        const string format =
            "%(refname:short)%09%(upstream:short)%09%(upstream:trackshort)%09%(committerdate:iso-strict)%09%(contents:subject)";
        var output = await RunGitAsync(workingDirectory, "for-each-ref", "refs/heads", $"--format={format}", ct);
        var merged = await ReadMergedBranchesAsync(workingDirectory, baseBranches, ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
        {
            var parts = line.Split('\t');
            var track = parts.ElementAtOrDefault(2) ?? string.Empty;
            return new BranchState(
                parts.ElementAtOrDefault(0) ?? string.Empty,
                NullIfEmpty(parts.ElementAtOrDefault(1)),
                ParseTrack(track, '+'), ParseTrack(track, '-'),
                DateTimeOffset.TryParse(parts.ElementAtOrDefault(3), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var date)
                    ? date
                    : null,
                parts.ElementAtOrDefault(4) ?? string.Empty,
                merged.Contains(parts.ElementAtOrDefault(0) ?? string.Empty));
        }).Where(branch => branch.Name.Length > 0).ToList();
    }

    private async Task<HashSet<string>> ReadMergedBranchesAsync(string workingDirectory,
        IReadOnlyList<string> baseBranches, CancellationToken ct)
    {
        foreach (var baseBranch in baseBranches)
        {
            try
            {
                var result = await RunGitAsync(workingDirectory, "branch", "--format=%(refname:short)", "--merged",
                    baseBranch, ct);
                return new(result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.Ordinal);
            }
            catch (InvalidOperationException)
            {
            }
        }

        return [];
    }

    private async Task<List<string>> ReadWorktreePathsAsync(string workingDirectory, CancellationToken ct)
    {
        var output = await RunGitAsync(workingDirectory, "worktree", "list", "--porcelain", ct);
        return output.Split('\n').Where(line => line.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(line => line[9..].Trim()).ToList();
    }

    private async Task<WorktreeState> ReadWorktreeAsync(string path, CancellationToken ct)
    {
        var status = await RunGitAsync(path, "status", "--porcelain=v2", "--branch", "--untracked-files=normal", ct);
        var staged = 0;
        var modified = 0;
        var untracked = 0;
        string? branch = null;
        foreach (var line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                branch = NullIfEmpty(line[14..].Trim().Replace("(detached)", string.Empty, StringComparison.Ordinal));
            else if (line.StartsWith("? ", StringComparison.Ordinal)) untracked++;
            else if (line.Length > 3 && (line[0] is '1' or '2' or 'u'))
            {
                var xy = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "..";
                if (xy[0] != '.') staged++;
                if (xy[1] != '.') modified++;
            }
        }

        var stashOutput = await RunGitAsync(path, "stash", "list", "--format=%gd", ct);
        var stashCount = stashOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return new(path, branch, modified, staged, untracked, stashCount);
    }

    private async Task<string> RunGitAsync(string workingDirectory, string firstArgument, string secondArgument,
        string thirdArgument, CancellationToken ct) =>
        await RunGitAsync(workingDirectory, [firstArgument, secondArgument, thirdArgument], ct);

    private async Task<string> RunGitAsync(string workingDirectory, string firstArgument, string secondArgument,
        string thirdArgument, string fourthArgument, CancellationToken ct) =>
        await RunGitAsync(workingDirectory, [firstArgument, secondArgument, thirdArgument, fourthArgument], ct);

    private async Task<string> RunGitAsync(string workingDirectory, string firstArgument, string secondArgument,
        string thirdArgument, string fourthArgument, string fifthArgument, CancellationToken ct) =>
        await RunGitAsync(workingDirectory,
            [firstArgument, secondArgument, thirdArgument, fourthArgument, fifthArgument], ct);

    private async Task<string> RunGitAsync(string workingDirectory, string firstArgument, string secondArgument,
        CancellationToken ct) =>
        await RunGitAsync(workingDirectory, [firstArgument, secondArgument], ct);

    private async Task<string> RunGitAsync(string workingDirectory, string firstArgument, CancellationToken ct) =>
        await RunGitAsync(workingDirectory, [firstArgument], ct);

    private async Task<string> RunGitAsync(string workingDirectory, string firstArgument, string secondArgument,
        string thirdArgument, string fourthArgument, string fifthArgument, string sixthArgument,
        CancellationToken ct) =>
        await RunGitAsync(workingDirectory,
            [firstArgument, secondArgument, thirdArgument, fourthArgument, fifthArgument, sixthArgument], ct);

    private async Task<string> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_commandTimeout);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git process could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"git exited with {process.ExitCode}."
                : error.Trim());
        return output;
    }

    private static int ParseTrack(string track, char marker) =>
        int.TryParse(new string(track.SkipWhile(value => value != marker).Skip(1).TakeWhile(char.IsDigit).ToArray()),
            out var value)
            ? value
            : 0;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
