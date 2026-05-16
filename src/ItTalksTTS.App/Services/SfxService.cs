using System.Text;
using NAudio.Wave;

namespace ItTalksTTS.App.Services;

/// <summary>Short UI feedback tones (generated PCM — no bundled binary assets).</summary>
public static class SfxService
{
    public static void PlayTap() => PlayFireForget(BuildTone(1350, 32, 0.11));

    public static void PlayButton() => PlayFireForget(BuildTone(720, 42, 0.13));

    public static void PlaySuccess() => PlayFireForget(Concat(BuildTone(523, 95, 0.11), Silence(28), BuildTone(784, 130, 0.13)));

    private static void PlayFireForget(byte[] pcm16MonoWav)
    {
        _ = Task.Run(() =>
            {
                try
                {
                    using var ms = new MemoryStream(pcm16MonoWav);
                    using var rdr = new WaveFileReader(ms);
                    using var wo = new WaveOutEvent();
                    wo.Init(rdr);
                    wo.Play();
                    while (wo.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(15);
                }
                catch
                {
                    /* ignore sfx failures */
                }
            });
    }

    private static byte[] Silence(int ms, int sampleRate = 44100)
    {
        var n = sampleRate * ms / 1000;
        var samples = new short[n];
        return EncodeWav(samples, sampleRate);
    }

    private static byte[] BuildTone(double frequencyHz, int durationMs, double amplitude)
    {
        const int sampleRate = 44100;
        var sampleCount = sampleRate * durationMs / 1000;
        var samples = new short[sampleCount];
        var twoPiF = 2.0 * Math.PI * frequencyHz / sampleRate;
        for (var i = 0; i < sampleCount; i++)
        {
            var env = Math.Min(1.0, i / (double)Math.Min(sampleCount, 480)) * Math.Min(1.0, (sampleCount - 1 - i) / (double)Math.Min(sampleCount, 900));
            var v = Math.Sin(twoPiF * i) * amplitude * env;
            var s = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
            samples[i] = s;
        }

        return EncodeWav(samples, sampleRate);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var buf = new byte[len];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, buf, o, p.Length);
            o += p.Length;
        }

        return buf;
    }

    private static byte[] EncodeWav(short[] samples, int sampleRate)
    {
        const int channels = 1;
        const int bits = 16;
        var dataSize = samples.Length * sizeof(short);
        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bits / 8);
        bw.Write((short)(channels * bits / 8));
        bw.Write((short)bits);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        foreach (var s in samples)
            bw.Write(s);
        return ms.ToArray();
    }
}
