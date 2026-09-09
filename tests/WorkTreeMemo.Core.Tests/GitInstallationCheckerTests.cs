using WorkTreeMemo.Core.Git;

namespace WorkTreeMemo.Core.Tests;

public sealed class GitInstallationCheckerTests
{
    [Fact]
    public async Task CheckAsync_reports_installed_git()
    {
        var actual = await new GitInstallationChecker().CheckAsync();

        Assert.True(actual.IsAvailable, actual.Detail);
        Assert.StartsWith("git version", actual.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_reports_missing_executable()
    {
        var actual = await new GitInstallationChecker("worktreememo-missing-git").CheckAsync();

        Assert.False(actual.IsAvailable);
        Assert.Contains("not installed", actual.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
