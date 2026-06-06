using System.IO;
using ItTalksTTS.Core;

namespace ItTalksTTS.Tts;

/// <summary>
/// The set of available TTS engines and the single place their descriptors
/// (and per-engine paths) are defined. Used by the supervisor, setup service,
/// and the UI so they all agree on engine identity, paths, and voice mode.
/// </summary>
public static class EngineRegistry
{
    public const string KokoroKey = "KokoroOnnx";
    public const string F5Key = "F5TTS";

    /// <summary>Bundled F5 reference clip (ships in the worker dir) used when the user hasn't picked one.</summary>
    public const string F5DefaultRefFileName = "f5_ref_default.wav";

    /// <summary>Transcript of <see cref="F5DefaultRefFileName"/> — must match the clip's spoken words.</summary>
    public const string F5DefaultRefText =
        "The quick brown fox jumps over the lazy dog while the morning sun rises gently over the quiet hills.";

    public static string F5DefaultRefAudioPath => Path.Combine(AppPaths.WorkerDir, F5DefaultRefFileName);

    public static EngineDescriptor Kokoro { get; } = new()
    {
        Id = TtsEngineId.KokoroOnnx,
        SettingsKey = KokoroKey,
        DisplayName = "Kokoro (ONNX)",
        WorkerScript = "worker.py",
        RequirementsFile = "requirements.txt",
        NeedsTorch = false,
        VoiceMode = VoiceInputMode.NamedVoices,
        // Kokoro keeps its original env locations for back-compat.
        VenvDir = AppPaths.PythonVenv,
        PackagesDir = AppPaths.PythonPackages,
        ModelsDir = AppPaths.ModelsDir,
        ReadyMarkerPath = Path.Combine(AppPaths.PythonPackages, AppPaths.PackagesReadyMarker),
        EnvVars = new Dictionary<string, string>
        {
            ["KOKORO_MODEL"] = AppPaths.KokoroOnnx,
            ["KOKORO_VOICES"] = AppPaths.KokoroVoices,
        },
    };

    public static EngineDescriptor F5 { get; } = new()
    {
        Id = TtsEngineId.F5Tts,
        SettingsKey = F5Key,
        DisplayName = "F5-TTS (voice cloning)",
        WorkerScript = "f5_worker.py",
        RequirementsFile = "f5_requirements.txt",
        NeedsTorch = true,
        VoiceMode = VoiceInputMode.ReferenceAudio,
        VenvDir = AppPaths.EngineVenv("f5"),
        PackagesDir = AppPaths.EnginePackages("f5"),
        ModelsDir = AppPaths.EngineModels("f5"),
        ReadyMarkerPath = AppPaths.EngineReadyMarker("f5"),
        EnvVars = new Dictionary<string, string>
        {
            ["HF_HOME"] = AppPaths.EngineModels("f5"),
            ["HF_HUB_DISABLE_TELEMETRY"] = "1",
        },
    };

    public static IReadOnlyList<EngineDescriptor> All { get; } = new[] { Kokoro, F5 };

    public static EngineDescriptor Default => Kokoro;

    public static EngineDescriptor FromKey(string? settingsKey) =>
        All.FirstOrDefault(e => string.Equals(e.SettingsKey, settingsKey, StringComparison.OrdinalIgnoreCase))
        ?? Default;

    /// <summary>An engine is usable when it has a venv interpreter or a completed --target install.</summary>
    public static bool IsInstalled(EngineDescriptor engine) =>
        File.Exists(AppPaths.VenvPython(engine.VenvDir)) || File.Exists(engine.ReadyMarkerPath);
}
