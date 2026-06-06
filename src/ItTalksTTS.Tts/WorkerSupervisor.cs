using System.Diagnostics;
using System.Text.Json;
using ItTalksTTS.Core;
using ItTalksTTS.Tts.Protocol;

namespace ItTalksTTS.Tts;

public enum TtsServiceState
{
    Stopped,
    Starting,
    Running,
    Error
}

/// <summary>
/// Manages a single TTS worker process (one engine at a time) over the JSON-line
/// protocol. Which engine to run, the interpreter, worker script, and environment
/// all come from the active <see cref="EngineDescriptor"/>.
/// </summary>
public sealed class WorkerSupervisor : IDisposable
{
    private readonly Action<string> _log;
    private readonly Func<EngineDescriptor> _activeEngine;
    private Process? _process;
    private StreamWriter? _stdin;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private Task? _stderrTask;
    private long _requestId;

    public TtsServiceState State { get; private set; } = TtsServiceState.Stopped;
    public string? LastError { get; private set; }

    /// <summary>The engine the most recent StartAsync launched (or would launch).</summary>
    public EngineDescriptor ActiveEngine => _activeEngine();

    public WorkerSupervisor(Action<string> log, Func<EngineDescriptor> activeEngine)
    {
        _log = log;
        _activeEngine = activeEngine;
    }

    public string ResolvePythonExe(EngineDescriptor engine)
    {
        if (OperatingSystem.IsWindows())
        {
            var venv = AppPaths.VenvPython(engine.VenvDir);
            if (File.Exists(venv))
                return venv;
            if (File.Exists(AppPaths.BundledPythonExe)
                && File.Exists(Path.Combine(engine.PackagesDir, AppPaths.PackagesReadyMarker)))
                return AppPaths.BundledPythonExe;
        }

        return File.Exists(AppPaths.BundledPythonExe) ? AppPaths.BundledPythonExe : "python";
    }

    private static void ApplyPythonPath(ProcessStartInfo psi, EngineDescriptor engine)
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (File.Exists(AppPaths.VenvPython(engine.VenvDir)))
            return; // venv has its packages installed in-place
        if (File.Exists(Path.Combine(engine.PackagesDir, AppPaths.PackagesReadyMarker)))
            psi.Environment["PYTHONPATH"] = engine.PackagesDir;
    }

    public string ResolveWorkerScript() => ResolveWorkerScript(ActiveEngine);

    public string ResolveWorkerScript(EngineDescriptor engine)
    {
        var deployed = Path.Combine(AppPaths.WorkerDir, engine.WorkerScript);
        if (File.Exists(deployed))
            return deployed;
        var bundled = Path.Combine(AppContext.BaseDirectory, "kokoro_worker", engine.WorkerScript);
        if (File.Exists(bundled))
            return bundled;
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "kokoro_worker", engine.WorkerScript));
        return File.Exists(repo) ? repo : deployed;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        LastError = null;
        State = TtsServiceState.Starting;

        var engine = _activeEngine();
        var script = ResolveWorkerScript(engine);
        if (!File.Exists(script))
        {
            State = TtsServiceState.Error;
            LastError = $"{engine.WorkerScript} not found. Run Setup from the Voice tab.";
            _log(LastError);
            return;
        }

        var python = ResolvePythonExe(engine);
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
        ApplyPythonPath(psi, engine);
        foreach (var (key, value) in engine.EnvVars)
            psi.Environment[key] = value;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!p.Start())
        {
            State = TtsServiceState.Error;
            LastError = "Failed to start Python worker process.";
            _log(LastError);
            return;
        }

        _process = p;
        _stdin = p.StandardInput;
        _stderrTask = Task.Run(() => ReadStdErr(p), cancellationToken);
        _log($"{engine.DisplayName} worker started (PID {p.Id}).");
        State = TtsServiceState.Running;
    }

    private void ReadStdErr(Process p)
    {
        try
        {
            string? line;
            while ((line = p.StandardError.ReadLine()) != null)
                _log(line);
        }
        catch
        {
            /* ignore */
        }
    }

    public async Task StopAsync()
    {
        State = TtsServiceState.Stopped;
        var stdin = _stdin;
        var p = _process;
        _stdin = null;
        _process = null;

        try
        {
            stdin?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        if (p is { HasExited: false })
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                /* ignore */
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    if (!p.HasExited)
                        p.Kill(entireProcessTree: true);
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        p?.Dispose();
        if (_stderrTask is not null)
        {
            try
            {
                await Task.WhenAny(_stderrTask, Task.Delay(3000)).ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }

            _stderrTask = null;
        }
    }

    public async Task<WorkerResponse?> SendAsync(WorkerRequest request, CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is null || _process.HasExited || _stdin is null)
            {
                LastError = "Worker not running.";
                return new WorkerResponse { Ok = false, Error = LastError };
            }

            var id = Interlocked.Increment(ref _requestId);
            request.Id = id;
            var line = JsonSerializer.Serialize(request, WorkerJson.Options);
            await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Read until we get the response for THIS request. A previous request whose
            // read was cancelled (e.g. Stop during a slow synth) leaves its response in
            // the pipe; discard those stale lines instead of mismatching them to later
            // requests (which caused clips from prior queue rows to play).
            while (true)
            {
                var outLine = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(outLine))
                {
                    LastError = "Empty response from worker.";
                    State = TtsServiceState.Error;
                    return new WorkerResponse { Ok = false, Error = LastError };
                }

                var resp = JsonSerializer.Deserialize<WorkerResponse>(outLine, WorkerJson.Options);
                if (resp is null)
                    continue;
                if (resp.Id is { } respId && respId != id)
                    continue; // stale response from an abandoned request — drop it
                return resp;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = TtsServiceState.Error;
            return new WorkerResponse { Ok = false, Error = ex.Message };
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
    }
}
