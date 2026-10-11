using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ItTalksTTS.Core.Services;

/// <summary>A cached clip ready to play again without preprocessing or synthesis.</summary>
public sealed record PlaybackCacheHit(string WavPath, string SpokenText, List<int>? Weights);

/// <summary>
/// Keeps the newest synthesized wavs on disk (one slot per queue item). Replaying a slot
/// refreshes it so a later store evicts some other clip. A fingerprint miss leaves the
/// files in place; the next successful synthesis for that id overwrites them.
/// </summary>
public sealed class PlaybackWavCache
{
    public const int Capacity = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    public PlaybackWavCache(string directory, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_directory);
        lock (_gate)
            TrimAndDropOrphans();
    }

    /// <summary>
    /// Settings that change the audio, plus the original queue text. Volume is omitted —
    /// it is applied when the wav is played, not baked into the file.
    /// </summary>
    public static string BuildFingerprint(
        string queueText,
        string engineKey,
        string voice,
        string referenceAudioPath,
        string referenceTranscript,
        bool preprocessEnabled,
        string preprocessModelId)
    {
        return string.Join(
            '\u001e',
            Field(queueText),
            Field(engineKey),
            Field(voice),
            Field(referenceAudioPath),
            Field(referenceTranscript),
            preprocessEnabled ? "1" : "0",
            Field(preprocessModelId));
    }

    public PlaybackCacheHit? TryGet(Guid id, string fingerprint)
    {
        lock (_gate)
        {
            var slot = ReadSlot(id);
            if (slot is null || !File.Exists(WavPath(id)))
            {
                DeleteSlot(id);
                return null;
            }

            if (!string.Equals(slot.Fingerprint, fingerprint, StringComparison.Ordinal))
                return null;

            slot.LastUsedUtc = NextStamp();
            WriteSlot(slot);
            return new PlaybackCacheHit(WavPath(id), slot.SpokenText, slot.Weights);
        }
    }

    public void Store(Guid id, string sourceWavPath, string spokenText, IReadOnlyList<int>? weights, string fingerprint)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var dest = WavPath(id);
            var partial = dest + ".partial";
            File.Copy(sourceWavPath, partial, overwrite: true);
            File.Move(partial, dest, overwrite: true);

            WriteSlot(new SlotRecord
            {
                Id = id,
                SpokenText = spokenText ?? "",
                Weights = weights is null ? null : weights.ToList(),
                Fingerprint = fingerprint ?? "",
                LastUsedUtc = NextStamp()
            });
            TrimAndDropOrphans();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
                return;
            foreach (var file in Directory.EnumerateFiles(_directory).ToList())
                TryDelete(file);
        }
    }

    private DateTimeOffset NextStamp()
    {
        var now = _clock();
        var newest = DateTimeOffset.MinValue;
        foreach (var slot in ReadAllSlots())
        {
            if (slot.LastUsedUtc > newest)
                newest = slot.LastUsedUtc;
        }

        return now > newest ? now : newest.AddTicks(1);
    }

    private void TrimAndDropOrphans()
    {
        var slots = ReadAllSlots();
        var keep = new HashSet<Guid>();
        foreach (var slot in slots)
            keep.Add(slot.Id);

        var overflow = slots.Count - Capacity;
        if (overflow > 0)
        {
            foreach (var old in slots
                         .OrderBy(s => s.LastUsedUtc)
                         .ThenBy(s => s.Id)
                         .Take(overflow))
            {
                DeleteSlot(old.Id);
                keep.Remove(old.Id);
            }
        }

        if (!Directory.Exists(_directory))
            return;

        foreach (var file in Directory.EnumerateFiles(_directory).ToList())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(name, "N", out var id) || !keep.Contains(id))
                TryDelete(file);
        }
    }

    private List<SlotRecord> ReadAllSlots()
    {
        var slots = new List<SlotRecord>();
        if (!Directory.Exists(_directory))
            return slots;

        foreach (var meta in Directory.EnumerateFiles(_directory, "*.json").ToList())
        {
            var name = Path.GetFileNameWithoutExtension(meta);
            if (!Guid.TryParseExact(name, "N", out var id))
            {
                TryDelete(meta);
                continue;
            }

            var slot = ReadSlot(id);
            if (slot is null || !File.Exists(WavPath(id)))
            {
                DeleteSlot(id);
                continue;
            }

            slots.Add(slot);
        }

        return slots;
    }

    private SlotRecord? ReadSlot(Guid id)
    {
        var path = MetaPath(id);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var slot = JsonSerializer.Deserialize<SlotRecord>(json, JsonOptions);
            if (slot is null)
                return null;
            slot.Id = id;
            slot.SpokenText ??= "";
            slot.Fingerprint ??= "";
            return slot;
        }
        catch
        {
            return null;
        }
    }

    private void WriteSlot(SlotRecord slot)
    {
        var json = JsonSerializer.Serialize(slot, JsonOptions);
        File.WriteAllText(MetaPath(slot.Id), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void DeleteSlot(Guid id)
    {
        TryDelete(WavPath(id));
        TryDelete(MetaPath(id));
        TryDelete(WavPath(id) + ".partial");
    }

    private string WavPath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".wav");

    private string MetaPath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".json");

    private static string Field(string? value)
    {
        value ??= "";
        return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class SlotRecord
    {
        public Guid Id { get; set; }

        public string SpokenText { get; set; } = "";

        public List<int>? Weights { get; set; }

        public string Fingerprint { get; set; } = "";

        public DateTimeOffset LastUsedUtc { get; set; }
    }
}
