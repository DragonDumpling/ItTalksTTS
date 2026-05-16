using System.Diagnostics;
using System.Text.Json;
using ItTalksTTS.Core;
using ItTalksTTS.Tts.Protocol;

namespace ItTalksTTS.Tts;

public enum KokoroServiceState
{
    Stopped,
    Starting,
    Running,
    Error
}

public sealed class KokoroWorkerSupervisor : IDisposable
{
    private readonly Action<string> _log;
    private Process? _process;
    private StreamWriter? _stdin;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private Task? _stderrTask;

    public KokoroServiceState State { get; private set; } = KokoroServiceState.Stopped;
    public string? LastError { get; private set; }

    public KokoroWorkerSupervisor(Action<string> log) => _log = log;

    public string ResolvePythonExe()
    {
        if (OperatingSystem.IsWindows())
        {
            var venv = Path.Combine(AppPaths.PythonVenv, "Scripts", "python.exe");
            if (File.Exists(venv))
                return venv;
            if (File.Exists(AppPaths.BundledPythonExe)
                && File.Exists(Path.Combine(AppPaths.PythonPackages, ".ittalks-ready")))
                return AppPaths.BundledPythonExe;
        }

        return File.Exists(AppPaths.BundledPythonExe) ? AppPaths.BundledPythonExe : "python";
    }

    private static void ApplyPythonPath(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var venv = Path.Combine(AppPaths.PythonVenv, "Scripts", "python.exe");
        if (File.Exists(venv))
            return;
        var marker = Path.Combine(AppPaths.PythonPackages, ".ittalks-ready");
        if (!File.Exists(marker))
            return;
        psi.Environment["PYTHONPATH"] = AppPaths.PythonPackages;
    }

    public string ResolveWorkerScript()
    {
        var deployed = Path.Combine(AppPaths.WorkerDir, "worker.py");
        if (File.Exists(deployed))
            return deployed;
        var bundled = Path.Combine(AppContext.BaseDirectory, "kokoro_worker", "worker.py");
        if (File.Exists(bundled))
            return bundled;
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "kokoro_worker", "worker.py"));
        return File.Exists(repo) ? repo : deployed;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        LastError = null;
        State = KokoroServiceState.Starting;
        var script = ResolveWorkerScript();
        if (!File.Exists(script))
        {
            State = KokoroServiceState.Error;
            LastError = "worker.py not found. Run Setup from the Voice tab.";
            _log(LastError);
            return;
        }

        var python = ResolvePythonExe();
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
        ApplyPythonPath(psi);
        psi.Environment["KOKORO_MODEL"] = AppPaths.KokoroOnnx;
        psi.Environment["KOKORO_VOICES"] = AppPaths.KokoroVoices;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!p.Start())
        {
            State = KokoroServiceState.Error;
            LastError = "Failed to start Python worker process.";
            _log(LastError);
            return;
        }

        _process = p;
        _stdin = p.StandardInput;
        _stderrTask = Task.Run(() => ReadStdErr(p), cancellationToken);
        _log($"Worker started (PID {p.Id}).");
        State = KokoroServiceState.Running;
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
        State = KokoroServiceState.Stopped;
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

            var line = JsonSerializer.Serialize(request, WorkerJson.Options);
            await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            var outLine = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(outLine))
            {
                LastError = "Empty response from worker.";
                State = KokoroServiceState.Error;
                return new WorkerResponse { Ok = false, Error = LastError };
            }

            return JsonSerializer.Deserialize<WorkerResponse>(outLine, WorkerJson.Options);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = KokoroServiceState.Error;
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
