using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioEq.Core.Presets;

public enum ApoFilterType
{
    Peak,
    LowShelf,
    HighShelf,
}

public sealed record ApoFilter(ApoFilterType Type, double FrequencyHz, double GainDb, double Q = 1.0);

/// <summary>
/// Equalizer APO <c>.txt</c> preset format: parse + serialize.
/// The APO text format is the closest thing the EQ world has to a lingua
/// franca (Equalizer APO, Peace, Neon Equalizer, AutoEQ exports, Resonance
/// all read it). Supporting it gives this app an instant preset library.
/// Supported subset (v1):
/// <code>
/// Preamp: -6 dB
/// Filter 1: ON PK Fc 1000 Hz Gain 3.5 dB Q 1.00
/// Filter 2: ON LS Fc 100 Hz Gain 2.0 dB
/// Filter 3: ON HS Fc 10000 Hz Gain -1.5 dB
/// </code>
/// OFF lines are skipped; PK/LS/HS supported; other ON filter types throw
/// <see cref="FormatException"/> so callers can surface "unsupported";
/// <c>#</c>/<c>;</c> comments and blank lines ignored; Q optional (defaults 1.0).
/// </summary>
public sealed class ApoPreset
{
    public string Name { get; }
    public double PreampDb { get; }
    public IReadOnlyList<ApoFilter> Filters { get; }

    public ApoPreset(string name, double preampDb = 0, IReadOnlyList<ApoFilter>? filters = null)
    {
        Name = name;
        PreampDb = preampDb;
        Filters = filters ?? Array.Empty<ApoFilter>();
    }

    /// <summary>
    /// Map this preset onto <paramref name="targetFreqs"/> band gains.
    /// Approximation (documented, v1): each filter contributes its full gain to
    /// the single nearest target band (log-frequency distance). With
    /// <paramref name="includePreamp"/> (default) the preamp is added on top
    /// of every band; pass false and apply <see cref="PreampDb"/> separately
    /// via the engine preamp (preferred — avoids wasting band headroom).
    /// </summary>
    public double[] ToGains(IReadOnlyList<double> targetFreqs, bool includePreamp = true)
    {
        var gains = new double[targetFreqs.Count];
        var baseGain = includePreamp ? PreampDb : 0;
        for (var i = 0; i < gains.Length; i++)
        {
            gains[i] = baseGain;
        }

        foreach (var filter in Filters)
        {
            var nearest = 0;
            var best = double.PositiveInfinity;
            var target = Math.Log10(filter.FrequencyHz);
            for (var i = 0; i < targetFreqs.Count; i++)
            {
                var d = Math.Abs(Math.Log10(targetFreqs[i]) - target);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }
            gains[nearest] += filter.GainDb;
        }

        return gains;
    }

    /// <summary>Serialize back to APO <c>.txt</c> (one PK line per band + preamp).</summary>
    public string ToApoText(IReadOnlyList<double> bandFreqs, IReadOnlyList<double> gainsDb)
    {
        var buf = new StringBuilder();
        buf.Append("# Exported by audio_eq (").Append(Name).AppendLine(")");
        if (PreampDb != 0)
        {
            buf.Append("Preamp: ").Append(Fmt(PreampDb)).AppendLine(" dB");
        }
        for (var i = 0; i < bandFreqs.Count; i++)
        {
            buf.Append("Filter ").Append(i + 1).Append(": ON PK Fc ")
               .Append(Fmt(bandFreqs[i])).Append(" Hz Gain ")
               .Append(Fmt(gainsDb[i])).Append(" dB Q 1.00")
               .AppendLine();
        }
        return buf.ToString();
    }

    private static string Fmt(double v)
        => v == Math.Round(v)
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>Parse APO <c>.txt</c> text into an <see cref="ApoPreset"/>.</summary>
public static class ApoParser
{
    private static readonly Regex PreampRe = new(
        @"^\s*preamp\s*:\s*([+-]?\d+(?:\.\d+)?)\s*dB",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FilterRe = new(
        @"^\s*filter\s+\d+\s*:\s*(ON|OFF)\s+(PK|LS|HS)\s+" +
        @"Fc\s+(\d+(?:\.\d+)?)\s*Hz\s+Gain\s+([+-]?\d+(?:\.\d+)?)\s*dB" +
        @"(?:\s+Q\s+(\d+(?:\.\d+)?))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UnsupportedRe = new(
        @"^\s*filter\s+\d+\s*:\s*ON\s+([A-Z]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ApoPreset Parse(string text, string name = "Imported")
    {
        double preampDb = 0;
        var filters = new List<ApoFilter>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            var preamp = PreampRe.Match(line);
            if (preamp.Success)
            {
                preampDb = double.Parse(preamp.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            var filter = FilterRe.Match(line);
            if (filter.Success)
            {
                if (filter.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    filters.Add(ParseFilter(filter, line));
                }
                continue; // OFF lines are skipped
            }

            var unsupported = UnsupportedRe.Match(line);
            if (unsupported.Success)
            {
                throw new FormatException($"Unsupported APO filter type \"{unsupported.Groups[1].Value}\": {line}");
            }
            // Unknown lines (GraphicEQ, Device, marker-less comments…) are
            // ignored in v1 rather than failing the whole import.
        }

        return new ApoPreset(name, preampDb, filters);
    }

    private static ApoFilter ParseFilter(Match m, string line)
    {
        var type = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "PK" => ApoFilterType.Peak,
            "LS" => ApoFilterType.LowShelf,
            "HS" => ApoFilterType.HighShelf,
            _ => throw new FormatException($"Unreachable filter type: {line}"),
        };

        var q = m.Groups[5].Success
            ? double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture)
            : 1.0;

        return new ApoFilter(
            type,
            double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
            q);
    }
}
