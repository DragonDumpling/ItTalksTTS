using ItTalksTTS.Core;

namespace ItTalksTTS.Tts.Preprocess;

/// <summary>
/// Catalog of locally-runnable preprocessing models. Picks resolve to files under
/// <see cref="AppPaths.PreprocessModelsDir"/>. URLs point at ungated HuggingFace
/// GGUF mirrors so install works without an HF token.
/// </summary>
public static class PreprocessModelRegistry
{
    public static PreprocessModelDescriptor Llama32_3B { get; } = new()
    {
        Id = "llama-3.2-3b-instruct",
        DisplayName = "Llama 3.2 3B Instruct",
        Note = "Meta's small instruct model. Solid all-round rewriter, ~2.0 GB (Q4_K_M).",
        FileName = "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        DownloadUrl = "https://huggingface.co/unsloth/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        ApproxBytes = 2_000_000_000,
    };

    public static PreprocessModelDescriptor Qwen25_3B { get; } = new()
    {
        Id = "qwen2.5-3b-instruct",
        DisplayName = "Qwen 2.5 3B Instruct",
        Note = "Strong on technical text and code identifiers. ~2.0 GB (Q4_K_M).",
        FileName = "Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        DownloadUrl = "https://huggingface.co/bartowski/Qwen2.5-3B-Instruct-GGUF/resolve/main/Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        ApproxBytes = 2_000_000_000,
    };

    public static PreprocessModelDescriptor Phi35_mini { get; } = new()
    {
        Id = "phi-3.5-mini-instruct",
        DisplayName = "Phi 3.5 mini (3.8B)",
        Note = "Microsoft's compact instruct model. A touch larger (~2.5 GB) but crisp rewrites.",
        FileName = "Phi-3.5-mini-instruct-Q4_K_M.gguf",
        DownloadUrl = "https://huggingface.co/bartowski/Phi-3.5-mini-instruct-GGUF/resolve/main/Phi-3.5-mini-instruct-Q4_K_M.gguf",
        ApproxBytes = 2_500_000_000,
    };

    public static IReadOnlyList<PreprocessModelDescriptor> All { get; } = new[]
    {
        Llama32_3B,
        Qwen25_3B,
        Phi35_mini,
    };

    public static PreprocessModelDescriptor Default => Llama32_3B;

    public static PreprocessModelDescriptor FromId(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Default;

    /// <summary>True when the model file exists and is non-trivially sized.</summary>
    public static bool IsInstalled(PreprocessModelDescriptor model)
    {
        var path = AppPaths.PreprocessModelFile(model.FileName);
        return File.Exists(path) && new FileInfo(path).Length > 1024 * 1024;
    }
}
