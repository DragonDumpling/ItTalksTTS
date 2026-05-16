using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ItTalksTTS.Api;

public sealed class EnqueueRequestDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

public sealed class LocalApiServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private WebApplication? _app;
    private readonly QueueManager _queue;
    private readonly Func<AppSettingsModel> _settingsFactory;
    private readonly SettingsStore _store;
    private readonly Action<Guid>? _onEnqueued;

    public LocalApiServer(
        QueueManager queue,
        Func<AppSettingsModel> settingsFactory,
        SettingsStore store,
        Action<Guid>? onEnqueued = null)
    {
        _queue = queue;
        _settingsFactory = settingsFactory;
        _store = store;
        _onEnqueued = onEnqueued;
    }

    public int Port { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.MapGet("/v1/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/v1/queue", async (HttpContext ctx) => await HandleEnqueueAsync(ctx).ConfigureAwait(false));

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;
        var server = app.Services.GetRequiredService<IServer>();
        var feat = server.Features.Get<IServerAddressesFeature>();
        var addr = feat?.Addresses.FirstOrDefault(a => a.StartsWith("http:", StringComparison.OrdinalIgnoreCase));
        Port = ParsePort(addr) ?? 0;
        var settings = _settingsFactory();
        _store.WriteRuntime(new RuntimeInfoModel { Port = Port, Token = settings.ApiToken });
    }

    private async Task<IResult> HandleEnqueueAsync(HttpContext ctx)
    {
        if (!await AuthorizeAsync(ctx).ConfigureAwait(false))
            return Results.Unauthorized();
        EnqueueRequestDto? body;
        try
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(ms.ToArray());
            body = JsonSerializer.Deserialize<EnqueueRequestDto>(json, JsonOptions);
        }
        catch
        {
            return Results.BadRequest();
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Text))
            return Results.BadRequest();
        body.Text = TextEncodingHelper.PrepareForQueue(body.Text);
        var settings = _settingsFactory();
        var filtered = FilterEngine.Apply(body.Text, settings.FilterRules);
        if (string.IsNullOrWhiteSpace(filtered))
            return Results.BadRequest("empty after filters");
        var source = string.IsNullOrWhiteSpace(body.Source) ? "API" : body.Source!;
        var id = _queue.Enqueue(filtered, source);
        try
        {
            _onEnqueued?.Invoke(id);
        }
        catch
        {
            /* ignore callback failures */
        }

        return Results.Json(new { id });
    }

    private async Task<bool> AuthorizeAsync(HttpContext ctx)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var settings = _settingsFactory();
        if (!ctx.Request.Headers.TryGetValue("Authorization", out var auth))
            return false;
        var v = auth.ToString();
        if (!v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var token = v["Bearer ".Length..].Trim();
        return string.Equals(token, settings.ApiToken, StringComparison.Ordinal);
    }

    private static int? ParsePort(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        return !Uri.TryCreate(url, UriKind.Absolute, out var u) ? null : u.Port;
    }

    public async Task StopAsync()
    {
        if (_app is null)
            return;
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
