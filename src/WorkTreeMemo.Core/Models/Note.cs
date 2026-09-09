namespace WorkTreeMemo.Core.Models;

public sealed record Note(
    string Text,
    bool IsParked,
    DateTimeOffset UpdatedAt,
    bool IsFlagged = false,
    DateTimeOffset? SnoozedUntil = null,
    string? NextStep = null,
    string? LocalPath = null);
