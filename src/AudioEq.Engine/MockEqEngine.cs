namespace AudioEq.Engine;

/// <summary>
/// No-device fallback implementing the full <see cref="IEqEngine"/> contract:
/// consumes audio on a wall-clock thread through the same EqChain plumbing,
/// so the UI and conformance tests run on machines without an audio device.
/// </summary>
public sealed class MockEqEngine : EqEngineBase
{
    private const int FramesPerBlock = 1024;

    private Thread? _thread;
    private volatile bool _run;
    private EngineState _state = EngineState.Stopped;
    private long _samples;

    public MockEqEngine(IReadOnlyList<double> freqsHz, double q = 1.0)
        : base(freqsHz, q)
    {
    }

    public override string BackendName => "mock";
    public override bool IsAvailable => true;
    public override EngineState State => _state;
    public override long SamplesProcessed => Interlocked.Read(ref _samples);
    public override int BufferUnderruns => 0;

    public override void Play(ReadOnlyMemory<float> interleaved, int sampleRate, int channels)
    {
        Stop();
        PreparePlayback(sampleRate, channels, FramesPerBlock);
        var source = interleaved.ToArray();
        _run = true;
        _state = EngineState.Playing;
        _thread = new Thread(() =>
        {
            var readFrame = 0L;
            var block = new float[FramesPerBlock * channels];
            while (_run)
            {
                var written = FillBlock(source, channels, block.AsSpan(), FramesPerBlock, ref readFrame, out var finished);
                Interlocked.Add(ref _samples, written);
                if (finished)
                {
                    _state = EngineState.Stopped;
                    break;
                }
                var sleepMs = (int)(FramesPerBlock * 1000.0 / sampleRate);
                Thread.Sleep(sleepMs > 0 ? sleepMs : 1);
            }
            _state = EngineState.Stopped;
        })
        {
            IsBackground = true,
            Name = "MockEqEngine",
        };
        _thread.Start();
    }

    public override void Stop()
    {
        _run = false;
        _thread?.Join(2000);
        _thread = null;
        _state = EngineState.Stopped;
    }

    public override void Dispose() => Stop();
}
