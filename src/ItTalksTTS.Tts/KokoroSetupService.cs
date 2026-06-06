using System.Diagnostics;
using ItTalksTTS.Core;

namespace ItTalksTTS.Tts;

public sealed class KokoroSetupService
{
    private const string PackagesReadyMarker = ".ittalks-ready";
    private readonly Action<string> _log;

    public KokoroSetupService(Action<string> log) => _log = log;

    public async Task RunSetupAsync(IProgress<(string step, double? fraction)>? progress, CancellationToken cancellationToken)
    {
        AppPaths.EnsureRoot();
        Directory.CreateDirectory(AppPaths.ModelsDir);
        Directory.CreateDirectory(Path.Combine(AppPaths.Root, "python"));

        progress?.Report(("Preparing Python…", 0.05));
        await EnsurePythonPackagesAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(("Downloading Kokoro ONNX model…", 0.2));
        await DownloadIfMissingAsync(
            "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx",
            AppPaths.KokoroOnnx,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(("Downloading voice bundle…", 0.55));
        await DownloadIfMissingAsync(
            "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin",
            AppPaths.KokoroVoices,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(("Deploying worker script…", 0.85));
        DeployWorkerFiles();

        progress?.Report(("Done.", 1.0));
    }

    /// <summary>
    /// Install/repair a specific engine. Kokoro routes to the existing self-contained
    /// flow; torch engines (F5-TTS) get their own venv with CUDA/CPU torch wheels and
    /// the engine's requirements, then pre-download their model.
    /// </summary>
    public async Task InstallEngineAsync(
        EngineDescriptor engine,
        IProgress<(string step, double? fraction)>? progress,
        CancellationToken cancellationToken)
    {
        AppPaths.EnsureRoot();
        DeployWorkerFiles();

        if (!engine.NeedsTorch)
        {
            await RunSetupAsync(progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(engine.ModelsDir);

        progress?.Report(("Preparing Python environment…", 0.05));
        var venvPython = await EnsureEngineVenvAsync(engine, cancellationToken).ConfigureAwait(false);

        var cuda = DetectCuda();
        var index = cuda
            ? "https://download.pytorch.org/whl/cu128"
            : "https://download.pytorch.org/whl/cpu";
        progress?.Report(($"Installing PyTorch ({(cuda ? "CUDA" : "CPU")})… this is a large download", 0.2));
        await RunPythonAsync(venvPython, new[] { "-m", "pip", "install", "torch", "torchaudio", "--index-url", index }, null, cancellationToken)
            .ConfigureAwait(false);

        var req = Path.Combine(AppPaths.WorkerDir, engine.RequirementsFile);
        if (!File.Exists(req))
            throw new InvalidOperationException($"Missing {req}");
        progress?.Report(($"Installing {engine.DisplayName} packages…", 0.6));
        await RunPythonAsync(venvPython, new[] { "-m", "pip", "install", "-r", req }, null, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(("Downloading the voice model…", 0.85));
        var modelEnv = new Dictionary<string, string>(engine.EnvVars);
        await RunPythonAsync(
            venvPython,
            new[] { "-c", "from f5_tts.api import F5TTS; F5TTS(); print('model ready')" },
            modelEnv,
            cancellationToken).ConfigureAwait(false);

        File.WriteAllText(engine.ReadyMarkerPath, DateTime.UtcNow.ToString("O"));
        progress?.Report(("Done.", 1.0));
    }

    private async Task<string> EnsureEngineVenvAsync(EngineDescriptor engine, CancellationToken cancellationToken)
    {
        var venvPython = AppPaths.VenvPython(engine.VenvDir);
        if (File.Exists(venvPython))
            return venvPython;

        var basePython = FindSystemPython()
            ?? throw new InvalidOperationException(
                "F5-TTS needs a system Python 3.10–3.12. Install it from https://python.org "
                + "(tick \"Add python.exe to PATH\"), then run Setup again.");

        Directory.CreateDirectory(Path.GetDirectoryName(engine.VenvDir)!);
        var create = NewProcess(basePython.Exe);
        foreach (var a in basePython.Prefix)
            create.ArgumentList.Add(a);
        create.ArgumentList.Add("-m");
        create.ArgumentList.Add("venv");
        create.ArgumentList.Add(engine.VenvDir);
        await RunProcessAsync(create, cancellationToken).ConfigureAwait(false);

        if (!File.Exists(venvPython))
            throw new InvalidOperationException($"venv creation did not produce {venvPython}");
        await RunPythonAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip", "wheel" }, null, cancellationToken)
            .ConfigureAwait(false);
        return venvPython;
    }

    private bool DetectCuda()
    {
        try
        {
            var psi = NewProcess("nvidia-smi");
            psi.ArgumentList.Add("-L");
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            if (!p.WaitForExit(5000))
            {
                try { p.Kill(); } catch { /* ignore */ }
                return false;
            }

            var detected = p.ExitCode == 0;
            _log(detected ? "GPU detected (nvidia-smi) — installing CUDA PyTorch." : "No NVIDIA GPU — installing CPU PyTorch.");
            return detected;
        }
        catch
        {
            _log("nvidia-smi not found — installing CPU PyTorch.");
            return false;
        }
    }

    private (string Exe, string[] Prefix)? FindSystemPython()
    {
        var candidates = new (string Exe, string[] Prefix)[]
        {
            ("py", new[] { "-3" }),
            ("python", Array.Empty<string>()),
            ("python3", Array.Empty<string>()),
        };
        foreach (var c in candidates)
        {
            try
            {
                var psi = NewProcess(c.Exe);
                foreach (var a in c.Prefix)
                    psi.ArgumentList.Add(a);
                psi.ArgumentList.Add("--version");
                using var p = Process.Start(psi);
                if (p is null)
                    continue;
                var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Python 3\.(\d+)");
                if (p.ExitCode == 0 && m.Success && int.TryParse(m.Groups[1].Value, out var minor)
                    && minor is >= 10 and <= 12)
                    return c;
            }
            catch
            {
                /* try next */
            }
        }

        return null;
    }

    private static ProcessStartInfo NewProcess(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };

    private async Task RunPythonAsync(string pythonExe, string[] args, IReadOnlyDictionary<string, string>? env, CancellationToken cancellationToken)
    {
        var psi = NewProcess(pythonExe);
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (env is not null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;
        await RunProcessAsync(psi, cancellationToken).ConfigureAwait(false);
    }

    public void DeployWorkerFiles()
    {
        Directory.CreateDirectory(AppPaths.WorkerDir);
        var srcDir = ResolveBundledWorkerDir();
        if (srcDir is null)
            throw new InvalidOperationException("Bundled kokoro_worker folder not found next to the app.");
        foreach (var file in Directory.GetFiles(srcDir))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(AppPaths.WorkerDir, name), true);
        }
    }

    /// <summary>
    /// Refresh the deployed worker *.py scripts from the copy bundled with this build.
    /// ResolveWorkerScript prefers the deployed copy, so without this an app update would
    /// keep running the previous version's worker.py. Only runs when a worker was already
    /// deployed (i.e. setup completed before); never throws — logging must not break launch.
    /// </summary>
    public void TryRefreshDeployedWorkerScripts()
    {
        try
        {
            var deployed = Path.Combine(AppPaths.WorkerDir, "worker.py");
            if (!File.Exists(deployed))
                return; // Setup hasn't run yet; first setup will deploy the current scripts.
            var srcDir = ResolveBundledWorkerDir();
            if (srcDir is null)
                return;
            // Refresh all worker assets (scripts, requirements, the F5 reference clip)
            // so engine files added by an update reach existing installs.
            foreach (var file in Directory.GetFiles(srcDir))
            {
                var dst = Path.Combine(AppPaths.WorkerDir, Path.GetFileName(file));
                if (FilesDiffer(file, dst))
                {
                    File.Copy(file, dst, true);
                    _log($"Refreshed worker file: {Path.GetFileName(file)}");
                }
            }
        }
        catch (Exception ex)
        {
            _log("Worker script refresh skipped: " + ex.Message);
        }
    }

    private static bool FilesDiffer(string src, string dst)
    {
        if (!File.Exists(dst))
            return true;
        var a = new FileInfo(src);
        var b = new FileInfo(dst);
        if (a.Length != b.Length)
            return true;
        return !File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(dst));
    }

    private static string? ResolveBundledWorkerDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var a = Path.Combine(baseDir, "kokoro_worker");
        if (Directory.Exists(a))
            return a;
        var b = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "tools", "kokoro_worker"));
        if (Directory.Exists(b))
            return b;
        return null;
    }

    private async Task EnsurePythonPackagesAsync(CancellationToken cancellationToken)
    {
        DeployWorkerFiles();
        var reqPath = Path.Combine(AppPaths.WorkerDir, "requirements.txt");
        if (!File.Exists(reqPath))
            throw new InvalidOperationException($"Missing {reqPath}");

        if (File.Exists(AppPaths.BundledPythonExe))
        {
            await EnsureBundledPipAsync(cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(AppPaths.PythonPackages);
            await PipInstallAsync(AppPaths.BundledPythonExe, reqPath, AppPaths.PythonPackages, cancellationToken)
                .ConfigureAwait(false);
            File.WriteAllText(Path.Combine(AppPaths.PythonPackages, PackagesReadyMarker), DateTime.UtcNow.ToString("O"));
            return;
        }

        if (!Directory.Exists(AppPaths.PythonVenv))
        {
            var create = new ProcessStartInfo
            {
                FileName = "python",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            create.ArgumentList.Add("-m");
            create.ArgumentList.Add("venv");
            create.ArgumentList.Add(AppPaths.PythonVenv);
            await RunProcessAsync(create, cancellationToken).ConfigureAwait(false);
        }

        var pip = Path.Combine(AppPaths.PythonVenv, "Scripts", "pip.exe");
        if (!File.Exists(pip))
            throw new InvalidOperationException(
                "pip not found. Install Python 3.10+ from python.org, or use the ItTalksTTS Windows installer (includes Python).");

        var install = new ProcessStartInfo
        {
            FileName = pip,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        install.ArgumentList.Add("install");
        install.ArgumentList.Add("-r");
        install.ArgumentList.Add(reqPath);
        await RunProcessAsync(install, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBundledPipAsync(CancellationToken cancellationToken)
    {
        var probe = new ProcessStartInfo
        {
            FileName = AppPaths.BundledPythonExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        probe.ArgumentList.Add("-m");
        probe.ArgumentList.Add("pip");
        probe.ArgumentList.Add("--version");
        try
        {
            await RunProcessAsync(probe, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch
        {
            /* install pip */
        }

        var getPip = Path.Combine(Path.GetDirectoryName(AppPaths.BundledPythonExe)!, "get-pip.py");
        if (!File.Exists(getPip))
            throw new InvalidOperationException($"Missing {getPip} next to bundled Python.");

        var bootstrap = new ProcessStartInfo
        {
            FileName = AppPaths.BundledPythonExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        bootstrap.ArgumentList.Add(getPip);
        bootstrap.ArgumentList.Add("--no-warn-script-location");
        await RunProcessAsync(bootstrap, cancellationToken).ConfigureAwait(false);
    }

    private async Task PipInstallAsync(string pythonExe, string requirements, string targetDir, CancellationToken cancellationToken)
    {
        var install = new ProcessStartInfo
        {
            FileName = pythonExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        install.ArgumentList.Add("-m");
        install.ArgumentList.Add("pip");
        install.ArgumentList.Add("install");
        install.ArgumentList.Add("-r");
        install.ArgumentList.Add(requirements);
        install.ArgumentList.Add("--target");
        install.ArgumentList.Add(targetDir);
        install.ArgumentList.Add("--upgrade");
        await RunProcessAsync(install, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunProcessAsync(ProcessStartInfo psi, CancellationToken cancellationToken)
    {
        _log($"{psi.FileName} {string.Join(" ", psi.ArgumentList)}");
        using var p = new Process { StartInfo = psi };
        if (!p.Start())
            throw new InvalidOperationException($"Failed to start: {psi.FileName}");
        p.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _log(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _log(e.Data);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{psi.FileName} exited with code {p.ExitCode}");
    }

    private async Task DownloadIfMissingAsync(string url, string dest, CancellationToken cancellationToken)
    {
        if (File.Exists(dest) && new FileInfo(dest).Length > 1024)
        {
            _log($"Already present: {dest}");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        await using var s = await http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        var tmp = dest + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await s.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(dest))
            File.Delete(dest);
        File.Move(tmp, dest);
        _log($"Downloaded: {dest}");
    }
}
