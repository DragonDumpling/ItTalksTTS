using CommunityToolkit.Mvvm.ComponentModel;

namespace ItTalksTTS.App.ViewModels;

/// <summary>One word in the "Now speaking" panel. <see cref="IsCurrent"/> flips true as the
/// TTS audio reaches that word (by time-proportional distribution), for karaoke-style highlight.</summary>
public sealed partial class SpeakWord : ObservableObject
{
    [ObservableProperty] private string text;
    [ObservableProperty] private bool isCurrent;

    public SpeakWord(string text)
    {
        this.text = text;
    }
}
