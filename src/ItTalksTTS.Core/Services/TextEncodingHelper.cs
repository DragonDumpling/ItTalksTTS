using System.Text;

namespace ItTalksTTS.Core.Services;

public static class TextEncodingHelper
{
    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly Encoding Windows1252;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    static TextEncodingHelper()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Cursor on Windows often pipes hook stdin as UTF-16 LE; reading as UTF-8 yields U+FFFD for punctuation.
    /// </summary>
    public static string DecodeHookStdin(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return "";

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes);
            if (bytes[0] == (byte)'{' && bytes[1] == 0)
                return Encoding.Unicode.GetString(bytes);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return StrictUtf8.GetString(bytes[3..]);

        return StrictUtf8.GetString(bytes);
    }

    /// <summary>Repair mojibake, then normalize smart punctuation for display and TTS.</summary>
    public static string PrepareForQueue(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        text = RepairUtf8Mojibake(text);
        return NormalizeTypography(text);
    }

    /// <summary>
    /// Reverses UTF-8 text that was mis-decoded as Latin-1 / Windows-1252 (e.g. café as cafÃ©, — as â€").
    /// </summary>
    public static string RepairUtf8Mojibake(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        if (!LooksLikeUtf8Mojibake(text))
            return text;

        var encodings = text.Contains('€', StringComparison.Ordinal) || text.Contains("â€", StringComparison.Ordinal)
            ? new[] { Windows1252, Latin1 }
            : new[] { Latin1, Windows1252 };

        var bestScore = MojibakeScore(text);
        string? best = null;

        foreach (var enc in encodings)
        {
            try
            {
                var repaired = Encoding.UTF8.GetString(enc.GetBytes(text));
                if (string.IsNullOrEmpty(repaired))
                    continue;
                var score = MojibakeScore(repaired);
                if (score < bestScore)
                {
                    best = repaired;
                    bestScore = score;
                }
            }
            catch
            {
                /* ignore */
            }
        }

        if (best is not null && MojibakeScore(best) > 0)
        {
            var again = RepairUtf8Mojibake(best);
            if (MojibakeScore(again) < MojibakeScore(best))
                best = again;
        }

        return best ?? text;
    }

    public static string NormalizeTypography(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            switch (c)
            {
                case '\uFFFD':
                    continue;
                case '\u2018' or '\u2019' or '\u201B':
                    sb.Append('\'');
                    break;
                case '\u201C' or '\u201D':
                    sb.Append('"');
                    break;
                case '\u2013' or '\u2014':
                    sb.Append('-');
                    break;
                case '\u2026':
                    sb.Append("...");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool LooksLikeUtf8Mojibake(string text) =>
        text.Contains("Ã", StringComparison.Ordinal) ||
        text.Contains("â€", StringComparison.Ordinal) ||
        text.Contains("Â", StringComparison.Ordinal);

    private static int MojibakeScore(string text)
    {
        var score = 0;
        foreach (var c in text)
        {
            if (c is 'Ã' or 'â' or 'Â' or '¢' or '\uFFFD')
                score += 2;
        }

        return score;
    }
}
