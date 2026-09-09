namespace WorkTreeMemo.Core.Models;

public sealed record AppConfiguration(
    List<ScanRoot> Roots,
    List<string> ExcludedDirectoryNames,
    List<string> BaseBranches,
    TimeSpan ScanInterval,
    int MaxParallelism,
    int StaleAfterDays,
    string Culture,
    ThemePreference? Theme = null,
    int? IgnoreBranchesOlderThanDays = null,
    List<string>? ExcludedRepositoryPaths = null,
    List<string>? FavoriteRepositoryPaths = null,
    List<SavedView>? SavedViews = null,
    WindowPlacement? LastWindowPlacement = null)
{
    public static AppConfiguration Default { get; } = new(
        [], ["node_modules", "bin", "obj", ".venv"], ["main", "master", "develop"],
        TimeSpan.FromMinutes(10), 4, 14, "en-US", IgnoreBranchesOlderThanDays: 14);
}
