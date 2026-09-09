using System.Text.Json;

namespace WorkTreeMemo.App.Services;

public sealed class GitHubReleaseUpdateChecker(HttpClient httpClient, ApplicationVersionProvider applicationVersionProvider)
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/MCKanpolat/WorkTreeMemo/releases/latest");

    public async Task<ReleaseUpdateCheckResult?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.UserAgent.ParseAdd("WorkTreeMemo-UpdateChecker");
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var tag = document.RootElement.GetProperty("tag_name").GetString()?.Trim() ?? string.Empty;
            var url = document.RootElement.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() : null;
            var latest = tag.TrimStart('v', 'V');
            return new(IsNewer(latest, applicationVersionProvider.Version), latest, Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null);
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    private static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out var latestVersion) && Version.TryParse(current, out var currentVersion) && latestVersion > currentVersion;
}
