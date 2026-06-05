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
            foreach (var file in Directory.GetFiles(srcDir, "*.py"))
            {
                var dst = Path.Combine(AppPaths.WorkerDir, Path.GetFileName(file));
                if (FilesDiffer(file, dst))
                {
                    File.Copy(file, dst, true);
                    _log($"Refreshed worker script: {Path.GetFileName(file)}");
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
