namespace WorkTreeMemo.Core.Models;

public sealed record BranchState(
    string Name,
    string? Upstream,
    int Ahead,
    int Behind,
    DateTimeOffset? LastCommitAt,
    string Subject,
    bool MergedIntoBase);
