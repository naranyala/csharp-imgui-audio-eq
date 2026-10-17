using AudioEq.Core.Dsp;

namespace AudioEq.Engine;

/// <summary>
/// Shared band/gain/channel plumbing for every backend: lock-free chain
/// swap on gain changes (state-preserving <see cref="EqChain.SetGains"/>),
/// mono/stereo interleave handling with scratch buffers owned by the engine.
/// </summary>
public abstract class EqEngineBase : IEqEngine
{
    private readonly double[] _freqsHz;
    private readonly double _q;
    private readonly object _gainsLock = new();
    private readonly float[] _gainsDb;
    private volatile ChainPair? _chains;
    private int _channels;
    private float[] _scratchL = [];
    private float[] _scratchR = [];

    protected sealed class ChainPair
    {
        public ChainPair(EqChain left, EqChain? right)
        {
            Left = left;
            Right = right;
        }

        public EqChain Left { get; }
        public EqChain? Right { get; }
    }

    protected EqEngineBase(IReadOnlyList<double> freqsHz, double q = 1.0)
    {
        if (freqsHz.Count == 0)
        {
            throw new ArgumentException("Need at least one band", nameof(freqsHz));
        }
        _freqsHz = freqsHz.ToArray();
        _q = q;
        _gainsDb = new float[_freqsHz.Length];
    }

    public int BandCount => _freqsHz.Length;

    public abstract string BackendName { get; }
    public abstract bool IsAvailable { get; }
    public abstract EngineState State { get; }
    public abstract long SamplesProcessed { get; }
    public abstract int BufferUnderruns { get; }
    public abstract void Play(ReadOnlyMemory<float> interleaved, int sampleRate, int channels);
    public abstract void Stop();
    public abstract void Dispose();

    public void ApplyGains(ReadOnlySpan<float> gainsDb)
    {
        if (gainsDb.Length != _freqsHz.Length)
        {
            throw new ArgumentException($"Expected {_freqsHz.Length} band gains", nameof(gainsDb));
        }
        lock (_gainsLock)
        {
            gainsDb.CopyTo(_gainsDb);
            var chains = _chains;
            if (chains != null)
            {
                PushGainsToChains(chains);
            }
        }
    }

    protected void PreparePlayback(int sampleRate, int channels, int framesPerBlock)
    {
        if (channels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "Mono or stereo only");
        }
        lock (_gainsLock)
        {
            var gains = ToDoubles();
            var left = new EqChain(_freqsHz, gains, sampleRate, _q);
            var right = channels == 2 ? new EqChain(_freqsHz, gains, sampleRate, _q) : null;
            var pair = new ChainPair(left, right);
            _chains = pair;
        }
        _channels = channels;
        _scratchL = channels == 2 ? new float[framesPerBlock] : [];
        _scratchR = channels == 2 ? new float[framesPerBlock] : [];
    }

    protected void ProcessInterleavedBlock(Span<float> interleaved, int frames)
    {
        var chains = _chains;
        if (chains == null)
        {
            return;
        }
        if (_channels == 1)
        {
            chains.Left.ProcessInPlace(interleaved[..frames]);
            return;
        }
        for (var f = 0; f < frames; f++)
        {
            _scratchL[f] = interleaved[2 * f];
            _scratchR[f] = interleaved[2 * f + 1];
        }
        chains.Left.ProcessInPlace(_scratchL.AsSpan(0, frames));
        chains.Right!.ProcessInPlace(_scratchR.AsSpan(0, frames));
        for (var f = 0; f < frames; f++)
        {
            interleaved[2 * f] = _scratchL[f];
            interleaved[2 * f + 1] = _scratchR[f];
        }
    }

    protected int FillBlock(
        ReadOnlyMemory<float> source, int channels, Span<float> block, int frames,
        ref long readFrame, out bool finished)
    {
        finished = false;
        var src = source.Span;
        var totalFrames = channels == 0 ? 0 : src.Length / channels;
        var written = 0;
        for (var f = 0; f < frames; f++)
        {
            if (readFrame >= totalFrames)
            {
                block.Slice(f * channels, (frames - f) * channels).Clear();
                break;
            }
            for (var c = 0; c < channels; c++)
            {
                block[f * channels + c] = src[(int)readFrame * channels + c];
            }
            readFrame++;
            written++;
        }
        ProcessInterleavedBlock(block, frames);
        finished = readFrame >= totalFrames;
        return written;
    }

    private double[] ToDoubles()
    {
        var gains = new double[_gainsDb.Length];
        for (var i = 0; i < _gainsDb.Length; i++)
        {
            gains[i] = _gainsDb[i];
        }
        return gains;
    }

    private void PushGainsToChains(ChainPair chains)
    {
        var gains = ToDoubles();
        chains.Left.SetGains(gains);
        chains.Right?.SetGains(gains);
    }
}
