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
    int? IgnoreBranchesOlderThanDays = null)
{
    public static AppConfiguration Default { get; } = new(
        [], ["node_modules", "bin", "obj", ".venv"], ["main", "master", "develop"],
        TimeSpan.FromMinutes(10), 4, 7, "en-US");
}
