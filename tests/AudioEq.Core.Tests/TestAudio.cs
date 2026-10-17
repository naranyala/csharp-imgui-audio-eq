namespace AudioEq.Core.Tests;

internal static class TestAudio
{
    public const int SampleRate = 44100;

    /// <summary>1-second sine at <paramref name="freqHz"/>, amplitude 0.5.</summary>
    public static float[] Sine(double freqHz, double seconds = 1.0)
    {
        var n = (int)Math.Round(SampleRate * seconds);
        var samples = new float[n];
        for (var i = 0; i < n; i++)
        {
            samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * freqHz * i / SampleRate));
        }
        return samples;
    }

    /// <summary>RMS of the second half (skips filter transient).</summary>
    public static double Rms(ReadOnlySpan<float> x)
    {
        var tail = x[(x.Length / 2)..];
        double sum = 0;
        foreach (var v in tail)
        {
            sum += v * v;
        }
        return Math.Sqrt(sum / tail.Length);
    }
}
