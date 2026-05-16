using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ItTalksTTS.Core.Services;
using ModelContextProtocol.Server;

namespace ItTalksTTS.McpServer;

[McpServerToolType]
public static class ItTalksTools
{
    [McpServerTool, Description("Add text to the ItTalksTTS playback queue. Requires the desktop app running with the local API started.")]
    public static async Task<string> EnqueueTts(
        [Description("Plain text to speak after filters are applied in the app.")] string text,
        [Description("Shown in The Q as the source, e.g. mcp or cursor.")] string? source = "mcp",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "error: empty text";
        var store = new SettingsStore();
        var rt = store.ReadRuntime();
        if (rt is null || rt.Port <= 0 || string.IsNullOrWhiteSpace(rt.Token))
            return "error: ItTalksTTS API not available. Start the ItTalksTTS app first (runtime.json missing or invalid).";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rt.Token);
        var payload = JsonSerializer.Serialize(new { text, source });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await http
            .PostAsync(new Uri($"http://127.0.0.1:{rt.Port}/v1/queue"), content, cancellationToken)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return resp.IsSuccessStatusCode
            ? body
            : $"error: HTTP {(int)resp.StatusCode} {body}";
    }

    [McpServerTool, Description("Returns whether runtime.json exists and which port is configured.")]
    public static string GetApiStatus()
    {
        var store = new SettingsStore();
        var rt = store.ReadRuntime();
        return rt is null
            ? "ItTalksTTS app does not appear to be running (no runtime.json)."
            : $"runtime.json: port {rt.Port}, token length {rt.Token.Length}.";
    }
}
