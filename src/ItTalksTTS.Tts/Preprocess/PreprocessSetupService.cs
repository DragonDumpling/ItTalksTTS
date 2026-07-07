using System.Diagnostics;
using System.Text.RegularExpressions;
using ItTalksTTS.Core;

namespace ItTalksTTS.Tts.Preprocess;

/// <summary>
/// Installs the optional preprocessing LLM: creates a dedicated venv, pip-installs
/// llama-cpp-python (CPU wheels ship prebuilt on Windows), and downloads the
/// selected GGUF model. Mirrors <see cref="KokoroSetupService"/> but targets
/// <see cref="AppPaths.PreprocessDir"/> so it stays independent of the TTS engines.
/// </summary>
public sealed class PreprocessSetupService
{
    private readonly Action<string> _log;
    public PreprocessSetupService(Action<string> log) => _log = log;

    public bool IsInstalled(PreprocessModelDescriptor? model = null)
    {
        model ??= PreprocessModelRegistry.Default;
        if (!File.Exists(AppPaths.VenvPython(AppPaths.PreprocessVenv)))
            return false;
        return PreprocessModelRegistry.IsInstalled(model);
    }

    public async Task InstallAsync(
        PreprocessModelDescriptor model,
        IProgress<(string step, double? fraction)>? progress,
        CancellationToken cancellationToken)
    {
        AppPaths.EnsureRoot();
        Directory.CreateDirectory(AppPaths.PreprocessDir);
        Directory.CreateDirectory(AppPaths.PreprocessModelsDir);

        progress?.Report(("Preparing Python environment…", 0.05));
        var venvPython = await EnsureVenvAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(("Installing llama-cpp-python…", 0.2));
        await RunPythonAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip", "wheel" }, null, cancellationToken).ConfigureAwait(false);
        // llama-cpp-python's prebuilt Windows wheels live on a separate index, not PyPI.
        // Force binary-only so pip never tries to build from source (which needs CMake +
        // Visual Studio C/C++ compilers that most machines don't have).
        await RunPythonAsync(
            venvPython,
            new[]
            {
                "-m", "pip", "install",
                "--only-binary", ":all:",
                "--extra-index-url", "https://abetlen.github.io/llama-cpp-python/whl/cpu/",
                "llama-cpp-python>=0.3.2",
            },
            null,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(($"Downloading {model.DisplayName} ({FormatBytes(model.ApproxBytes)})…", 0.45));
        var dest = AppPaths.PreprocessModelFile(model.FileName);
        await DownloadIfMissingAsync(model.DownloadUrl, dest, cancellationToken).ConfigureAwait(false);

        File.WriteAllText(AppPaths.PreprocessReadyMarker, DateTime.UtcNow.ToString("O"));
        progress?.Report(("Done.", 1.0));
    }

    /// <summary>Python interpreter to launch the worker with — prefers the preprocess venv.</summary>
    public string ResolvePythonExe()
    {
        var venv = AppPaths.VenvPython(AppPaths.PreprocessVenv);
        return File.Exists(venv) ? venv : venv; // callers should check File.Exists
    }

    /// <summary>Path to the deployed prep_worker.py (deployed alongside the TTS workers).</summary>
    public static string ResolveWorkerScript()
    {
        var deployed = Path.Combine(AppPaths.WorkerDir, "prep_worker.py");
        if (File.Exists(deployed))
            return deployed;
        var bundled = Path.Combine(AppContext.BaseDirectory, "kokoro_worker", "prep_worker.py");
        if (File.Exists(bundled))
            return bundled;
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "kokoro_worker", "prep_worker.py"));
        return File.Exists(repo) ? repo : deployed;
    }

    private async Task<string> EnsureVenvAsync(CancellationToken cancellationToken)
    {
        var venvPython = AppPaths.VenvPython(AppPaths.PreprocessVenv);
        if (File.Exists(venvPython))
            return venvPython;

        var basePython = FindSystemPython()
            ?? throw new InvalidOperationException(
                "The preprocessing model needs a system Python 3.10–3.12. Install it from https://python.org "
                + "(tick \"Add python.exe to PATH\"), then run Setup again.");

        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.PreprocessVenv)!);
        var create = NewProcess(basePython.Exe);
        foreach (var a in basePython.Prefix)
            create.ArgumentList.Add(a);
        create.ArgumentList.Add("-m");
        create.ArgumentList.Add("venv");
        create.ArgumentList.Add(AppPaths.PreprocessVenv);
        await RunProcessAsync(create, cancellationToken).ConfigureAwait(false);

        if (!File.Exists(venvPython))
            throw new InvalidOperationException($"venv creation did not produce {venvPython}");
        return venvPython;
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
                var m = Regex.Match(text, @"Python 3\.(\d+)");
                if (p.ExitCode == 0 && m.Success && int.TryParse(m.Groups[1].Value, out var minor)
                    && minor is >= 10 and <= 13)
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

    private async Task RunProcessAsync(ProcessStartInfo psi, CancellationToken cancellationToken)
    {
        _log($"{psi.FileName} {string.Join(" ", psi.ArgumentList)}");
        using var p = new Process { StartInfo = psi };
        if (!p.Start())
            throw new InvalidOperationException($"Failed to start: {psi.FileName}");
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _log(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{psi.FileName} exited with code {p.ExitCode}");
    }

    private async Task DownloadIfMissingAsync(string url, string dest, CancellationToken cancellationToken)
    {
        if (File.Exists(dest) && new FileInfo(dest).Length > 1024 * 1024)
        {
            _log($"Already present: {dest}");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ItTalksTTS-prep-installer");
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

    private static string FormatBytes(long bytes) => bytes >= 1_000_000_000
        ? $"{bytes / 1_000_000_000.0:0.#} GB"
        : $"{bytes / 1_000_000.0:0.#} MB";
}
