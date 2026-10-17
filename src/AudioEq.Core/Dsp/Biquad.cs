namespace AudioEq.Core.Dsp;

/// <summary>Kind of second-order section.</summary>
public enum BiquadType
{
    Peaking,
    LowShelf,
    HighShelf,
    Notch,
}

/// <summary>
/// Second-order IIR filter (biquad) using the RBJ Audio EQ Cookbook formulas.
/// See https://www.w3.org/TR/audio-eq-cookbook/ — the same math behind most
/// parametric EQs worldwide (Equalizer APO, EasyEffects, CamillaDSP).
/// Coefficients are normalized (a0 = 1). Instances are stateful (Direct Form I);
/// use one instance per channel, or <see cref="EqChain"/> which handles that.
/// </summary>
public sealed class Biquad
{
    public BiquadType Type { get; }
    public double FreqHz { get; private set; }
    public double Q { get; private set; }
    public double GainDb { get; private set; }

    public double B0 { get; private set; }
    public double B1 { get; private set; }
    public double B2 { get; private set; }
    public double A1 { get; private set; }
    public double A2 { get; private set; }

    private double _x1, _x2, _y1, _y2;

    private Biquad(BiquadType type, double freqHz, double q, double gainDb,
        double b0, double b1, double b2, double a1, double a2)
    {
        Type = type;
        FreqHz = freqHz;
        Q = q;
        GainDb = gainDb;
        B0 = b0;
        B1 = b1;
        B2 = b2;
        A1 = a1;
        A2 = a2;
    }

    /// <summary>Peaking (bell) band: boosts/cuts around <paramref name="freqHz"/>
    /// with bandwidth set by <paramref name="q"/>. 0 dB is bit-transparent.</summary>
    public static Biquad Peaking(double freqHz, double q, double gainDb, int sampleRate)
    {
        var c = ComputePeaking(freqHz, q, gainDb, sampleRate);
        return new Biquad(BiquadType.Peaking, c.freqHz, c.q, c.gainDb, c.b0, c.b1, c.b2, c.a1, c.a2);
    }

    /// <summary>Recompute peaking coefficients in place, preserving DF1 state.
    /// Live engine gain changes use this instead of rebuilding instances.</summary>
    public void ConfigurePeaking(double freqHz, double q, double gainDb, int sampleRate)
    {
        if (Type != BiquadType.Peaking)
        {
            throw new InvalidOperationException("Only peaking bands are reconfigurable");
        }
        var c = ComputePeaking(freqHz, q, gainDb, sampleRate);
        FreqHz = c.freqHz;
        Q = c.q;
        GainDb = c.gainDb;
        B0 = c.b0;
        B1 = c.b1;
        B2 = c.b2;
        A1 = c.a1;
        A2 = c.a2;
    }

    private static (double freqHz, double q, double gainDb, double b0, double b1, double b2, double a1, double a2)
        ComputePeaking(double freqHz, double q, double gainDb, int sampleRate)
    {
        freqHz = Clamp(freqHz, 1.0, sampleRate * 0.499);
        q = Clamp(q, 0.05, 100.0);
        gainDb = Clamp(gainDb, -60.0, 30.0);

        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * freqHz / sampleRate;
        var alpha = Math.Sin(w0) / (2 * q);
        var cosW0 = Math.Cos(w0);

        var b0 = 1 + alpha * a;
        var b1 = -2 * cosW0;
        var b2 = 1 - alpha * a;
        var a1 = -2 * cosW0;
        var a2 = 1 - alpha / a;
        var a0 = 1 + alpha / a;
        return (freqHz, q, gainDb, b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>Low shelf: boosts/cuts everything below <paramref name="freqHz"/>.
    /// <paramref name="s"/> is shelf slope (1 = gentle, higher = steeper).</summary>
    public static Biquad LowShelf(double freqHz, double gainDb, int sampleRate, double s = 1.0)
        => Create(BiquadType.LowShelf, freqHz, q: 1.0, gainDb, sampleRate, (a, w0, alpha, cosW0, sqrtA, ss) =>
        {
            var al = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / ss - 1) + 2);
            var b0 = a * ((a + 1) - (a - 1) * cosW0 + 2 * sqrtA * al);
            var b1 = 2 * a * ((a - 1) - (a + 1) * cosW0);
            var b2 = a * ((a + 1) - (a - 1) * cosW0 - 2 * sqrtA * al);
            var a1 = -2 * ((a - 1) + (a + 1) * cosW0);
            var a2 = (a + 1) + (a - 1) * cosW0 - 2 * sqrtA * al;
            return (b0, b1, b2, a1, a2, (a + 1) + (a - 1) * cosW0 + 2 * sqrtA * al);
        }, s);

    /// <summary>High shelf: boosts/cuts everything above <paramref name="freqHz"/>.</summary>
    public static Biquad HighShelf(double freqHz, double gainDb, int sampleRate, double s = 1.0)
        => Create(BiquadType.HighShelf, freqHz, q: 1.0, gainDb, sampleRate, (a, w0, alpha, cosW0, sqrtA, ss) =>
        {
            var al = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / ss - 1) + 2);
            var b0 = a * ((a + 1) + (a - 1) * cosW0 + 2 * sqrtA * al);
            var b1 = -2 * a * ((a - 1) + (a + 1) * cosW0);
            var b2 = a * ((a + 1) + (a - 1) * cosW0 - 2 * sqrtA * al);
            var a1 = 2 * ((a - 1) - (a + 1) * cosW0);
            var a2 = (a + 1) - (a - 1) * cosW0 - 2 * sqrtA * al;
            return (b0, b1, b2, a1, a2, (a + 1) - (a - 1) * cosW0 + 2 * sqrtA * al);
        }, s);

    /// <summary>Notch (band-reject): removes a narrow band around
    /// <paramref name="freqHz"/>; gainDb is unused (kept for API symmetry).</summary>
    public static Biquad Notch(double freqHz, double q, int sampleRate)
        => Create(BiquadType.Notch, freqHz, q, gainDb: 0, sampleRate, (a, w0, alpha, cosW0, sqrtA, ss) =>
        {
            var b0 = 1.0;
            var b1 = -2 * cosW0;
            var b2 = 1.0;
            var a1 = -2 * cosW0;
            var a2 = 1 - alpha;
            return (b0, b1, b2, a1, a2, 1 + alpha);
        });

    private static Biquad Create(
        BiquadType type, double freqHz, double q, double gainDb, int sampleRate,
        Func<double, double, double, double, double, double,
            (double b0, double b1, double b2, double a1, double a2, double a0)> formula,
        double s = 1.0)
    {
        freqHz = Clamp(freqHz, 1.0, sampleRate * 0.499);
        q = Clamp(q, 0.05, 100.0);
        gainDb = Clamp(gainDb, -60.0, 30.0);

        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * freqHz / sampleRate;
        var alpha = Math.Sin(w0) / (2 * q);
        var cosW0 = Math.Cos(w0);
        var sqrtA = Math.Sqrt(a);

        var (b0, b1, b2, a1, a2, a0) = formula(a, w0, alpha, cosW0, sqrtA, s);
        return new Biquad(type, freqHz, q, gainDb, b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    private static double Clamp(double v, double lo, double hi)
    {
        if (double.IsNaN(v))
        {
            return lo;
        }
        return v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Process one sample in [-1, 1].</summary>
    public double Process(double x)
    {
        var y = B0 * x + B1 * _x1 + B2 * _x2 - A1 * _y1 - A2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    /// <summary>Process a block in place.</summary>
    public void ProcessInPlace(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Process(samples[i]);
        }
    }

    /// <summary>Process a block, returning a new array.</summary>
    public float[] ProcessBlock(ReadOnlySpan<float> input)
    {
        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            output[i] = (float)Process(input[i]);
        }
        return output;
    }

    public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

    /// <summary>Steady-state magnitude response |H(f)| at <paramref name="freqHz"/>
    /// (linear, not dB). Used by the EQ curve widget and tests — no audio needed.</summary>
    public double MagnitudeAt(double freqHz, int sampleRate)
    {
        var w = 2 * Math.PI * freqHz / sampleRate;
        var cosW = Math.Cos(w);
        var cos2W = Math.Cos(2 * w);
        var sinW = Math.Sin(w);
        var sin2W = Math.Sin(2 * w);
        var numRe = B0 + B1 * cosW + B2 * cos2W;
        var numIm = -(B1 * sinW + B2 * sin2W);
        var denRe = 1 + A1 * cosW + A2 * cos2W;
        var denIm = -(A1 * sinW + A2 * sin2W);
        return Math.Sqrt(numRe * numRe + numIm * numIm) /
               Math.Sqrt(denRe * denRe + denIm * denIm);
    }
}
