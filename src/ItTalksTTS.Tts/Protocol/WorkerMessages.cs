using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItTalksTTS.Tts.Protocol;

public sealed class WorkerRequest
{
    /// <summary>Monotonic request id; the worker echoes it so responses can be matched (drops stale ones).</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("cmd")]
    public string Cmd { get; init; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("voice")]
    public string? Voice { get; init; }

    [JsonPropertyName("lang")]
    public string? Lang { get; init; }

    [JsonPropertyName("speed")]
    public double? Speed { get; init; }

    // --- Engine-specific voice inputs (all optional; ignored by engines that don't use them) ---

    /// <summary>F5-TTS: path to the reference audio clip to clone.</summary>
    [JsonPropertyName("refAudio")]
    public string? RefAudio { get; init; }

    /// <summary>F5-TTS: transcript of the reference clip.</summary>
    [JsonPropertyName("refText")]
    public string? RefText { get; init; }

    /// <summary>Parler-TTS: natural-language description of the desired voice.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class WorkerResponse
{
    /// <summary>Echo of the request id (null from legacy workers).</summary>
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("wav")]
    public string? Wav { get; init; }

    [JsonPropertyName("voices")]
    public List<string>? Voices { get; init; }

    /// <summary>Optional: voice-selection mode the worker expects ("namedVoices" | "referenceAudio" | "description").</summary>
    [JsonPropertyName("voiceMode")]
    public string? VoiceMode { get; init; }
}

public static class WorkerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
