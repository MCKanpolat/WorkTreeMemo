using System.Diagnostics;
using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.Core.Tests;

public sealed class GitCliReaderTests : IDisposable
{
    private readonly string _repositoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadAsync_reads_branches_and_current_worktree_status()
    {
        Directory.CreateDirectory(_repositoryPath);
        RunGit("init", "--initial-branch=main");
        RunGit("config", "user.name", "WorkTreeMemo Tests");
        RunGit("config", "user.email", "tests@worktreememo.local");
        File.WriteAllText(Path.Combine(_repositoryPath, "readme.md"), "initial");
        RunGit("add", "readme.md");
        RunGit("commit", "-m", "Initial commit");
        RunGit("checkout", "-b", "feature/coverage");
        File.WriteAllText(Path.Combine(_repositoryPath, "staged.txt"), "staged");
        RunGit("add", "staged.txt");
        File.AppendAllText(Path.Combine(_repositoryPath, "readme.md"), "\nmodified");
        File.WriteAllText(Path.Combine(_repositoryPath, "untracked.txt"), "untracked");

        var snapshot = await new GitCliReader().ReadAsync(new RepoRef(_repositoryPath, "coverage"), ["main"], CancellationToken.None);

        Assert.Null(snapshot.Error);
        Assert.Contains(snapshot.Branches, branch => branch.Name == "main" && branch.MergedIntoBase);
        Assert.Contains(snapshot.Branches, branch => branch.Name == "feature/coverage" && branch.Upstream is null);
        var worktree = Assert.Single(snapshot.Worktrees);
        Assert.Equal("feature/coverage", worktree.Branch);
        Assert.Equal(1, worktree.Staged);
        Assert.Equal(1, worktree.Modified);
        Assert.Equal(1, worktree.Untracked);
    }

    [Fact]
    public async Task ReadAsync_returns_snapshot_error_for_missing_repository()
    {
        var snapshot = await new GitCliReader().ReadAsync(new RepoRef(Path.Combine(_repositoryPath, "missing"), "missing"), ["main"], CancellationToken.None);

        Assert.NotNull(snapshot.Error);
        Assert.Empty(snapshot.Branches);
    }

    private void RunGit(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = _repositoryPath, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    public void Dispose() { if (Directory.Exists(_repositoryPath)) Directory.Delete(_repositoryPath, true); }
}
