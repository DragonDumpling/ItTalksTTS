using ItTalksTTS.Tts;

namespace ItTalksTTS.Tests;

public class EngineRegistryTests
{
    [Fact]
    public void FromKey_resolves_known_engines()
    {
        Assert.Equal(TtsEngineId.KokoroOnnx, EngineRegistry.FromKey("KokoroOnnx").Id);
        Assert.Equal(TtsEngineId.F5Tts, EngineRegistry.FromKey("F5TTS").Id);
    }

    [Fact]
    public void FromKey_falls_back_to_default_for_unknown_or_null()
    {
        Assert.Equal(EngineRegistry.Default.Id, EngineRegistry.FromKey(null).Id);
        Assert.Equal(EngineRegistry.Default.Id, EngineRegistry.FromKey("nope").Id);
    }

    [Fact]
    public void Engines_have_distinct_keys_and_worker_scripts()
    {
        Assert.Equal(EngineRegistry.All.Count, EngineRegistry.All.Select(e => e.SettingsKey).Distinct().Count());
        Assert.Equal(EngineRegistry.All.Count, EngineRegistry.All.Select(e => e.WorkerScript).Distinct().Count());
    }

    [Fact]
    public void Voice_modes_match_engine_paradigms()
    {
        Assert.Equal(VoiceInputMode.NamedVoices, EngineRegistry.Kokoro.VoiceMode);
        Assert.Equal(VoiceInputMode.ReferenceAudio, EngineRegistry.F5.VoiceMode);
        Assert.True(EngineRegistry.F5.NeedsTorch);
        Assert.False(EngineRegistry.Kokoro.NeedsTorch);
    }
}
