using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ItTalksTTS.Core;

namespace ItTalksTTS.Tts.Preprocess;

/// <summary>State of the preprocessing worker process.</summary>
public enum PreprocessState
{
    Stopped,
    Starting,
    Running,
    Error
}

/// <summary>
/// Owns the <c>prep_worker.py</c> process and exchanges JSON-line requests with it
/// over stdin/stdout, mirroring <see cref="WorkerSupervisor"/> but kept independent
/// so the preprocessor can stay warm while TTS engines start and stop. The active
/// model is supplied lazily by <paramref name="activeModel"/> so a settings change
/// takes effect on the next start without re-wiring.
/// </summary>
public sealed class PreprocessSupervisor : IDisposable
{
    private readonly Action<string> _log;
    private readonly Func<PreprocessModelDescriptor?> _activeModel;
    private readonly Func<string> _resolvePythonExe;
    private readonly Func<string> _resolveWorkerScript;
    private Process? _process;
    private StreamWriter? _stdin;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private Task? _stderrTask;
    private long _requestId;

    public PreprocessState State { get; private set; } = PreprocessState.Stopped;
    public string? LastError { get; private set; }

    public PreprocessSupervisor(
        Action<string> log,
        Func<PreprocessModelDescriptor?> activeModel,
        Func<string> resolvePythonExe,
        Func<string> resolveWorkerScript)
    {
        _log = log;
        _activeModel = activeModel;
        _resolvePythonExe = resolvePythonExe;
        _resolveWorkerScript = resolveWorkerScript;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        LastError = null;

        var model = _activeModel();
        if (model is null)
        {
            State = PreprocessState.Error;
            LastError = "No preprocessing model selected.";
            _log(LastError);
            return;
        }

        var script = _resolveWorkerScript();
        if (!File.Exists(script))
        {
            State = PreprocessState.Error;
            LastError = "prep_worker.py not found. Run Setup from the Voice tab.";
            _log(LastError);
            return;
        }

        var python = _resolvePythonExe();
        if (!File.Exists(python))
        {
            State = PreprocessState.Error;
            LastError = "Preprocess Python environment not installed. Use the Install button on the Voice tab.";
            _log(LastError);
            return;
        }

        var modelPath = AppPaths.PreprocessModelFile(model.FileName);
        if (!File.Exists(modelPath))
        {
            State = PreprocessState.Error;
            LastError = $"Preprocess model file missing: {model.FileName}";
            _log(LastError);
            return;
        }

        State = PreprocessState.Starting;
        var psi = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script) ?? AppPaths.Root
        };
        psi.ArgumentList.Add(script);
        psi.Environment["ITTALKS_PREP_MODEL"] = modelPath;
        psi.Environment["ITTALKS_PREP_NCTX"] = model.ContextSize.ToString();
        if (!string.IsNullOrWhiteSpace(model.ChatFormat))
            psi.Environment["ITTALKS_PREP_CHAT_FMT"] = model.ChatFormat;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!p.Start())
        {
            State = PreprocessState.Error;
            LastError = "Failed to start preprocess worker process.";
            _log(LastError);
            return;
        }

        _process = p;
        _stdin = p.StandardInput;
        _stderrTask = Task.Run(() => ReadStdErr(p), cancellationToken);
        _log($"Preprocess worker started ({model.DisplayName}, PID {p.Id}).");
        State = PreprocessState.Running;
    }

    private void ReadStdErr(Process p)
    {
        try
        {
            string? line;
            while ((line = p.StandardError.ReadLine()) != null)
            {
                // llama-cpp prints progress/init chatter on stderr; surface it but trim noise.
                if (!string.IsNullOrWhiteSpace(line))
                    _log(line.Length > 400 ? line[..400] + "…" : line);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    public async Task StopAsync()
    {
        State = PreprocessState.Stopped;
        var stdin = _stdin;
        var p = _process;
        _stdin = null;
        _process = null;

        try { stdin?.Dispose(); } catch { /* ignore */ }

        if (p is { HasExited: false })
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }

        p?.Dispose();
        if (_stderrTask is not null)
        {
            try { await Task.WhenAny(_stderrTask, Task.Delay(3000)).ConfigureAwait(false); } catch { /* ignore */ }
            _stderrTask = null;
        }
    }

    /// <summary>Result of a preprocessing pass: the speech-friendly text and a per-word
    /// duration weight (syllable count) array aligned to <see cref="Text"/>.Split() —
    /// used by the UI to map playback progress onto the correct word for highlighting.
    /// <c>Weights</c> is null when the worker didn't provide them (older worker / failure).</summary>
    public sealed record PreprocessResult(string Text, List<int>? Weights);

    /// <summary>Rewrite <paramref name="text"/> into a speech-friendly form.</summary>
    /// <returns>The rewritten text plus optional per-word weights, or the input unchanged
    /// (with null weights) on any failure so speech never blocks.</returns>
    public async Task<PreprocessResult> PreprocessAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new PreprocessResult(text, null);

        if (State != PreprocessState.Running)
        {
            LastError = "Preprocess worker not running.";
            return new PreprocessResult(text, null);
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is null || _process.HasExited || _stdin is null)
            {
                LastError = "Preprocess worker not running.";
                State = PreprocessState.Error;
                return new PreprocessResult(text, null);
            }

            var id = Interlocked.Increment(ref _requestId);
            var line = JsonSerializer.Serialize(new PrepRequest { Id = id, Cmd = "prep", Text = text }, PrepJson.Options);
            await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);

            while (true)
            {
                var outLine = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(outLine))
                {
                    LastError = "Empty response from preprocess worker.";
                    State = PreprocessState.Error;
                    return new PreprocessResult(text, null);
                }

                var resp = JsonSerializer.Deserialize<PrepResponse>(outLine, PrepJson.Options);
                if (resp is null)
                    continue;
                if (resp.Id is { } respId && respId != id)
                    continue; // stale
                if (resp is not { Ok: true } || string.IsNullOrWhiteSpace(resp.Text))
                    return new PreprocessResult(text, null); // graceful fallback — never block speech
                return new PreprocessResult(resp.Text!, resp.Weights);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation (Stop, or the per-request timeout) — the worker process is
            // almost certainly still alive, so don't flip the whole supervisor to Error
            // (that would disable preprocessing for every subsequent clip this session).
            LastError = "preprocess request cancelled";
            return new PreprocessResult(text, null);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            // Only treat it as a fatal worker error if the process actually died.
            if (_process is null || _process.HasExited)
                State = PreprocessState.Error;
            return new PreprocessResult(text, null);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
    }
}

internal sealed class PrepRequest
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("cmd")] public string Cmd { get; init; } = "";
    [JsonPropertyName("text")] public string? Text { get; init; }
}

internal sealed class PrepResponse
{
    [JsonPropertyName("id")] public long? Id { get; init; }
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("weights")] public List<int>? Weights { get; init; }
}

internal static class PrepJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
