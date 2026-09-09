using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.Core.Scanning;

public static class SnapshotChangeDetector
{
    public static IReadOnlyList<ActivityEntry> Detect(SnapshotDocument previous, SnapshotDocument current)
    {
        var before = previous.Repositories.ToDictionary(repo => repo.Repo.Path, StringComparer.OrdinalIgnoreCase);
        var entries = new List<ActivityEntry>();
        foreach (var repo in current.Repositories)
        {
            if (!before.TryGetValue(repo.Repo.Path, out var old))
            {
                entries.Add(new(repo.ScannedAt, repo.Repo.Path, "Repository discovered"));
                continue;
            }

            if (old.Error != repo.Error)
                entries.Add(new(repo.ScannedAt, repo.Repo.Path, repo.Error is null ? "Scan recovered" : "Scan error"));
            if (DirtyCount(old) != DirtyCount(repo))
                entries.Add(new(repo.ScannedAt, repo.Repo.Path, $"Uncommitted work: {DirtyCount(old)} → {DirtyCount(repo)}"));
            if (StashCount(old) != StashCount(repo))
                entries.Add(new(repo.ScannedAt, repo.Repo.Path, $"Stashes: {StashCount(old)} → {StashCount(repo)}"));
            if (AheadCount(old) != AheadCount(repo))
                entries.Add(new(repo.ScannedAt, repo.Repo.Path, $"Ahead commits: {AheadCount(old)} → {AheadCount(repo)}"));
        }
        return entries;
    }

    private static int DirtyCount(RepoSnapshot repo) => repo.Worktrees.Sum(worktree => worktree.Modified + worktree.Staged + worktree.Untracked);
    private static int StashCount(RepoSnapshot repo) => repo.Worktrees.Sum(worktree => worktree.StashCount);
    private static int AheadCount(RepoSnapshot repo) => repo.Branches.Sum(branch => branch.Ahead);
}
