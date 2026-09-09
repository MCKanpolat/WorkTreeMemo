namespace WorkTreeMemo.App.Services;

public sealed record ReleaseUpdateCheckResult(bool IsUpdateAvailable, string LatestVersion, Uri? ReleaseUrl);
