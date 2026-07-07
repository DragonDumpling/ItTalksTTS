namespace ItTalksTTS.Tts.Preprocess;

/// <summary>
/// One locally-runnable GGUF model used to rewrite text into a speech-friendly form
/// before it reaches the TTS engine. All entries target the ~3B class so they run
/// comfortably on CPU via llama-cpp-python while still being useful for rewriting.
/// </summary>
public sealed record PreprocessModelDescriptor
{
    /// <summary>Stable id persisted in <c>Settings.PreprocessModelId</c>.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Short human-readable note shown in the model dropdown tooltip.</summary>
    public required string Note { get; init; }

    /// <summary>GGUF filename stored under <c>AppPaths.PreprocessModelsDir</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>Direct download URL (ungated HuggingFace resolve link preferred).</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Approximate download size in bytes (for progress + free-space messaging).</summary>
    public required long ApproxBytes { get; init; }

    /// <summary>llama-cpp-python chat-format hint. <c>null</c> = let llama-cpp auto-detect.</summary>
    public string? ChatFormat { get; init; }

    /// <summary>Context window to allocate for the model.</summary>
    public int ContextSize { get; init; } = 4096;
}
