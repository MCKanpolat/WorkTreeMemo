using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Scanning;

namespace WorkTreeMemo.Core.Tests;

public sealed class WipClassifierTests
{
    [Fact]
    public void Classify_returns_local_git_categories()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var repo = new RepoSnapshot(
            new RepoRef("/repos/a", "a"),
            [
                new BranchState("feature/a", null, 2, 0, now.AddDays(-8), "work", false),
                new BranchState("old", "origin/old", 0, 0, now, "done", true)
            ],
            [new WorktreeState("/repos/a", "feature/a", 1, 1, 1, 1)], now);
        var notes = new Dictionary<string, Note>
            { [WipClassifier.NoteKey("/repos/a", "feature/a")] = new("waiting", true, now) };

        var actual = new WipClassifier().Classify([repo], notes, 7, now).Select(item => item.Kind).ToHashSet();

        Assert.Contains(WipKind.Unpushed, actual);
        Assert.Contains(WipKind.Dirty, actual);
        Assert.Contains(WipKind.Stashed, actual);
        Assert.Contains(WipKind.StaleUnmerged, actual);
        Assert.Contains(WipKind.Parked, actual);
        Assert.Contains(WipKind.CleanCandidate, actual);
    }
}
