using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ItTalksTTS.Core.Services;

namespace ItTalksTTS.HookEnqueue;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            await RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Log("fatal -- " + ex.Message);
            return 1;
        }
    }

    private static void Log(string message) => Console.Error.WriteLine("ittalks-hook: " + message);

    private static async Task RunAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var ms = new MemoryStream();
        await stdin.CopyToAsync(ms).ConfigureAwait(false);
        var raw = TextEncodingHelper.DecodeHookStdin(ms.ToArray());
        if (string.IsNullOrWhiteSpace(raw))
        {
            Log("empty stdin");
            return;
        }

        var json = raw.Trim();
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
            json = json.Substring(start, end - start + 1);

        string? text;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("text", out var textEl))
            {
                Log("no text property in hook JSON");
                return;
            }

            text = textEl.GetString();
        }
        catch (Exception ex)
        {
            Log("invalid JSON -- " + ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Log("empty text");
            return;
        }

        const int max = 400_000;
        if (text.Length > max)
            text = text[..max];

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rtPath = Path.Combine(appData, "ItTalksTTS", "runtime.json");
        var settingsPath = Path.Combine(appData, "ItTalksTTS", "settings.json");
        if (!File.Exists(rtPath))
        {
            Log("runtime.json missing -- start ItTalksTTS first");
            return;
        }

        if (!File.Exists(settingsPath))
        {
            Log("settings.json missing");
            return;
        }

        var rtJson = await File.ReadAllTextAsync(rtPath, Encoding.UTF8).ConfigureAwait(false);
        var settingsJson = await File.ReadAllTextAsync(settingsPath, Encoding.UTF8).ConfigureAwait(false);
        using var rtDoc = JsonDocument.Parse(rtJson);
        using var settingsDoc = JsonDocument.Parse(settingsJson);
        if (!rtDoc.RootElement.TryGetProperty("port", out var portEl) || portEl.ValueKind != JsonValueKind.Number)
        {
            Log("invalid port in runtime.json");
            return;
        }

        if (!settingsDoc.RootElement.TryGetProperty("apiToken", out var tokenEl))
        {
            Log("apiToken missing in settings.json");
            return;
        }

        var port = portEl.GetInt32();
        var token = tokenEl.GetString();
        if (port <= 0 || string.IsNullOrWhiteSpace(token))
        {
            Log("invalid port or apiToken");
            return;
        }

        var payload = JsonSerializer.Serialize(new { text, source = "cursor-hook" });
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await http
            .PostAsync(new Uri($"http://127.0.0.1:{port}/v1/queue"), content)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
            Log($"enqueued to The Q ({text.Length} chars)");
        else
            Log($"HTTP {(int)resp.StatusCode} {body}");
    }
}
