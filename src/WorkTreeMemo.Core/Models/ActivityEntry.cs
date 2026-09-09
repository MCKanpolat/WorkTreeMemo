namespace WorkTreeMemo.Core.Models;

public sealed record ActivityEntry(DateTimeOffset DetectedAt, string RepoPath, string Description);
