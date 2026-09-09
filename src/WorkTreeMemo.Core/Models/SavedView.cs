namespace WorkTreeMemo.Core.Models;

/// <summary>A locally persisted work-queue filter preset.</summary>
public sealed record SavedView(string Name, string SearchText, WipKind? Kind, bool FavoritesOnly, bool FlaggedOnly);
