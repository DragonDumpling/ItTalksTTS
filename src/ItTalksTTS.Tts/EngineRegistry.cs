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
    public const string PhononKey = "Phonon31m";
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
        SetupSummary = "Installs Kokoro's Python packages and downloads the ONNX model and voices. Internet required. The Windows installer includes Python; a dev build needs Python 3.10+ on PATH.",
        WorkerScript = "worker.py",
        RequirementsFile = "requirements.txt",
        NeedsTorch = false,
        VoiceMode = VoiceInputMode.NamedVoices,
        DefaultVoice = "af_sarah",
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

    public static EngineDescriptor Phonon { get; } = new()
    {
        Id = TtsEngineId.Phonon31m,
        SettingsKey = PhononKey,
        DisplayName = "Phonon 31m",
        SetupSummary = "Installs Phonon 31m: the ptts runtime (no PyTorch) and the English q8 weights, about 65 MB. Internet required. Uses the installer's Python when it is present; otherwise Python 3.10–3.12 on PATH.",
        WorkerScript = "phonon_worker.py",
        RequirementsFile = "phonon_requirements.txt",
        NeedsTorch = false,
        VoiceMode = VoiceInputMode.NamedVoices,
        DefaultVoice = "Easton",
        VenvDir = AppPaths.EngineVenv("phonon"),
        PackagesDir = AppPaths.EnginePackages("phonon"),
        ModelsDir = AppPaths.EngineModels("phonon"),
        ReadyMarkerPath = AppPaths.EngineReadyMarker("phonon"),
        EnvVars = new Dictionary<string, string>
        {
            ["HF_HOME"] = AppPaths.EngineModels("phonon"),
            ["HF_HUB_DISABLE_TELEMETRY"] = "1",
            ["PHONON_LANG"] = "en",
            ["PHONON_QUANT"] = "q8",
            ["PHONON_DEVICE"] = "cpu",
            ["PHONON_VOICE"] = "Easton",
        },
    };

    public static EngineDescriptor F5 { get; } = new()
    {
        Id = TtsEngineId.F5Tts,
        SettingsKey = F5Key,
        DisplayName = "F5-TTS (voice cloning)",
        SetupSummary = "Installs F5-TTS with PyTorch (CUDA when an NVIDIA GPU is present, otherwise CPU) and downloads the cloning model, about 1.3 GB. Needs Python 3.10–3.12 on PATH.",
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

    public static IReadOnlyList<EngineDescriptor> All { get; } = new[] { Kokoro, Phonon, F5 };

    public static EngineDescriptor Default => Kokoro;

    public static bool TryGet(string? settingsKey, out EngineDescriptor engine)
    {
        var found = All.FirstOrDefault(e => string.Equals(e.SettingsKey, settingsKey, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            engine = Default;
            return false;
        }

        engine = found;
        return true;
    }

    public static EngineDescriptor FromKey(string? settingsKey) =>
        TryGet(settingsKey, out var engine) ? engine : Default;

    /// <summary>An engine is usable when it has a venv interpreter or a completed --target install.</summary>
    public static bool IsInstalled(EngineDescriptor engine) =>
        File.Exists(AppPaths.VenvPython(engine.VenvDir)) || File.Exists(engine.ReadyMarkerPath);

    /// <summary>
    /// True when setup has finished for this engine. Kokoro also needs its ONNX files;
    /// other engines are ready once <see cref="IsInstalled"/> is true.
    /// </summary>
    public static bool IsUsable(EngineDescriptor engine)
    {
        if (engine.Id == TtsEngineId.KokoroOnnx)
        {
            var hasModel = File.Exists(AppPaths.KokoroOnnx) && File.Exists(AppPaths.KokoroVoices);
            return hasModel && IsInstalled(engine);
        }

        return IsInstalled(engine);
    }

    public static string SetupButtonText(bool alreadyInstalled) =>
        alreadyInstalled ? "Run setup again" : "Run setup";
}
