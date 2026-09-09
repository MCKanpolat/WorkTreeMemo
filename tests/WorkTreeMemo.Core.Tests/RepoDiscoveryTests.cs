using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Scanning;

namespace WorkTreeMemo.Core.Tests;

public sealed class RepoDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Discover_finds_repositories_within_depth_and_skips_exclusions()
    {
        CreateRepository("one");
        CreateRepository("nested/two");
        CreateRepository("node_modules/ignored");
        Directory.CreateDirectory(Path.Combine(_root, "linked"));
        File.WriteAllText(Path.Combine(_root, "linked", ".git"), "gitdir: /some/other/path");

        var actual = new RepoDiscovery().Discover([new ScanRoot(_root, 2)], ["node_modules"], CancellationToken.None);

        Assert.Equal(["linked", "one", "two"], actual.Select(repo => repo.Name));
        Assert.DoesNotContain(actual, repo => repo.Name == "ignored");
    }

    [Fact]
    public void Discover_respects_max_depth()
    {
        CreateRepository("a/b/repository");

        var actual = new RepoDiscovery().Discover([new ScanRoot(_root, 1)], [], CancellationToken.None);

        Assert.Empty(actual);
    }

    private void CreateRepository(string relativePath) => Directory.CreateDirectory(Path.Combine(_root, relativePath, ".git"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
