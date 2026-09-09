using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.Core.Scanning;

public sealed class WipClassifier
{
    /// <param name="ignoreBranchesOlderThanDays">
    /// Branches untouched for longer than this are left out entirely, so a long history
    /// of abandoned work does not drown the list. Null or zero keeps every branch.
    /// Uncommitted and stashed work is never affected: it belongs to a worktree rather
    /// than to a branch, and its age is not known. A branch the user has parked also
    /// stays, because parking is an explicit request to keep it in view.
    /// </param>
    public IReadOnlyList<WipItem> Classify(IEnumerable<RepoSnapshot> repositories,
        IReadOnlyDictionary<string, Note> notes, int staleAfterDays, DateTimeOffset now,
        int? ignoreBranchesOlderThanDays = null)
    {
        var items = new List<WipItem>();
        foreach (var repo in repositories.Where(repo => repo.Error is null))
        {
            foreach (var worktree in repo.Worktrees)
            {
                if (worktree.Modified + worktree.Staged + worktree.Untracked > 0)
                    items.Add(new(repo, null, worktree, WipKind.Dirty, "Uncommitted changes"));
                if (worktree.StashCount > 0)
                    items.Add(new(repo, null, worktree, WipKind.Stashed, $"{worktree.StashCount} stash(es)"));
            }

            foreach (var branch in repo.Branches)
            {
                var key = NoteKey(repo.Repo.Path, branch.Name);
                var isParked = notes.TryGetValue(key, out var note) && note.IsParked;
                if (isParked) items.Add(new(repo, branch, null, WipKind.Parked, note!.Text));
                if (!isParked && IsBeyondCutoff(branch, ignoreBranchesOlderThanDays, now)) continue;
                if (branch.Ahead > 0 || branch.Upstream is null)
                    items.Add(new(repo, branch, null, WipKind.Unpushed,
                        branch.Ahead > 0 ? $"{branch.Ahead} commit(s) ahead" : "No upstream"));
                if (!branch.MergedIntoBase && branch.LastCommitAt < now.AddDays(-staleAfterDays))
                    items.Add(new(repo, branch, null, WipKind.StaleUnmerged, "Old, unmerged local branch"));
                if (branch.MergedIntoBase)
                    items.Add(new(repo, branch, null, WipKind.CleanCandidate, "Merged into a configured base"));
            }
        }

        return items;
    }

    private static bool IsBeyondCutoff(BranchState branch, int? cutoffDays, DateTimeOffset now) =>
        cutoffDays is > 0 && branch.LastCommitAt is { } lastCommitAt &&
        lastCommitAt < now.AddDays(-cutoffDays.Value);

    public static string NoteKey(string repoPath, string branch) => $"{repoPath}\n{branch}";
}
