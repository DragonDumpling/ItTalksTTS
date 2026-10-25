using ItTalksTTS.Tts;

namespace ItTalksTTS.Tests;

public class EngineRegistryTests
{
    [Fact]
    public void FromKey_resolves_known_engines()
    {
        Assert.Equal(TtsEngineId.KokoroOnnx, EngineRegistry.FromKey("KokoroOnnx").Id);
        Assert.Equal(TtsEngineId.Phonon31m, EngineRegistry.FromKey("Phonon31m").Id);
        Assert.Equal(TtsEngineId.F5Tts, EngineRegistry.FromKey("F5TTS").Id);
        Assert.False(EngineRegistry.TryGet("nope", out _));
        Assert.True(EngineRegistry.TryGet("phonon31m", out var phonon));
        Assert.Equal(TtsEngineId.Phonon31m, phonon.Id);
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
        Assert.Equal(VoiceInputMode.NamedVoices, EngineRegistry.Phonon.VoiceMode);
        Assert.Equal(VoiceInputMode.ReferenceAudio, EngineRegistry.F5.VoiceMode);
        Assert.Equal("af_sarah", EngineRegistry.Kokoro.DefaultVoice);
        Assert.Equal("Easton", EngineRegistry.Phonon.DefaultVoice);
        Assert.True(EngineRegistry.F5.NeedsTorch);
        Assert.False(EngineRegistry.Kokoro.NeedsTorch);
        Assert.False(EngineRegistry.Phonon.NeedsTorch);
        Assert.NotEqual(EngineRegistry.Kokoro.VenvDir, EngineRegistry.Phonon.VenvDir);
        Assert.NotEqual(EngineRegistry.F5.ModelsDir, EngineRegistry.Phonon.ModelsDir);
    }

    [Fact]
    public void Setup_copy_names_the_selected_engine()
    {
        Assert.Contains("Phonon", EngineRegistry.Phonon.SetupSummary);
        Assert.DoesNotContain("Kokoro", EngineRegistry.Phonon.SetupSummary);
        Assert.Contains("Kokoro", EngineRegistry.Kokoro.SetupSummary);
        Assert.Contains("F5-TTS", EngineRegistry.F5.SetupSummary);
        Assert.Equal("Run setup", EngineRegistry.SetupButtonText(alreadyInstalled: false));
        Assert.Equal("Run setup again", EngineRegistry.SetupButtonText(alreadyInstalled: true));
    }
}
