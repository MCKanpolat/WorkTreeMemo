using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Scanning;

namespace WorkTreeMemo.Core.Tests;

public sealed class BranchAgeCutoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private static RepoSnapshot RepoWithBranchLastTouched(DateTimeOffset? lastCommitAt) => new(
        new RepoRef("/repos/a", "a"),
        [new BranchState("feature/ancient", null, 2, 0, lastCommitAt, "work", false)],
        [new WorktreeState("/repos/a", "feature/ancient", 1, 0, 0, 1)], Now);

    private static HashSet<WipKind> Classify(RepoSnapshot repo, int? cutoff,
        Dictionary<string, Note>? notes = null) =>
        new WipClassifier().Classify([repo], notes ?? [], 7, Now, cutoff)
            .Select(item => item.Kind).ToHashSet();

    [Fact]
    public void Without_a_cutoff_an_ancient_branch_is_still_reported()
    {
        var actual = Classify(RepoWithBranchLastTouched(Now.AddDays(-400)), null);
        Assert.Contains(WipKind.Unpushed, actual);
        Assert.Contains(WipKind.StaleUnmerged, actual);
    }

    [Fact]
    public void A_branch_older_than_the_cutoff_is_left_out()
    {
        var actual = Classify(RepoWithBranchLastTouched(Now.AddDays(-400)), 90);
        Assert.DoesNotContain(WipKind.Unpushed, actual);
        Assert.DoesNotContain(WipKind.StaleUnmerged, actual);
    }

    [Fact]
    public void Uncommitted_and_stashed_work_survives_the_cutoff_because_it_has_no_branch_date()
    {
        var actual = Classify(RepoWithBranchLastTouched(Now.AddDays(-400)), 90);
        Assert.Contains(WipKind.Dirty, actual);
        Assert.Contains(WipKind.Stashed, actual);
    }

    [Fact]
    public void A_branch_within_the_cutoff_is_unaffected()
        => Assert.Contains(WipKind.Unpushed, Classify(RepoWithBranchLastTouched(Now.AddDays(-10)), 90));

    [Fact]
    public void A_branch_with_no_commit_date_is_kept_rather_than_silently_dropped()
        => Assert.Contains(WipKind.Unpushed, Classify(RepoWithBranchLastTouched(null), 90));

    [Fact]
    public void Parking_a_branch_keeps_it_visible_past_the_cutoff()
    {
        var notes = new Dictionary<string, Note>
            { [WipClassifier.NoteKey("/repos/a", "feature/ancient")] = new("waiting on review", true, Now) };
        var actual = Classify(RepoWithBranchLastTouched(Now.AddDays(-400)), 90, notes);
        Assert.Contains(WipKind.Parked, actual);
    }

    [Fact]
    public void A_cutoff_of_zero_means_no_cutoff()
        => Assert.Contains(WipKind.Unpushed, Classify(RepoWithBranchLastTouched(Now.AddDays(-400)), 0));
}
