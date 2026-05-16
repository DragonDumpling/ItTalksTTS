using System.Reflection;

namespace ItTalksTTS.App;

internal static class AppBuildInfo
{
    public static string ShortLabel
    {
        get
        {
            var asm = typeof(AppBuildInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                var plus = info.IndexOf('+', StringComparison.Ordinal);
                if (plus >= 0 && plus < info.Length - 1)
                    return "b" + info[(plus + 1)..];
                return info;
            }

            var v = asm.GetName().Version;
            return v is null ? "dev" : $"b{v.Build}.{v.Revision:D4}";
        }
    }
}
