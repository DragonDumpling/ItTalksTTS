using System.Text.Json;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Tts;

namespace ItTalksTTS.Tests;

public class SettingsSerializationTests
{
    [Fact]
    public void AppSettings_roundtrips_filter_rules()
    {
        var m = new AppSettingsModel();
        m.FilterRules.Add(new FilterRuleModel { Match = "`", Replacement = "" });
        var json = JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var back = JsonSerializer.Deserialize<AppSettingsModel>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(back);
        Assert.Single(back.FilterRules);
        Assert.Equal("`", back.FilterRules[0].Match);
        Assert.True(back.Autoplay);
    }

    [Fact]
    public void AppSettings_roundtrips_engine_and_f5_fields()
    {
        var m = new AppSettingsModel
        {
            SelectedModel = "F5TTS",
            F5RefAudioPath = @"C:\clips\me.wav",
            F5RefText = "This is my voice."
        };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var back = JsonSerializer.Deserialize<AppSettingsModel>(JsonSerializer.Serialize(m, opts), opts);
        Assert.NotNull(back);
        Assert.Equal("F5TTS", back.SelectedModel);
        Assert.Equal(@"C:\clips\me.wav", back.F5RefAudioPath);
        Assert.Equal("This is my voice.", back.F5RefText);
    }

    [Fact]
    public void Switching_engines_remembers_each_named_voice()
    {
        var settings = new AppSettingsModel
        {
            SelectedModel = "KokoroOnnx",
            SelectedVoice = "af_bella"
        };

        EngineVoiceSelection.ApplyEngineSwitch(settings, "KokoroOnnx", "Phonon31m");
        Assert.Equal("Easton", settings.SelectedVoice);
        Assert.Equal("af_bella", settings.EngineVoices["KokoroOnnx"]);

        settings.SelectedVoice = "Zoey";
        EngineVoiceSelection.ApplyEngineSwitch(settings, "Phonon31m", "KokoroOnnx");
        Assert.Equal("af_bella", settings.SelectedVoice);
        Assert.Equal("Zoey", settings.EngineVoices["Phonon31m"]);
    }

    [Fact]
    public void Same_engine_keeps_the_current_voice()
    {
        var settings = new AppSettingsModel { SelectedVoice = "af_sky" };
        EngineVoiceSelection.ApplyEngineSwitch(settings, "KokoroOnnx", "KokoroOnnx");
        Assert.Equal("af_sky", settings.SelectedVoice);
        Assert.Empty(settings.EngineVoices);
    }

    [Fact]
    public void Round_trip_through_f5_restores_the_named_voice()
    {
        var settings = new AppSettingsModel { SelectedVoice = "Easton" };
        settings.EngineVoices["KokoroOnnx"] = "af_sarah";

        EngineVoiceSelection.ApplyEngineSwitch(settings, "Phonon31m", "F5TTS");
        Assert.Equal("Easton", settings.SelectedVoice);

        EngineVoiceSelection.ApplyEngineSwitch(settings, "F5TTS", "KokoroOnnx");
        Assert.Equal("af_sarah", settings.SelectedVoice);
    }

    [Fact]
    public void AppSettings_roundtrips_engine_voices()
    {
        var m = new AppSettingsModel { SelectedModel = "Phonon31m", SelectedVoice = "Zoey" };
        m.EngineVoices["KokoroOnnx"] = "af_sarah";
        m.EngineVoices["Phonon31m"] = "Zoey";
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var back = JsonSerializer.Deserialize<AppSettingsModel>(JsonSerializer.Serialize(m, opts), opts);
        Assert.NotNull(back);
        Assert.Equal("Phonon31m", back.SelectedModel);
        Assert.Equal("Zoey", back.SelectedVoice);
        Assert.Equal("af_sarah", back.EngineVoices["KokoroOnnx"]);
        Assert.Equal("Zoey", back.EngineVoices["Phonon31m"]);
    }

    [Fact]
    public void AppSettings_roundtrips_voice_volume()
    {
        var m = new AppSettingsModel { VoiceVolume = 3.0 };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var back = JsonSerializer.Deserialize<AppSettingsModel>(JsonSerializer.Serialize(m, opts), opts);
        Assert.NotNull(back);
        Assert.Equal(3.0, back.VoiceVolume);
    }
}
