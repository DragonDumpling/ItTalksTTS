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

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
