namespace AudioEq.Core.Dsp;

/// <summary>
/// A series chain of peaking bands — one <see cref="Biquad"/> per band.
/// Matches the app's N-band model: center frequencies, per-band gains, shared Q.
/// </summary>
public sealed class EqChain
{
    private readonly Biquad[] _bands;
    private readonly double[] _gains;

    public IReadOnlyList<double> FreqsHz { get; }
    public IReadOnlyList<double> GainsDb => _gains;
    public int SampleRate { get; }
    public double Q { get; }

    public EqChain(IReadOnlyList<double> freqsHz, IReadOnlyList<double> gainsDb, int sampleRate, double q = 1.0)
    {
        if (freqsHz.Count != gainsDb.Count)
        {
            throw new ArgumentException("freqsHz and gainsDb must have the same length");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        FreqsHz = freqsHz.ToArray();
        _gains = gainsDb.ToArray();
        SampleRate = sampleRate;
        Q = q;

        _bands = new Biquad[freqsHz.Count];
        for (var i = 0; i < freqsHz.Count; i++)
        {
            _bands[i] = Biquad.Peaking(freqsHz[i], q, _gains[i], sampleRate);
        }
    }

    /// <summary>Number of bands in the chain.</summary>
    public int BandCount => _bands.Length;

    /// <summary>Process a mono block in place through every band.</summary>
    public void ProcessInPlace(Span<float> samples)
    {
        foreach (var band in _bands)
        {
            band.ProcessInPlace(samples);
        }
    }

    /// <summary>Process a mono block, returning a new array.</summary>
    public float[] ProcessBlock(ReadOnlySpan<float> input)
    {
        var output = input.ToArray();
        ProcessInPlace(output);
        return output;
    }

    public void Reset()
    {
        foreach (var band in _bands)
        {
            band.Reset();
        }
    }

    /// <summary>Update per-band gains without resetting filter state —
    /// the live-engine path (no rebuild, no reset click).</summary>
    public void SetGains(ReadOnlySpan<double> gainsDb)
    {
        if (gainsDb.Length != _bands.Length)
        {
            throw new ArgumentException("gainsDb must match BandCount", nameof(gainsDb));
        }
        for (var i = 0; i < _bands.Length; i++)
        {
            _gains[i] = gainsDb[i];
            _bands[i].ConfigurePeaking(FreqsHz[i], Q, gainsDb[i], SampleRate);
        }
    }

    /// <summary>Combined magnitude response across all bands (linear).</summary>
    public double MagnitudeAt(double freqHz)
    {
        var mag = 1.0;
        foreach (var band in _bands)
        {
            mag *= band.MagnitudeAt(freqHz, SampleRate);
        }
        return mag;
    }

    /// <summary>
    /// Log-frequency magnitude response in dB, from <paramref name="fromHz"/>
    /// to <paramref name="toHz"/> in <paramref name="points"/> evenly spaced
    /// (log) samples. Powers the EQ response-curve widget — pure math, no audio.
    /// </summary>
    public double[] MagnitudeResponseDb(double fromHz, double toHz, int points)
    {
        if (points < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Need at least 2 points");
        }
        if (fromHz <= 0 || toHz <= fromHz)
        {
            throw new ArgumentOutOfRangeException(nameof(fromHz), "Require 0 < fromHz < toHz");
        }

        var response = new double[points];
        var logFrom = Math.Log10(fromHz);
        var logTo = Math.Log10(toHz);
        for (var i = 0; i < points; i++)
        {
            var f = Math.Pow(10, logFrom + (logTo - logFrom) * i / (points - 1));
            var mag = Math.Max(MagnitudeAt(f), 1e-12);
            response[i] = 20 * Math.Log10(mag);
        }
        return response;
    }
}
