using System.Text.Json;
using System.Text.Json.Nodes;

namespace ItTalksTTS.Core.Services;

/// <summary>
/// Installs ItTalksTTS as a <b>user-level</b> Cursor hook (~/.cursor/hooks.json) so any workspace gets enqueue without cloning this repo.
/// </summary>
public static class CursorHookInstaller
{
    public const string HookExeName = "ItTalksHookEnqueue.exe";
    public const string HookCommand = "./hooks/ItTalksHookEnqueue.exe";
    private const string HookEvent = "afterAgentResponse";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string CursorDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor");

    public static string CursorHooksDirectory => Path.Combine(CursorDirectory, "hooks");

    public static string CursorHooksJsonPath => Path.Combine(CursorDirectory, "hooks.json");

    public static string InstalledHookExePath => Path.Combine(CursorHooksDirectory, HookExeName);

    public static string BundledHookExePath =>
        Path.Combine(AppContext.BaseDirectory, HookExeName);

    public static bool IsConfigured()
    {
        if (!File.Exists(CursorHooksJsonPath) || !File.Exists(InstalledHookExePath))
            return false;
        try
        {
            var json = File.ReadAllText(CursorHooksJsonPath);
            return json.Contains(HookExeName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static (bool Ok, string Message) Install()
    {
        try
        {
            if (!File.Exists(BundledHookExePath))
                return (false, $"Missing {HookExeName} next to the app. Reinstall ItTalksTTS.");

            Directory.CreateDirectory(CursorHooksDirectory);
            File.Copy(BundledHookExePath, InstalledHookExePath, overwrite: true);
            WriteHooksJson(mergeExisting: true);
            return (true, "Cursor hooks installed for all projects. Restart Cursor if it was already open.");
        }
        catch (Exception ex)
        {
            return (false, "Cursor hook install failed: " + ex.Message);
        }
    }

    public static (bool Ok, string Message) Uninstall()
    {
        try
        {
            if (File.Exists(CursorHooksJsonPath))
            {
                var root = JsonNode.Parse(File.ReadAllText(CursorHooksJsonPath)) as JsonObject;
                if (root?["hooks"] is JsonObject hooks && hooks[HookEvent] is JsonArray arr)
                {
                    var kept = new JsonArray();
                    foreach (var item in arr)
                    {
                        if (item is null)
                            continue;
                        var cmd = item["command"]?.GetValue<string>() ?? "";
                        if (cmd.Contains(HookExeName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        kept.Add(item.DeepClone());
                    }

                    if (kept.Count == 0)
                        hooks.Remove(HookEvent);
                    else
                        hooks[HookEvent] = kept;
                }

                File.WriteAllText(CursorHooksJsonPath, root?.ToJsonString(JsonOptions) ?? "{}");
            }

            if (File.Exists(InstalledHookExePath))
                File.Delete(InstalledHookExePath);

            return (true, "Cursor hooks removed.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static void WriteHooksJson(bool mergeExisting)
    {
        JsonObject root;
        if (mergeExisting && File.Exists(CursorHooksJsonPath))
        {
            root = JsonNode.Parse(File.ReadAllText(CursorHooksJsonPath)) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["version"] = 1;
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        var entry = new JsonObject
        {
            ["command"] = HookCommand,
            ["timeout"] = 25
        };

        JsonArray list;
        if (hooks[HookEvent] is JsonArray existing)
        {
            list = new JsonArray();
            foreach (var item in existing)
            {
                if (item is null)
                    continue;
                var cmd = item["command"]?.GetValue<string>() ?? "";
                if (cmd.Contains(HookExeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(item.DeepClone());
            }
        }
        else
        {
            list = new JsonArray();
        }

        list.Add(entry);
        hooks[HookEvent] = list;

        Directory.CreateDirectory(CursorDirectory);
        File.WriteAllText(CursorHooksJsonPath, root.ToJsonString(JsonOptions));
    }
}
