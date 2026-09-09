namespace WorkTreeMemo.Core.Models;

public sealed record RepoSnapshot(
    RepoRef Repo,
    IReadOnlyList<BranchState> Branches,
    IReadOnlyList<WorktreeState> Worktrees,
    DateTimeOffset ScannedAt,
    string? Error = null,
    DateTimeOffset? DirectoryModifiedAt = null,
    long? DiskSizeBytes = null);
