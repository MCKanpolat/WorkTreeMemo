using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Scanning;

namespace WorkTreeMemo.Core.Tests;

public sealed class SnapshotChangeDetectorTests
{
    [Fact]
    public void Detect_records_changed_work_and_stashes()
    {
        var now = DateTimeOffset.UtcNow;
        var previous = new SnapshotDocument([Repo(0, 0, 0, now)], now.AddMinutes(-1));
        var current = new SnapshotDocument([Repo(2, 1, 3, now)], now);

        var actual = SnapshotChangeDetector.Detect(previous, current);

        Assert.Contains(actual, entry => entry.Description.StartsWith("Uncommitted work", StringComparison.Ordinal));
        Assert.Contains(actual, entry => entry.Description.StartsWith("Stashes", StringComparison.Ordinal));
        Assert.Contains(actual, entry => entry.Description.StartsWith("Ahead commits", StringComparison.Ordinal));
    }

    private static RepoSnapshot Repo(int dirty, int stashes, int ahead, DateTimeOffset now) => new(
        new RepoRef("/repo", "repo"), [new BranchState("main", null, ahead, 0, now, "", false)],
        [new WorktreeState("/repo", "main", dirty, 0, 0, stashes)], now);
}
