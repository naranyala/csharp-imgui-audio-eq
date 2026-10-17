using AudioEq.Core.Dsp;

namespace AudioEq.Core.Presets;

/// <summary>Bridges APO presets into the DSP core (E2 → E3).</summary>
public static class ApoPresetExtensions
{
    /// <summary>
    /// Build one <see cref="Biquad"/> per ON filter in this preset, preserving
    /// type, frequency, gain, and Q. Preamp is intentionally NOT a biquad —
    /// callers apply <see cref="ApoPreset.PreampDb"/> as flat post-gain
    /// (engine preamp role).
    /// </summary>
    public static Biquad[] ToBiquads(this ApoPreset preset, int sampleRate)
    {
        var biquads = new Biquad[preset.Filters.Count];
        for (var i = 0; i < preset.Filters.Count; i++)
        {
            var f = preset.Filters[i];
            biquads[i] = f.Type switch
            {
                ApoFilterType.Peak => Biquad.Peaking(f.FrequencyHz, f.Q, f.GainDb, sampleRate),
                ApoFilterType.LowShelf => Biquad.LowShelf(f.FrequencyHz, f.GainDb, sampleRate),
                ApoFilterType.HighShelf => Biquad.HighShelf(f.FrequencyHz, f.GainDb, sampleRate),
                _ => throw new NotSupportedException($"Unsupported filter type {f.Type}"),
            };
        }
        return biquads;
    }

    /// <summary>Magnitude response (dB) of this preset's filters at a frequency.</summary>
    public static double MagnitudeDbAt(this ApoPreset preset, double freqHz, int sampleRate)
    {
        double mag = 1;
        foreach (var b in preset.ToBiquads(sampleRate))
        {
            mag *= b.MagnitudeAt(freqHz, sampleRate);
        }
        return 20 * Math.Log10(Math.Max(mag, 1e-12));
    }
}
