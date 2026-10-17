namespace AudioEq.Engine;

public enum EngineState
{
    Stopped,
    Playing,
}

/// <summary>
/// Playback-backend contract (pyramid E1 / G1). UI and audition flow depend
/// only on this — backends swap at startup with zero <c>DrawUI()</c> changes.
/// Implementations must make <see cref="ApplyGains"/> safe during playback
/// and must not allocate or block on the audio path.
/// </summary>
public interface IEqEngine : IDisposable
{
    string BackendName { get; }
    bool IsAvailable { get; }
    EngineState State { get; }
    long SamplesProcessed { get; }
    int BufferUnderruns { get; }

    /// <summary>Stream <paramref name="interleaved"/> audio through the live EqChain.</summary>
    void Play(ReadOnlyMemory<float> interleaved, int sampleRate, int channels);

    /// <summary>Apply per-band gains; lock-free swap, safe mid-playback.</summary>
    void ApplyGains(ReadOnlySpan<float> gainsDb);

    void Stop();
}
