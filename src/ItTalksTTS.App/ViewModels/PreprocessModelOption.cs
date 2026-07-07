using ItTalksTTS.Tts.Preprocess;

namespace ItTalksTTS.App.ViewModels;

/// <summary>UI-facing wrapper for a <see cref="PreprocessModelDescriptor"/> so the dropdown can bind Id vs. display text.</summary>
public sealed record PreprocessModelOption
{
    public required string Id { get; init; }
    public required string Display { get; init; }
    public required string Note { get; init; }

    public static PreprocessModelOption From(PreprocessModelDescriptor m) =>
        new() { Id = m.Id, Display = m.DisplayName, Note = m.Note };
}
