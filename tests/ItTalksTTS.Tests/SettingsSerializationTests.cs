using System.Text.Json;
using ItTalksTTS.Core.Models;

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
    public void AppSettings_roundtrips_voice_volume()
    {
        var m = new AppSettingsModel { VoiceVolume = 3.0 };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var back = JsonSerializer.Deserialize<AppSettingsModel>(JsonSerializer.Serialize(m, opts), opts);
        Assert.NotNull(back);
        Assert.Equal(3.0, back.VoiceVolume);
    }
}
