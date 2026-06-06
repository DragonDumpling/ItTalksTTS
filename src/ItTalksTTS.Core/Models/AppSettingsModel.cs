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

    [ObservableProperty] private ObservableCollection<FilterRuleModel> filterRules = new();

    [ObservableProperty] private int serviceLogMaxLines = 500;
}
