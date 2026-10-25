using ItTalksTTS.Core.Models;

namespace ItTalksTTS.Tts;

/// <summary>
/// Remembers each engine's voice across model switches. Kokoro and Phonon both
/// use named voices, but the names don't overlap (af_sarah vs Easton).
/// </summary>
public static class EngineVoiceSelection
{
    public static void ApplyEngineSwitch(AppSettingsModel settings, string? previousKey, string? nextKey)
    {
        settings.EngineVoices ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var next = EngineRegistry.FromKey(nextKey);
        if (string.Equals(previousKey, next.SettingsKey, StringComparison.OrdinalIgnoreCase))
            return;

        if (EngineRegistry.TryGet(previousKey, out var previous))
            settings.EngineVoices[previous.SettingsKey] = settings.SelectedVoice ?? "";

        if (TryGetSaved(settings, next.SettingsKey, out var remembered))
        {
            settings.SelectedVoice = remembered;
            return;
        }

        if (next.VoiceMode == VoiceInputMode.NamedVoices && !string.IsNullOrWhiteSpace(next.DefaultVoice))
            settings.SelectedVoice = next.DefaultVoice;
    }

    private static bool TryGetSaved(AppSettingsModel settings, string settingsKey, out string voice)
    {
        foreach (var pair in settings.EngineVoices)
        {
            if (string.Equals(pair.Key, settingsKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                voice = pair.Value;
                return true;
            }
        }

        voice = "";
        return false;
    }
}
