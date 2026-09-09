namespace WorkTreeMemo.Core.Models;

public sealed record Note(string Text, bool IsParked, DateTimeOffset UpdatedAt);
