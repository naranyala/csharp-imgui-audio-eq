using AudioEq.Core.Dsp;
using AudioEq.Core.Presets;
using Xunit;
using static AudioEq.Core.Tests.TestAudio;

namespace AudioEq.Core.Tests;

public class ApoPresetTests
{
    private const string SampleApo = """
        # AutoEQ-style headphone correction
        Preamp: -5.5 dB
        Filter 1: ON PK Fc 60 Hz Gain 4.0 dB Q 1.00
        Filter 2: ON PK Fc 230 Hz Gain -1.5 dB Q 1.20
        Filter 3: OFF PK Fc 910 Hz Gain 9.9 dB Q 1.00
        Filter 4: ON LS Fc 100 Hz Gain 2.0 dB
        Filter 5: ON HS Fc 10000 Hz Gain -2.5 dB Q 0.90
        ; a comment line
        """;

    private static readonly double[] AppBands = { 60, 230, 910, 3600, 14000 };

    [Fact]
    public void ParsesPreampFiltersSkipsOffAndComments()
    {
        var preset = ApoParser.Parse(SampleApo, name: "Test HP");

        Assert.Equal("Test HP", preset.Name);
        Assert.Equal(-5.5, preset.PreampDb);
        Assert.Equal(4, preset.Filters.Count); // OFF filter excluded
        Assert.Equal(60, preset.Filters[0].FrequencyHz);
        Assert.Equal(4.0, preset.Filters[0].GainDb);
        Assert.Equal(1.0, preset.Filters[0].Q);
        Assert.Equal(ApoFilterType.LowShelf, preset.Filters[2].Type);
        Assert.Equal(1.0, preset.Filters[2].Q); // defaulted
        Assert.Equal(ApoFilterType.HighShelf, preset.Filters[3].Type);
    }

    [Fact]
    public void ToGainsMapsEachFilterToNearestBandPlusPreamp()
    {
        var preset = ApoParser.Parse(SampleApo);
        var gains = preset.ToGains(AppBands);

        // preamp everywhere; 60Hz PK -> band 0; 230 PK -> band 1;
        // 100Hz LS nearest is 60 (log-distance); 10k HS nearest is 14k.
        Assert.Equal(-5.5 + 4.0 + 2.0, gains[0], 9);
        Assert.Equal(-5.5 - 1.5, gains[1], 9);
        Assert.Equal(-5.5, gains[2], 9);
        Assert.Equal(-5.5, gains[3], 9);
        Assert.Equal(-5.5 - 2.5, gains[4], 9);
    }

    [Fact]
    public void ToGainsWithoutPreampLeavesHeadroomForEnginePreamp()
    {
        var preset = ApoParser.Parse(SampleApo);
        var gains = preset.ToGains(AppBands, includePreamp: false);

        Assert.Equal(4.0 + 2.0, gains[0], 9);
        Assert.Equal(-1.5, gains[1], 9);
        Assert.Equal(-2.5, gains[4], 9);
        Assert.Equal(-5.5, preset.PreampDb);
    }

    [Fact]
    public void ExportRoundTripsThroughTheParser()
    {
        var gains = new[] { 6.0, 4.0, 0.0, -1.5, 3.0 };
        var preset = new ApoPreset(name: "RoundTrip");
        var text = preset.ToApoText(AppBands, gains);
        var reparsed = ApoParser.Parse(text, name: "RoundTrip");

        Assert.Equal(5, reparsed.Filters.Count);
        Assert.Equal(gains, reparsed.ToGains(AppBands));
    }

    [Fact]
    public void UnsupportedOnFilterTypeThrows()
    {
        Assert.Throws<FormatException>(() =>
            ApoParser.Parse("Filter 1: ON LP Fc 500 Hz Gain 3 dB"));
    }

    [Fact]
    public void EmptyFileYieldsEmptyPresetNotAnError()
    {
        var preset = ApoParser.Parse("# nothing here\n");
        Assert.Empty(preset.Filters);
        Assert.Equal(0, preset.PreampDb);
    }

    [Fact]
    public void ParsesCaseInsensitivelyAndWithCrlfLineEndings()
    {
        var text = "PREAMP: -3 dB\r\nFilter 1: ON pk fc 500 hz gain 2.5 db q 0.90\r\n";
        var preset = ApoParser.Parse(text);

        Assert.Equal(-3.0, preset.PreampDb);
        var filter = Assert.Single(preset.Filters);
        Assert.Equal(ApoFilterType.Peak, filter.Type);
        Assert.Equal(500, filter.FrequencyHz);
        Assert.Equal(2.5, filter.GainDb);
        Assert.Equal(0.9, filter.Q);
    }

    [Fact]
    public void LastPreampLineWins()
    {
        var preset = ApoParser.Parse("Preamp: -2 dB\nPreamp: -5 dB\n");
        Assert.Equal(-5.0, preset.PreampDb);
    }

    [Fact]
    public void ToApoTextOmitsZeroPreampAndFormatsNumbers()
    {
        var preset = new ApoPreset(name: "Fmt");
        var text = preset.ToApoText(new[] { 60.0, 1000.5 }, new[] { 6.0, -1.5 });

        Assert.DoesNotContain("Preamp", text);
        Assert.Contains("Fc 60 Hz Gain 6 dB", text);
        Assert.Contains("Fc 1000.5 Hz Gain -1.5 dB", text);

        var withPreamp = new ApoPreset(name: "Fmt", preampDb: -5.5);
        Assert.Contains("Preamp: -5.5 dB", withPreamp.ToApoText(new[] { 60.0 }, new[] { 0.0 }));
    }

    [Fact]
    public void UnknownLinesLikeGraphicEqAreIgnoredNotFatal()
    {
        var preset = ApoParser.Parse("""
            GraphicEQ: 20 -6; 30 -5; 40 -4
            Device: Speakers
            Filter 1: ON PK Fc 100 Hz Gain 1 dB Q 1.00
            """);

        var filter = Assert.Single(preset.Filters);
        Assert.Equal(100, filter.FrequencyHz);
    }

    [Fact]
    public void UnsupportedNotchOnTypeThrows()
    {
        Assert.Throws<FormatException>(() =>
            ApoParser.Parse("Filter 1: ON NO Fc 1000 Hz Gain 3 dB Q 1.00"));
    }

    [Fact]
    public void EmptyFiltersToGainsReturnsBaseGainOnly()
    {
        var preset = new ApoPreset(name: "Empty", preampDb: -4);
        var withPreamp = preset.ToGains(AppBands);
        Assert.All(withPreamp, g => Assert.Equal(-4.0, g, 9));

        var without = preset.ToGains(AppBands, includePreamp: false);
        Assert.All(without, g => Assert.Equal(0.0, g, 9));
    }

    [Fact]
    public void ToBiquadsProducesFilterWhoseMagnitudeMatchesDeclaredGain()
    {
        var preset = ApoParser.Parse("Filter 1: ON PK Fc 1000 Hz Gain 6 dB Q 1.00");

        // +6 dB at center => ~x2 amplitude => 6 dB
        Assert.InRange(preset.MagnitudeDbAt(1000, SampleRate), 5.9, 6.1);
        // Far off-center the band is nearly gone.
        Assert.True(Math.Abs(preset.MagnitudeDbAt(100, SampleRate)) < 1.5);

        var biquads = preset.ToBiquads(SampleRate);
        var biquad = Assert.Single(biquads);
        Assert.Equal(BiquadType.Peaking, biquad.Type);
        Assert.Equal(1000, biquad.FreqHz);
        Assert.Equal(1.0, biquad.Q, 9);
        Assert.Equal(6.0, biquad.GainDb, 9);
    }

    [Fact]
    public void ShelfFiltersMapToMatchingBiquadTypes()
    {
        var preset = ApoParser.Parse("""
            Filter 1: ON LS Fc 120 Hz Gain 4 dB
            Filter 2: ON HS Fc 9000 Hz Gain -3 dB Q 0.80
            """);

        var biquads = preset.ToBiquads(SampleRate);
        Assert.Equal(2, biquads.Length);
        Assert.Equal(BiquadType.LowShelf, biquads[0].Type);
        Assert.Equal(120, biquads[0].FreqHz);
        Assert.Equal(4.0, biquads[0].GainDb, 9);
        Assert.Equal(BiquadType.HighShelf, biquads[1].Type);
        Assert.Equal(-3.0, biquads[1].GainDb, 9);
    }
}
