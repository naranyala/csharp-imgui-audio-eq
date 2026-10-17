using AudioEq.Core.Dsp;

namespace AudioEq.Core.Audio;

/// <summary>
/// Offline (file) EQ rendering: apply band gains to a <see cref="WavData"/>
/// without any native audio path. This is the first *audible* proof in the
/// project — render, play the output file, hear the sliders. Each channel gets
/// a fresh <see cref="EqChain"/> so filter state never leaks between channels.
/// <paramref name="preampDb"/> is applied as a flat post-gain (headroom
/// control for boosted presets).
/// </summary>
public static class OfflineRenderer
{
    public static WavData RenderEqOnWav(
        WavData wav,
        IReadOnlyList<double> freqsHz,
        IReadOnlyList<double> gainsDb,
        double preampDb = 0,
        double q = 1.0)
    {
        var preampLin = Math.Pow(10, preampDb / 20);
        var input = wav.ChannelData();
        var output = new float[wav.Channels][];

        for (var c = 0; c < wav.Channels; c++)
        {
            var chain = new EqChain(freqsHz, gainsDb, wav.SampleRate, q);
            var channel = input[c];
            chain.ProcessInPlace(channel);
            if (preampLin != 1.0)
            {
                for (var i = 0; i < channel.Length; i++)
                {
                    channel[i] = (float)(channel[i] * preampLin);
                }
            }
            output[c] = channel;
        }

        var interleaved = new float[wav.Samples.Length];
        for (var c = 0; c < wav.Channels; c++)
        {
            for (var i = 0; i < wav.Frames; i++)
            {
                interleaved[i * wav.Channels + c] = output[c][i];
            }
        }

        return new WavData(wav.SampleRate, wav.Channels, interleaved);
    }

    /// <summary>Convenience for mono sample arrays (tests, demos).</summary>
    public static float[] RenderEqMono(
        ReadOnlySpan<float> samples,
        IReadOnlyList<double> freqsHz,
        IReadOnlyList<double> gainsDb,
        int sampleRate,
        double preampDb = 0,
        double q = 1.0)
    {
        var chain = new EqChain(freqsHz, gainsDb, sampleRate, q);
        var output = chain.ProcessBlock(samples);
        var preampLin = Math.Pow(10, preampDb / 20);
        if (preampLin == 1.0)
        {
            return output;
        }
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = (float)(output[i] * preampLin);
        }
        return output;
    }

    /// <summary>One-call render + serialize: EQ the wav and return PCM16 RIFF bytes.</summary>
    public static byte[] RenderToPcm16(
        WavData wav,
        IReadOnlyList<double> freqsHz,
        IReadOnlyList<double> gainsDb,
        double preampDb = 0,
        double q = 1.0)
        => RenderEqOnWav(wav, freqsHz, gainsDb, preampDb, q).WritePcm16();
}
