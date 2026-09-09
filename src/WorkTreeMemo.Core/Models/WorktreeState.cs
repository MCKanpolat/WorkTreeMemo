namespace WorkTreeMemo.Core.Models;

public sealed record WorktreeState(
    string Path,
    string? Branch,
    int Modified,
    int Staged,
    int Untracked,
    int StashCount,
    DateTimeOffset? LatestStashAt = null);
