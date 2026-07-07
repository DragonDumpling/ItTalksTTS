using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ItTalksTTS.Core.Models;

public partial class AppSettingsModel : ObservableObject
{
    [ObservableProperty] private string apiToken = "";

    [ObservableProperty] private string selectedModel = "KokoroOnnx";

    [ObservableProperty] private string selectedVoice = "af_sarah";

    // F5-TTS voice cloning: reference clip + its transcript. Empty = use the bundled default.
    [ObservableProperty] private string f5RefAudioPath = "";

    [ObservableProperty] private string f5RefText = "";

    [ObservableProperty] private bool autoplay = true;

    /// <summary>Playback gain multiplier (1.0 = source level; default 3.0 compensates for quiet synthesis).</summary>
    [ObservableProperty] private double voiceVolume = 3.0;

    [ObservableProperty] private ObservableCollection<FilterRuleModel> filterRules = new();

    [ObservableProperty] private int serviceLogMaxLines = 500;

    // --- Optional speech-friendly preprocessing via a small local LLM ---
    //
    // When enabled, text is rewritten by a local ~3B model right before synthesis so
    // it sounds natural spoken aloud: markdown/code/emoji stripped, numbers/dates/
    // abbreviations expanded to spoken form, URLs/API keys/hashes replaced with short
    // descriptions, and verbose text tightened. The original queue text is untouched.
    [ObservableProperty] private bool preprocessEnabled;

    /// <summary>Stable id of the local model used for preprocessing (see PreprocessModelRegistry).</summary>
    [ObservableProperty] private string preprocessModelId = "llama-3.2-3b-instruct";
}
