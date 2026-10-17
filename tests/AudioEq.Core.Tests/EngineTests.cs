using System.Diagnostics;
using AudioEq.Engine;
using Xunit;
using static AudioEq.Core.Tests.TestAudio;

namespace AudioEq.Core.Tests;

public class EngineTests
{
    private static readonly double[] Freqs = { 100, 300, 1000, 3000, 10000 };

    private static float[] SineStereo(double hz, int frames)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(0.25 * Math.Sin(2 * Math.PI * hz * i / SampleRate));
            samples[2 * i] = v;
            samples[2 * i + 1] = v;
        }
        return samples;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return condition();
    }

    [Fact]
    public void MockEngine_StateTransitionsPlayToStop()
    {
        using var engine = new MockEqEngine(Freqs);
        Assert.True(engine.IsAvailable);
        Assert.Equal(EngineState.Stopped, engine.State);

        engine.Play(SineStereo(440, 4096), SampleRate, 2);
        Assert.Equal(EngineState.Playing, engine.State);

        Assert.True(WaitUntil(() => engine.State == EngineState.Stopped, 5000));
        Assert.True(engine.SamplesProcessed >= 4096);
        Assert.Equal(0, engine.BufferUnderruns);
    }

    [Fact]
    public void MockEngine_StopHaltsProcessingImmediately()
    {
        using var engine = new MockEqEngine(Freqs);
        engine.Play(SineStereo(440, SampleRate * 5), SampleRate, 2);
        Thread.Sleep(50);
        engine.Stop();
        Assert.Equal(EngineState.Stopped, engine.State);
        var atStop = engine.SamplesProcessed;
        Thread.Sleep(100);
        Assert.Equal(atStop, engine.SamplesProcessed);
    }

    [Fact]
    public void MockEngine_RejectsWrongGainCount()
    {
        using var engine = new MockEqEngine(Freqs);
        Assert.Throws<ArgumentException>(() => engine.ApplyGains(new float[3]));
    }

    [Fact]
    public void OpenAlEngine_SinesToDeviceAndComplete()
    {
        using var engine = new OpenAlPlaybackEngine(Freqs);
        if (!engine.IsAvailable)
        {
            return;
        }
        var frames = SampleRate / 4;
        engine.ApplyGains(new float[] { -12, -12, -12, -12, -12 });
        engine.Play(SineStereo(440, frames), SampleRate, 2);
        Assert.Equal(EngineState.Playing, engine.State);

        Assert.True(WaitUntil(() => engine.State == EngineState.Stopped, 10000),
            "device playback did not finish in time");
        Assert.True(engine.SamplesProcessed >= frames);
        Assert.True(engine.BufferUnderruns <= 2,
            $"underruns: {engine.BufferUnderruns}");
    }

    [Fact]
    public void OpenAlEngine_EnumeratesAtLeastNoDevicesWhenUnavailableOrDefault()
    {
        using var engine = new OpenAlPlaybackEngine(Freqs);
        var devices = engine.EnumerateDevices();
        if (engine.IsAvailable)
        {
            Assert.NotEmpty(devices);
        }
        else
        {
            Assert.Empty(devices);
        }
    }
}
