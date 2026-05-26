namespace ItTalksTTS.Core.Services;

public sealed class AppUpdateService
{
    public const string SetupAssetFileName = "ItTalksTTS-Setup.exe";

    public async Task<string> DownloadSetupAsync(
        string downloadUrl,
        Version version,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        AppPaths.EnsureRoot();
        Directory.CreateDirectory(AppPaths.UpdatesDir);
        var dest = Path.Combine(AppPaths.UpdatesDir, $"ItTalksTTS-Setup-{ReleaseVersion.Format(version)}.exe");

        using var http = CreateDownloadClient();
        using var resp = await http
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        if (total is > 0)
            log?.Invoke($"Update: downloading {total / (1024 * 1024):0.#} MB...");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(dest);
        await stream.CopyToAsync(file, ct).ConfigureAwait(false);

        log?.Invoke("Update: download complete.");
        return dest;
    }

    public void LaunchInstaller(string setupPath)
    {
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("Installer not found.", setupPath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NOCANCEL",
            UseShellExecute = true,
            Verb = "runas"
        };
        System.Diagnostics.Process.Start(psi);
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ItTalksTTS-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        return client;
    }
}
