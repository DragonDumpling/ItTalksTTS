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
}
