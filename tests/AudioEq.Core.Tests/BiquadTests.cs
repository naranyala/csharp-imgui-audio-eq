using AudioEq.Core.Dsp;
using Xunit;
using static AudioEq.Core.Tests.TestAudio;

namespace AudioEq.Core.Tests;

public class BiquadTests
{
    private static readonly double[] AppFreqs = { 60, 230, 910, 3600, 14000 };

    [Fact]
    public void FlatChainIsTransparent()
    {
        var chain = new EqChain(AppFreqs, new double[] { 0, 0, 0, 0, 0 }, SampleRate);
        var input = Sine(1000);
        var output = chain.ProcessBlock(input);
        var ratio = Rms(output) / Rms(input);
        Assert.Equal(1.0, ratio, 6);
    }

    [Fact]
    public void Plus6DbPeakingDoublesAmplitudeAtCenterFrequency()
    {
        var chain = new EqChain(new[] { 1000.0 }, new[] { 6.0 }, SampleRate);
        var ratio = Rms(chain.ProcessBlock(Sine(1000))) / Rms(Sine(1000));
        Assert.InRange(ratio, 1.85, 2.15);
    }

    [Fact]
    public void Minus6DbPeakingHalvesAmplitudeAtCenterFrequency()
    {
        var chain = new EqChain(new[] { 1000.0 }, new[] { -6.0 }, SampleRate);
        var ratio = Rms(chain.ProcessBlock(Sine(1000))) / Rms(Sine(1000));
        Assert.InRange(ratio, 0.45, 0.55);
    }

    [Fact]
    public void PeakingIsSelectiveFarAwayToneNearlyUntouched()
    {
        var chain = new EqChain(new[] { 1000.0 }, new[] { 6.0 }, SampleRate);
        var ratio = Rms(chain.ProcessBlock(Sine(100))) / Rms(Sine(100));
        Assert.True(ratio < 1.2, $"ratio was {ratio}");
    }

    [Fact]
    public void MagnitudeAtAgreesWithTimeDomainGain()
    {
        var band = Biquad.Peaking(1000, q: 1, gainDb: 6, SampleRate);
        Assert.InRange(band.MagnitudeAt(1000, SampleRate), 1.99, 2.01);
        Assert.True(band.MagnitudeAt(100, SampleRate) < 1.2);
    }

    [Fact]
    public void LowShelfBoostsLowsSparesHighs()
    {
        var shelf = Biquad.LowShelf(200, 6, SampleRate);
        var lowRatio = Rms(shelf.ProcessBlock(Sine(50))) / Rms(Sine(50));
        shelf.Reset();
        var highRatio = Rms(shelf.ProcessBlock(Sine(10000))) / Rms(Sine(10000));
        Assert.True(lowRatio > 1.5, $"lowRatio was {lowRatio}");
        Assert.True(highRatio < 1.2, $"highRatio was {highRatio}");
    }

    [Fact]
    public void HighShelfBoostsHighsSparesLows()
    {
        var shelf = Biquad.HighShelf(8000, 6, SampleRate);
        var highRatio = Rms(shelf.ProcessBlock(Sine(15000))) / Rms(Sine(15000));
        shelf.Reset();
        var lowRatio = Rms(shelf.ProcessBlock(Sine(100))) / Rms(Sine(100));
        Assert.True(highRatio > 1.5, $"highRatio was {highRatio}");
        Assert.True(lowRatio < 1.2, $"lowRatio was {lowRatio}");
    }

    [Fact]
    public void NotchAttenuatesAtCenterFrequency()
    {
        var notch = Biquad.Notch(1000, q: 4, SampleRate);
        Assert.True(notch.MagnitudeAt(1000, SampleRate) < 0.05,
            $"magnitude at center was {notch.MagnitudeAt(1000, SampleRate)}");
        Assert.True(notch.MagnitudeAt(100, SampleRate) > 0.9);
        Assert.True(notch.MagnitudeAt(10000, SampleRate) > 0.9);
    }

    [Fact]
    public void DegenerateInputsAreClampedToFiniteStableCoefficients()
    {
        // freq/Q/gain far outside sane ranges must not produce NaN/Inf.
        var biquads = new[]
        {
            Biquad.Peaking(freqHz: -5000, q: 0, gainDb: -1e9, SampleRate),
            Biquad.Peaking(freqHz: 1e9, q: 1e9, gainDb: 1e9, SampleRate),
            Biquad.LowShelf(freqHz: 0, gainDb: 1e6, SampleRate),
            Biquad.HighShelf(freqHz: double.NaN, gainDb: 999, SampleRate),
            Biquad.Notch(freqHz: -1, q: -10, SampleRate),
        };

        foreach (var b in biquads)
        {
            Assert.True(double.IsFinite(b.B0) && double.IsFinite(b.B1) && double.IsFinite(b.B2) &&
                        double.IsFinite(b.A1) && double.IsFinite(b.A2), $"non-finite coefficients for {b.Type}");
            b.Reset();
            var y = b.Process(0.5);
            Assert.True(double.IsFinite(y), $"non-finite output for {b.Type}");
        }
    }

    [Fact]
    public void ClampedParametersLandInsideDocumentedRanges()
    {
        var low = Biquad.Peaking(-5000, q: 0, gainDb: -1e9, SampleRate);
        Assert.Equal(1.0, low.FreqHz, 9);
        Assert.Equal(0.05, low.Q, 9);
        Assert.Equal(-60.0, low.GainDb, 9);

        var high = Biquad.Peaking(1e9, q: 1e9, gainDb: 1e9, SampleRate);
        Assert.Equal(SampleRate * 0.499, high.FreqHz, 6);
        Assert.Equal(100.0, high.Q, 9);
        Assert.Equal(30.0, high.GainDb, 9);
    }

    [Fact]
    public void ProcessInPlaceMatchesProcessBlock()
    {
        var input = Sine(700, seconds: 0.25);
        var a = Biquad.Peaking(700, 1.5, 5, SampleRate);
        var b = Biquad.Peaking(700, 1.5, 5, SampleRate);

        var returned = a.ProcessBlock(input);
        var inPlace = input.ToArray();
        b.ProcessInPlace(inPlace);

        Assert.Equal(returned, inPlace);
    }

    [Fact]
    public void ResetRestoresInitialState()
    {
        var processed = Biquad.Peaking(1000, 2, 6, SampleRate);
        var fresh = Biquad.Peaking(1000, 2, 6, SampleRate);

        // Warm up `processed` with one block, then reset.
        processed.ProcessBlock(Sine(1000, seconds: 0.1));
        processed.Reset();

        var chunk = Sine(300, seconds: 0.1);
        var afterReset = processed.ProcessBlock(chunk);
        var freshOutput = fresh.ProcessBlock(chunk);

        Assert.Equal(freshOutput, afterReset);
    }

    [Fact]
    public void HigherQIsNarrowerAroundTheSameCenter()
    {
        var wide = Biquad.Peaking(1000, q: 0.5, gainDb: 6, SampleRate);
        var narrow = Biquad.Peaking(1000, q: 5, gainDb: 6, SampleRate);

        // Same gain at center...
        Assert.Equal(2.0, wide.MagnitudeAt(1000, SampleRate), 2);
        Assert.Equal(2.0, narrow.MagnitudeAt(1000, SampleRate), 2);

        // ...but the high-Q band has fallen off more an octave and a half away.
        Assert.True(narrow.MagnitudeAt(3000, SampleRate) < wide.MagnitudeAt(3000, SampleRate),
            "narrow filter should attenuate off-center more than wide filter");
    }

    [Fact]
    public void ImpulseResponseDecaysAndStaysFinite()
    {
        // Resonant band (Q=20) kicked with an impulse must ring down, not blow up.
        var biquad = Biquad.Peaking(1000, q: 20, gainDb: 6, SampleRate);
        var input = new float[SampleRate]; // 1 second
        input[0] = 1f;

        var output = biquad.ProcessBlock(input);

        double maxAbs = 0;
        foreach (var y in output)
        {
            Assert.True(float.IsFinite(y), "non-finite output sample");
            maxAbs = Math.Max(maxAbs, Math.Abs(y));
        }
        Assert.True(maxAbs < 10, $"peak was {maxAbs}");

        // Tail must be negligible after ~1 s of decay.
        Assert.True(Math.Abs(output[^1]) < 1e-6, $"tail was {output[^1]}");
    }

    [Fact]
    public void ExtremeClampedSettingsStayBoundedOnLiveSignal()
    {
        var biquad = Biquad.Peaking(freqHz: 1e9, q: 1e9, gainDb: 1e9, SampleRate);

        var input = new float[SampleRate]; // 1 second of deterministic multi-tone
        for (var i = 0; i < input.Length; i++)
        {
            var t = (double)i / SampleRate;
            input[i] = (float)(0.9 * (Math.Sin(2 * Math.PI * 440 * t) +
                                      0.5 * Math.Sin(2 * Math.PI * 3000 * t)));
        }

        var output = biquad.ProcessBlock(input);
        foreach (var y in output)
        {
            Assert.True(float.IsFinite(y), "non-finite output sample");
            Assert.True(Math.Abs(y) < 100, $"unbounded output {y}");
        }
    }

    [Fact]
    public void ConfigurePeakingUpdatesCoefficientsAndKeepsState()
    {
        var biquad = Biquad.Peaking(1000, 1.0, 0.0, SampleRate);
        var warmup = Sine(1000);
        _ = biquad.ProcessBlock(warmup);

        biquad.ConfigurePeaking(1000, 1.0, 6.0, SampleRate);

        Assert.Equal(6.0, biquad.GainDb);
        Assert.InRange(biquad.MagnitudeAt(1000, SampleRate), 1.85, 2.15);

        var output = biquad.ProcessBlock(Sine(1000));
        foreach (var y in output)
        {
            Assert.True(float.IsFinite(y), "non-finite sample after ConfigurePeaking");
        }
    }

    [Fact]
    public void ConfigurePeakingClampsDegenerateParameters()
    {
        var biquad = Biquad.Peaking(1000, 1.0, 0.0, SampleRate);
        biquad.ConfigurePeaking(-5, -1, 1e9, SampleRate);
        Assert.Equal(1.0, biquad.FreqHz);
        Assert.Equal(0.05, biquad.Q);
        Assert.Equal(30.0, biquad.GainDb);
    }
}
