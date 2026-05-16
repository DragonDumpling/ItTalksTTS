using System.Text;
using System.Text.Json;
using ItTalksTTS.Core.Models;

namespace ItTalksTTS.Core.Services;

public static class QueuePersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(IEnumerable<QueueItemModel> items)
    {
        AppPaths.EnsureRoot();
        var list = items.ToList();
        File.WriteAllText(
            AppPaths.QueuePersistencePath,
            JsonSerializer.Serialize(list, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static List<QueueItemModel> Load()
    {
        if (!File.Exists(AppPaths.QueuePersistencePath))
            return [];
        try
        {
            var json = File.ReadAllText(AppPaths.QueuePersistencePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<QueueItemModel>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
