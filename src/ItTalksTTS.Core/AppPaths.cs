namespace ItTalksTTS.Core;

public static class AppPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ItTalksTTS");

    public static string PythonVenv => Path.Combine(Root, "python", "venv");

    public static string PythonPackages => Path.Combine(Root, "python", "packages");

    /// <summary>Shipped with the installer under the app folder (embeddable CPython).</summary>
    public static string BundledPythonExe =>
        Path.Combine(AppContext.BaseDirectory, "python-embed", "python.exe");

    public static string ModelsDir => Path.Combine(Root, "models");

    public static string KokoroOnnx => Path.Combine(ModelsDir, "kokoro-v1.0.onnx");

    public static string KokoroVoices => Path.Combine(ModelsDir, "voices-v1.0.bin");

    public static string SettingsPath => Path.Combine(Root, "settings.json");

    public static string QueuePersistencePath => Path.Combine(Root, "queue.json");

    public static string RuntimePath => Path.Combine(Root, "runtime.json");

    public static string WorkerDir => Path.Combine(Root, "kokoro_worker");

    public static string UpdatesDir => Path.Combine(Root, "updates");

    public static string LogsDir => Path.Combine(Root, "logs");

    public static string LogFilePath => Path.Combine(LogsDir, "app.log");

    /// <summary>Marker written into a packages dir once pip install completes (embedded-Python path).</summary>
    public const string PackagesReadyMarker = ".ittalks-ready";

    // --- Per-engine layout (engines other than the default Kokoro env) ---

    public static string EnginesDir => Path.Combine(Root, "engines");

    public static string EngineRoot(string folder) => Path.Combine(EnginesDir, folder);

    public static string EngineVenv(string folder) => Path.Combine(EngineRoot(folder), "venv");

    public static string EnginePackages(string folder) => Path.Combine(EngineRoot(folder), "packages");

    public static string EngineModels(string folder) => Path.Combine(EngineRoot(folder), "models");

    public static string EngineReadyMarker(string folder) => Path.Combine(EngineRoot(folder), ".ready");

    /// <summary>The python.exe inside a Windows venv directory.</summary>
    public static string VenvPython(string venvDir) => Path.Combine(venvDir, "Scripts", "python.exe");

    // --- Optional preprocessing LLM (separate from the TTS engines) ---

    public static string PreprocessDir => Path.Combine(Root, "preprocess");

    public static string PreprocessVenv => Path.Combine(PreprocessDir, "venv");

    public static string PreprocessPackages => Path.Combine(PreprocessDir, "packages");

    public static string PreprocessModelsDir => Path.Combine(PreprocessDir, "models");

    /// <summary>Marker written once the preprocessing venv + model are ready.</summary>
    public static string PreprocessReadyMarker => Path.Combine(PreprocessDir, ".ready");

    /// <summary>Resolved per-model GGUF path under <see cref="PreprocessModelsDir"/>.</summary>
    public static string PreprocessModelFile(string fileName) => Path.Combine(PreprocessModelsDir, fileName);

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
