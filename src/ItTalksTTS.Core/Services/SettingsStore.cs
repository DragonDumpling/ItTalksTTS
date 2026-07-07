using System.Text.Json;
using System.Text.Json.Serialization;
using ItTalksTTS.Core.Models;

namespace ItTalksTTS.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppSettingsModel Load()
    {
        AppPaths.EnsureRoot();
        if (!File.Exists(AppPaths.SettingsPath))
        {
            var fresh = new AppSettingsModel { ApiToken = GenerateToken() };
            Save(fresh);
            return fresh;
        }

        var json = File.ReadAllText(AppPaths.SettingsPath);
        var model = JsonSerializer.Deserialize<AppSettingsModel>(json, JsonOptions);
        if (model is null)
            return new AppSettingsModel { ApiToken = GenerateToken() };
        if (string.IsNullOrWhiteSpace(model.ApiToken))
            model.ApiToken = GenerateToken();
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("autoplay", out _))
                model.Autoplay = true;
            if (!doc.RootElement.TryGetProperty("voiceVolume", out _))
                model.VoiceVolume = 3.0;
            if (!doc.RootElement.TryGetProperty("preprocessEnabled", out _))
                model.PreprocessEnabled = false;
            if (!doc.RootElement.TryGetProperty("preprocessModelId", out _))
                model.PreprocessModelId = "llama-3.2-3b-instruct";
        }

        return model;
    }

    public void Save(AppSettingsModel settings)
    {
        AppPaths.EnsureRoot();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }

    public void WriteRuntime(RuntimeInfoModel runtime)
    {
        AppPaths.EnsureRoot();
        File.WriteAllText(
            AppPaths.RuntimePath,
            JsonSerializer.Serialize(runtime, JsonOptions));
    }

    public RuntimeInfoModel? ReadRuntime()
    {
        if (!File.Exists(AppPaths.RuntimePath))
            return null;
        return JsonSerializer.Deserialize<RuntimeInfoModel>(File.ReadAllText(AppPaths.RuntimePath), JsonOptions);
    }

    private static string GenerateToken() => Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
}
