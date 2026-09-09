namespace WorkTreeMemo.Core.Models;

public sealed record SnapshotDocument(IReadOnlyList<RepoSnapshot> Repositories, DateTimeOffset UpdatedAt);
