using ItTalksTTS.Core;

namespace ItTalksTTS.Tts;

public enum TtsEngineId
{
    KokoroOnnx,
    F5Tts,
}

/// <summary>How a given engine selects its voice — drives which UI panel is shown.</summary>
public enum VoiceInputMode
{
    /// <summary>A fixed list of named voices (Kokoro).</summary>
    NamedVoices,

    /// <summary>Zero-shot cloning from a reference audio clip + its transcript (F5-TTS).</summary>
    ReferenceAudio,

    /// <summary>A natural-language description of the desired voice (Parler-TTS).</summary>
    Description,
}

/// <summary>
/// Everything the supervisor, setup service, and UI need to drive one TTS engine.
/// Built by <see cref="EngineRegistry"/> so paths resolve from <see cref="AppPaths"/>.
/// </summary>
public sealed record EngineDescriptor
{
    public required TtsEngineId Id { get; init; }

    /// <summary>Stable string stored in Settings.SelectedModel.</summary>
    public required string SettingsKey { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Worker script filename deployed under <see cref="AppPaths.WorkerDir"/>.</summary>
    public required string WorkerScript { get; init; }

    /// <summary>Requirements file (in the worker dir) installed during setup.</summary>
    public required string RequirementsFile { get; init; }

    /// <summary>True for PyTorch engines — setup installs CUDA/CPU torch wheels first.</summary>
    public required bool NeedsTorch { get; init; }

    public required VoiceInputMode VoiceMode { get; init; }

    /// <summary>Dedicated virtual-env dir (preferred interpreter when present).</summary>
    public required string VenvDir { get; init; }

    /// <summary>pip --target dir, used with the embedded Python when no venv exists.</summary>
    public required string PackagesDir { get; init; }

    /// <summary>Model/cache dir (e.g. HF_HOME for torch engines).</summary>
    public required string ModelsDir { get; init; }

    /// <summary>Marker file written once the engine is fully installed.</summary>
    public required string ReadyMarkerPath { get; init; }

    /// <summary>Extra environment variables passed to the worker process.</summary>
    public required IReadOnlyDictionary<string, string> EnvVars { get; init; }
}
