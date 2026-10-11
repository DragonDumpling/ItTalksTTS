using System.Text.Json;
using ItTalksTTS.Core.Services;

namespace ItTalksTTS.Tests;

public class PlaybackWavCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ittalks-wav-cache-" + Guid.NewGuid().ToString("N"));
    private readonly string _cacheDir;
    private readonly string _sourceWav;

    public PlaybackWavCacheTests()
    {
        _cacheDir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_root);
        _sourceWav = Path.Combine(_root, "source.wav");
        File.WriteAllBytes(_sourceWav, [1, 2, 3, 4]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Sixth_store_deletes_the_oldest_wav()
    {
        var cache = new PlaybackWavCache(_cacheDir);
        var ids = StoreDistinct(cache, 6, "fp");

        Assert.False(File.Exists(Wav(ids[0])));
        Assert.False(File.Exists(Meta(ids[0])));
        Assert.Equal(PlaybackWavCache.Capacity, WavCount());
        for (var i = 1; i < ids.Length; i++)
            Assert.True(File.Exists(Wav(ids[i])));
    }

    [Fact]
    public void Replay_makes_a_clip_newest_so_the_next_eviction_takes_another()
    {
        var cache = new PlaybackWavCache(_cacheDir);
        var ids = StoreDistinct(cache, 5, "fp");

        var replayed = cache.TryGet(ids[0], "fp");
        Assert.NotNull(replayed);

        var sixth = Guid.NewGuid();
        cache.Store(sixth, _sourceWav, "spoken-6", null, "fp");

        Assert.True(File.Exists(Wav(ids[0])));
        Assert.False(File.Exists(Wav(ids[1])));
        Assert.True(File.Exists(Wav(sixth)));
        Assert.Equal(PlaybackWavCache.Capacity, WavCount());
    }

    [Fact]
    public void Fingerprint_mismatch_is_a_miss_and_keeps_the_file()
    {
        var cache = new PlaybackWavCache(_cacheDir);
        var id = Guid.NewGuid();
        cache.Store(id, _sourceWav, "hello there", new List<int> { 1, 2 }, "voice-a");

        Assert.Null(cache.TryGet(id, "voice-b"));
        Assert.True(File.Exists(Wav(id)));

        var hit = cache.TryGet(id, "voice-a");
        Assert.NotNull(hit);
        Assert.Equal("hello there", hit!.SpokenText);
        Assert.Equal(new[] { 1, 2 }, hit.Weights);
        Assert.Equal(Wav(id), hit.WavPath);
    }

    [Fact]
    public void Replacing_the_same_id_does_not_grow_past_five()
    {
        var cache = new PlaybackWavCache(_cacheDir);
        var ids = StoreDistinct(cache, 5, "fp");

        cache.Store(ids[0], _sourceWav, "replaced", new List<int> { 4 }, "fp-new");

        Assert.Equal(PlaybackWavCache.Capacity, WavCount());
        Assert.Equal(PlaybackWavCache.Capacity, Directory.GetFiles(_cacheDir, "*.json").Length);
        Assert.Null(cache.TryGet(ids[0], "fp"));
        var hit = cache.TryGet(ids[0], "fp-new");
        Assert.NotNull(hit);
        Assert.Equal("replaced", hit!.SpokenText);
        Assert.Equal(new[] { 4 }, hit.Weights);
        for (var i = 1; i < ids.Length; i++)
            Assert.NotNull(cache.TryGet(ids[i], "fp"));
    }

    [Fact]
    public void Startup_drops_clips_beyond_the_five_newest()
    {
        var cache = new PlaybackWavCache(_cacheDir);
        var ids = StoreDistinct(cache, 5, "fp");

        var planted = Guid.NewGuid();
        File.Copy(Wav(ids[4]), Wav(planted));
        var sidecar = new Dictionary<string, object?>
        {
            ["id"] = planted,
            ["spokenText"] = "stale",
            ["weights"] = null,
            ["fingerprint"] = "fp",
            ["lastUsedUtc"] = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        File.WriteAllText(
            Meta(planted),
            JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.Equal(6, WavCount());

        _ = new PlaybackWavCache(_cacheDir);

        Assert.False(File.Exists(Wav(planted)));
        Assert.False(File.Exists(Meta(planted)));
        Assert.Equal(PlaybackWavCache.Capacity, WavCount());
        foreach (var id in ids)
            Assert.True(File.Exists(Wav(id)));
    }

    [Fact]
    public void Clear_removes_saved_clips()
    {
        var cache = new PlaybackWavCache(_cacheDir);
        StoreDistinct(cache, 2, "fp");
        cache.Clear();
        Assert.Empty(Directory.GetFiles(_cacheDir));
    }

    [Fact]
    public void Fingerprint_tracks_voice_engine_reference_and_preprocess()
    {
        var baseline = PlaybackWavCache.BuildFingerprint(
            "hello", "KokoroOnnx", "af_sarah", "", "", false, "llama-3.2-3b-instruct");

        Assert.Equal(
            baseline,
            PlaybackWavCache.BuildFingerprint(
                "hello", "KokoroOnnx", "af_sarah", "", "", false, "llama-3.2-3b-instruct"));
        Assert.NotEqual(
            baseline,
            PlaybackWavCache.BuildFingerprint(
                "hello", "KokoroOnnx", "af_bella", "", "", false, "llama-3.2-3b-instruct"));
        Assert.NotEqual(
            baseline,
            PlaybackWavCache.BuildFingerprint(
                "hello", "F5Tts", "af_sarah", @"C:\clips\me.wav", "reference", false, "llama-3.2-3b-instruct"));
        Assert.NotEqual(
            baseline,
            PlaybackWavCache.BuildFingerprint(
                "hello", "KokoroOnnx", "af_sarah", "", "", true, "llama-3.2-3b-instruct"));
        Assert.NotEqual(
            baseline,
            PlaybackWavCache.BuildFingerprint(
                "other", "KokoroOnnx", "af_sarah", "", "", false, "llama-3.2-3b-instruct"));
    }

    private Guid[] StoreDistinct(PlaybackWavCache cache, int count, string fingerprint)
    {
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = Guid.NewGuid();
            cache.Store(ids[i], _sourceWav, "spoken-" + i, null, fingerprint);
        }

        return ids;
    }

    private string Wav(Guid id) => Path.Combine(_cacheDir, id.ToString("N") + ".wav");

    private string Meta(Guid id) => Path.Combine(_cacheDir, id.ToString("N") + ".json");

    private int WavCount() => Directory.GetFiles(_cacheDir, "*.wav").Length;
}
