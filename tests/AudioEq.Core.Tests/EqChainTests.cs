using AudioEq.Core.Dsp;
using Xunit;
using static AudioEq.Core.Tests.TestAudio;

namespace AudioEq.Core.Tests;

public class EqChainTests
{
    [Fact]
    public void ChainMagnitudeEqualsProductOfBandMagnitudes()
    {
        var freqs = new[] { 60.0, 230.0, 910.0 };
        var gains = new[] { 4.0, -1.5, 3.0 };
        var chain = new EqChain(freqs, gains, SampleRate, q: 1.2);

        foreach (var f in new[] { 50.0, 230.0, 700.0, 5000.0 })
        {
            double product = 1;
            for (var i = 0; i < freqs.Length; i++)
            {
                product *= Biquad.Peaking(freqs[i], 1.2, gains[i], SampleRate).MagnitudeAt(f, SampleRate);
            }
            Assert.Equal(product, chain.MagnitudeAt(f), 12);
        }
    }

    [Fact]
    public void EmptyChainIsIdentity()
    {
        var chain = new EqChain(Array.Empty<double>(), Array.Empty<double>(), SampleRate);
        Assert.Equal(0, chain.BandCount);
        Assert.Equal(1.0, chain.MagnitudeAt(1000), 12);

        var input = Sine(1000, seconds: 0.1);
        Assert.Equal(input, chain.ProcessBlock(input));

        foreach (var db in chain.MagnitudeResponseDb(20, 20000, 17))
        {
            Assert.Equal(0.0, db, 9);
        }
    }

    [Fact]
    public void InvalidArgumentsThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new EqChain(new[] { 60.0 }, new[] { 1.0, 2.0 }, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EqChain(new[] { 60.0 }, new[] { 1.0 }, sampleRate: 0));
    }

    [Fact]
    public void ResponseDbForFlatChainIsZeroEverywhere()
    {
        var chain = new EqChain(new[] { 60.0, 230.0, 910.0, 3600.0, 14000.0 },
            new double[] { 0, 0, 0, 0, 0 }, SampleRate);

        foreach (var db in chain.MagnitudeResponseDb(20, 20000, 257))
        {
            Assert.Equal(0.0, db, 9);
        }
    }

    [Fact]
    public void ResponseDbBoostedBandPeaksNearItsFrequency()
    {
        const double bandFreq = 910;
        var chain = new EqChain(new[] { bandFreq }, new[] { 6.0 }, SampleRate);

        const int points = 513;
        var response = chain.MagnitudeResponseDb(1, 20000, points);

        var peakIndex = 0;
        for (var i = 1; i < response.Length; i++)
        {
            if (response[i] > response[peakIndex])
            {
                peakIndex = i;
            }
        }

        var peakFreq = Math.Pow(10, Math.Log10(1) + (Math.Log10(20000) - Math.Log10(1)) * peakIndex / (points - 1));
        Assert.InRange(peakFreq, bandFreq * 0.97, bandFreq * 1.03);
        Assert.InRange(response[peakIndex], 5.9, 6.1);
    }

    [Fact]
    public void ResponseDbUsesExactLogSpacingAndMatchesDirectMagnitude()
    {
        var chain = new EqChain(new[] { 500.0 }, new[] { -6.0 }, SampleRate, q: 0.8);

        const int points = 65;
        var fromHz = 20.0;
        var toHz = 20000.0;
        var response = chain.MagnitudeResponseDb(fromHz, toHz, points);

        for (var i = 0; i < points; i++)
        {
            var f = Math.Pow(10, Math.Log10(fromHz) + (Math.Log10(toHz) - Math.Log10(fromHz)) * i / (points - 1));
            var expected = 20 * Math.Log10(chain.MagnitudeAt(f));
            Assert.Equal(expected, response[i], 9);
        }
    }

    [Fact]
    public void ResponseDbRejectsDegenerateRanges()
    {
        var chain = new EqChain(new[] { 100.0 }, new[] { 3.0 }, SampleRate);
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.MagnitudeResponseDb(20, 20000, points: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.MagnitudeResponseDb(0, 20000, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.MagnitudeResponseDb(20000, 20, 16));
    }

    [Fact]
    public void MeasuredTimeDomainGainMatchesAnalyticMagnitude()
    {
        // Sweep: RMS of processed sine vs MagnitudeAt at the same frequency.
        var chain = new EqChain(new[] { 1000.0 }, new[] { 6.0 }, SampleRate, q: 1.0);

        foreach (var f in new[] { 100.0, 250.0, 1000.0, 4000.0, 10000.0 })
        {
            var input = Sine(f);
            var measured = Rms(chain.ProcessBlock(input)) / Rms(input);
            var expected = chain.MagnitudeAt(f);
            var relError = Math.Abs(measured / expected - 1.0);
            Assert.True(relError < 0.05, $"f={f}: measured={measured}, expected={expected}, relErr={relError}");
            chain.Reset();
        }
    }

    [Fact]
    public void SetGainsUpdatesMagnitudeResponse()
    {
        var chain = new EqChain(new[] { 1000.0 }, new[] { 0.0 }, SampleRate);
        Assert.InRange(chain.MagnitudeAt(1000), 0.99, 1.01);

        chain.SetGains(new[] { 6.0 });
        var mag = chain.MagnitudeAt(1000);
        Assert.InRange(mag, 1.85, 2.15);
        Assert.Equal(6.0, chain.GainsDb[0]);

        chain.SetGains(new[] { -6.0 });
        Assert.InRange(chain.MagnitudeAt(1000), 0.45, 0.55);
    }

    [Fact]
    public void SetGainsPreservesStateAndStaysFinite()
    {
        var chain = new EqChain(new[] { 1000.0 }, new[] { 0.0 }, SampleRate, q: 2.0);
        var impulse = new float[256];
        impulse[0] = 1f;
        chain.ProcessInPlace(impulse);

        chain.SetGains(new[] { 12.0 });

        var input = Sine(1000, seconds: 0.05);
        foreach (var y in chain.ProcessBlock(input))
        {
            Assert.True(float.IsFinite(y), "non-finite sample after SetGains");
        }
    }

    [Fact]
    public void SetGainsRejectsWrongLength()
    {
        var chain = new EqChain(new[] { 1000.0, 3000.0 }, new[] { 0.0, 0.0 }, SampleRate);
        Assert.Throws<ArgumentException>(() => chain.SetGains(new[] { 1.0 }));
    }
}
