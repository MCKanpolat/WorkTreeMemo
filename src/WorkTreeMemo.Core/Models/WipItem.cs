namespace WorkTreeMemo.Core.Models;

public sealed record WipItem(
    RepoSnapshot Repository,
    BranchState? Branch,
    WorktreeState? Worktree,
    WipKind Kind,
    string Detail);
