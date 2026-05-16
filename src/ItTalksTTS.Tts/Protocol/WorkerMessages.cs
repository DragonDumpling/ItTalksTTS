using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItTalksTTS.Tts.Protocol;

public sealed class WorkerRequest
{
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
}

public sealed class WorkerResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("wav")]
    public string? Wav { get; init; }

    [JsonPropertyName("voices")]
    public List<string>? Voices { get; init; }
}

public static class WorkerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
