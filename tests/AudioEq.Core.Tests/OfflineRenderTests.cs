using AudioEq.Core.Audio;
using Xunit;
using static AudioEq.Core.Tests.TestAudio;

namespace AudioEq.Core.Tests;

public class OfflineRenderTests
{
    private static readonly double[] AppFreqs = { 60, 230, 910, 3600, 14000 };

    [Fact]
    public void FlatRenderIsTransparentAndPreservesWavMetadata()
    {
        var left = Sine(1000);
        var right = Sine(500);
        var interleaved = new float[left.Length * 2];
        for (var i = 0; i < left.Length; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }
        var wav = new WavData(SampleRate, 2, interleaved);

        var output = OfflineRenderer.RenderEqOnWav(
            wav, AppFreqs, new double[] { 0, 0, 0, 0, 0 });

        Assert.Equal(SampleRate, output.SampleRate);
        Assert.Equal(2, output.Channels);
        Assert.Equal(wav.Frames, output.Frames);

        var outLeft = output.ChannelData()[0];
        Assert.Equal(1.0, Rms(outLeft) / Rms(left), 4);
    }

    [Fact]
    public void SerializedOutputIsRealRiffFile()
    {
        var wav = new WavData(SampleRate, 1, Sine(1000));
        var bytes = wav.WritePcm16();

        Assert.True(bytes.Length > 44);
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal("WAVE"u8.ToArray(), bytes[8..12]);

        var reread = WavData.Read(bytes);
        Assert.Equal(SampleRate, reread.SampleRate);
        Assert.Equal(1, reread.Channels);
        Assert.Equal(wav.Frames, reread.Frames);
    }

    [Fact]
    public void BoostedRenderIsMeasurablyLouderAtCenterFrequency()
    {
        var input = Sine(910);
        var wav = new WavData(SampleRate, 1, input);
        var output = OfflineRenderer.RenderEqOnWav(
            wav, AppFreqs, new[] { 0.0, 0.0, 6.0, 0.0, 0.0 });

        var ratio = Rms(output.Samples) / Rms(input);
        Assert.InRange(ratio, 1.85, 2.15);
    }

    [Fact]
    public void PreampAppliesFlatGainWithoutTouchingBandGains()
    {
        var input = Sine(910);
        var boosted = OfflineRenderer.RenderEqMono(
            input, new[] { 910.0 }, new[] { 0.0 }, SampleRate, preampDb: 6);

        var ratio = Rms(boosted) / Rms(input);
        Assert.InRange(ratio, 1.95, 2.05);
    }

    [Fact]
    public void ChannelsAreProcessedIndependently()
    {
        var input = Sine(910);
        var boosted = OfflineRenderer.RenderEqMono(
            input, new[] { 910.0 }, new[] { 6.0 }, SampleRate);
        var flat = OfflineRenderer.RenderEqMono(
            input, new[] { 910.0 }, new[] { 0.0 }, SampleRate);

        var ratio = Rms(boosted) / Rms(flat);
        Assert.InRange(ratio, 1.85, 2.15);
    }

    [Fact]
    public void StereoChannelsDoNotLeakFilterState()
    {
        // Identical channels in -> identical channels out (fresh EqChain per channel).
        var mono = Sine(1000, seconds: 0.5);
        var interleaved = new float[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++)
        {
            interleaved[i * 2] = mono[i];
            interleaved[i * 2 + 1] = mono[i];
        }
        var wav = new WavData(SampleRate, 2, interleaved);

        var output = OfflineRenderer.RenderEqOnWav(
            wav, new[] { 1000.0 }, new[] { 6.0 }, q: 2.0);
        var channels = output.ChannelData();

        for (var i = 0; i < channels[0].Length; i++)
        {
            Assert.Equal(channels[0][i], channels[1][i], 6);
        }
    }

    [Fact]
    public void RenderDoesNotMutateInputWav()
    {
        var input = Sine(910, seconds: 0.25);
        var original = input.ToArray();
        var wav = new WavData(SampleRate, 1, input);

        OfflineRenderer.RenderEqOnWav(wav, new[] { 910.0 }, new[] { 12.0 }, q: 4);

        Assert.Equal(original, wav.Samples);
    }

    [Fact]
    public void CutMakesSignalMeasurablyQuieter()
    {
        var input = Sine(910);
        var wav = new WavData(SampleRate, 1, input);
        var output = OfflineRenderer.RenderEqOnWav(
            wav, AppFreqs, new[] { 0.0, 0.0, -6.0, 0.0, 0.0 });

        var ratio = Rms(output.Samples) / Rms(input);
        Assert.InRange(ratio, 0.45, 0.55);
    }

    [Fact]
    public void SameInputRendersDeterministically()
    {
        var wav = new WavData(SampleRate, 1, Sine(440, seconds: 0.2));
        var args = (new[] { 60.0, 230.0, 910.0 }, new[] { 3.0, -2.0, 1.0 }, 2.5, 1.1);

        var a = OfflineRenderer.RenderToPcm16(wav, args.Item1, args.Item2, args.Item3, args.Item4);
        var b = OfflineRenderer.RenderToPcm16(wav, args.Item1, args.Item2, args.Item3, args.Item4);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RenderToPcm16ProducesPlayableBoostedRiff()
    {
        var input = Sine(910);
        var wav = new WavData(SampleRate, 1, input);

        var bytes = OfflineRenderer.RenderToPcm16(
            wav, AppFreqs, new[] { 0.0, 0.0, 6.0, 0.0, 0.0 });

        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        var reread = WavData.Read(bytes);
        var ratio = Rms(reread.Samples) / Rms(input);
        Assert.InRange(ratio, 1.85, 2.15);
    }

    [Fact]
    public void RenderAppliesQFromParameter()
    {
        var input = Sine(910);
        var wav = new WavData(SampleRate, 1, input);

        var wide = OfflineRenderer.RenderEqOnWav(wav, new[] { 910.0 }, new[] { 6.0 }, q: 0.5);
        var narrow = OfflineRenderer.RenderEqOnWav(wav, new[] { 910.0 }, new[] { 6.0 }, q: 5.0);

        // At the exact center both should be ~x2 regardless of Q;
        // away from it the narrow band must differ from the wide one.
        var inputFar = Sine(2730); // ~1.5 octaves above 910
        var farWav = new WavData(SampleRate, 1, inputFar);
        var wideFar = Rms(OfflineRenderer.RenderEqOnWav(farWav, new[] { 910.0 }, new[] { 6.0 }, q: 0.5).Samples);
        var narrowFar = Rms(OfflineRenderer.RenderEqOnWav(farWav, new[] { 910.0 }, new[] { 6.0 }, q: 5.0).Samples);

        Assert.InRange(Rms(wide.Samples) / Rms(input), 1.85, 2.15);
        Assert.InRange(Rms(narrow.Samples) / Rms(input), 1.85, 2.15);
        Assert.True(narrowFar < wideFar, $"narrow={narrowFar}, wide={wideFar}");
    }
}
