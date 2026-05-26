using System.Text.Json;

namespace ItTalksTTS.Core.Services;

public static class ReleaseVersion
{
    public static Version? ParseTag(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;
        var tag = tagName.Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tag = tag[1..];
        return Version.TryParse(tag, out var v) ? v : null;
    }

    public static string Format(Version version)
    {
        if (version.Revision > 0)
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        if (version.Build > 0)
            return $"{version.Major}.{version.Minor}.{version.Build}";
        return $"{version.Major}.{version.Minor}";
    }

    public static bool IsNewerThan(Version latest, Version current) => latest > current;
}

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    Version? LatestVersion,
    string? ReleaseUrl,
    string? SetupDownloadUrl,
    string? Error)
{
    public static UpdateCheckResult Failed(string error) => new(false, null, null, null, error);
}

public sealed class UpdateCheckService
{
    public const string RepoOwner = "DragonDumpling";
    public const string RepoName = "ItTalksTTS";
    public const string LatestReleaseApiUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public static string LatestReleasePageUrl =>
        $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

    private readonly HttpClient _http;

    public UpdateCheckService(HttpClient? http = null) => _http = http ?? CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ItTalksTTS-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(LatestReleaseApiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return UpdateCheckResult.Failed($"HTTP {(int)resp.StatusCode}");

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            var setupUrl = FindSetupAssetUrl(doc.RootElement);
            var latest = ReleaseVersion.ParseTag(tag);
            if (latest is null)
                return UpdateCheckResult.Failed("invalid release tag");

            var available = ReleaseVersion.IsNewerThan(latest, currentVersion);
            return new UpdateCheckResult(available, latest, url ?? LatestReleasePageUrl, setupUrl, null);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    public static string? FindSetupAssetUrl(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl))
                continue;
            if (!string.Equals(nameEl.GetString(), AppUpdateService.SetupAssetFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (asset.TryGetProperty("browser_download_url", out var urlEl))
                return urlEl.GetString();
        }

        return null;
    }
}
