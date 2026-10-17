using Silk.NET.OpenAL;

namespace AudioEq.Engine;

/// <summary>
/// Real device playback via OpenAL Soft (bundled natives, see
/// docs/audio-backend-decision.md). A refill thread unqueues processed
/// Float32 buffers, runs the live EqChain on each block, requeues.
/// Underruns are counted, not hidden.
/// </summary>
public sealed class OpenAlPlaybackEngine : EqEngineBase
{
    private const int FramesPerBlock = 1024;
    private const int PrimeBuffers = 4;

    private const int AlFormatMonoFloat32 = 0x10011;
    private const int AlFormatStereoFloat32 = 0x10010;
    private const int AlAllDevicesSpecifier = 0x1013;

    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly unsafe Device* _device;
    private readonly unsafe Context* _context;
    private readonly bool _deviceOk;
    private Thread? _thread;
    private volatile bool _run;
    private EngineState _state = EngineState.Stopped;
    private long _samples;
    private int _underruns;

    public OpenAlPlaybackEngine(IReadOnlyList<double> freqsHz, double q = 1.0)
        : base(freqsHz, q)
    {
        _al = AL.GetApi(soft: true);
        _alc = ALContext.GetApi(soft: true);
        unsafe
        {
            _device = _alc.OpenDevice(null);
            if (_device == null)
            {
                _deviceOk = false;
                return;
            }
            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                _alc.CloseDevice(_device);
                _device = null;
                _deviceOk = false;
                return;
            }
            _alc.MakeContextCurrent(_context);
            _deviceOk = true;
        }
    }

    public override string BackendName => "openal-soft";
    public override bool IsAvailable => _deviceOk;
    public override EngineState State => _state;
    public override long SamplesProcessed => Interlocked.Read(ref _samples);
    public override int BufferUnderruns => Volatile.Read(ref _underruns);

    public IReadOnlyList<string> EnumerateDevices()
    {
        if (!_deviceOk)
        {
            return [];
        }
        unsafe
        {
            var multi = _alc.GetContextProperty(_device, (GetContextString)AlAllDevicesSpecifier);
            if (!string.IsNullOrEmpty(multi))
            {
                var list = multi.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (list.Length > 0)
                {
                    return list;
                }
            }
            var fallback = _alc.GetContextProperty(_device, GetContextString.DeviceSpecifier);
            return string.IsNullOrEmpty(fallback) ? [] : new[] { fallback };
        }
    }

    public override void Play(ReadOnlyMemory<float> interleaved, int sampleRate, int channels)
    {
        if (!_deviceOk)
        {
            throw new InvalidOperationException("OpenAL device unavailable");
        }
        Stop();
        PreparePlayback(sampleRate, channels, FramesPerBlock);
        var source = interleaved.ToArray();
        _run = true;
        _state = EngineState.Playing;
        _thread = new Thread(() => RunPlayback(source, sampleRate, channels))
        {
            IsBackground = true,
            Name = "OpenAlPlaybackEngine",
        };
        _thread.Start();
    }

    public override void Stop()
    {
        _run = false;
        _thread?.Join(3000);
        _thread = null;
        _state = EngineState.Stopped;
    }

    public override void Dispose()
    {
        Stop();
        unsafe
        {
            if (_context != null)
            {
                _alc.MakeContextCurrent(null);
                _alc.DestroyContext(_context);
            }
            if (_device != null)
            {
                _alc.CloseDevice(_device);
            }
        }
        _al.Dispose();
        _alc.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RunPlayback(float[] source, int sampleRate, int channels)
    {
        unsafe
        {
            _alc.MakeContextCurrent(_context);

            var buffers = _al.GenBuffers(PrimeBuffers);
            var sourceId = _al.GenSource();
            var block = new float[FramesPerBlock * channels];
            var readFrame = 0L;
            var finished = false;

            for (var i = 0; i < PrimeBuffers; i++)
            {
                var written = FillBlock(source, channels, block.AsSpan(), FramesPerBlock, ref readFrame, out finished);
                QueueBuffer(sourceId, buffers[i], block, sampleRate, channels);
                Interlocked.Add(ref _samples, written);
                if (finished)
                {
                    break;
                }
            }
            _al.SourcePlay(sourceId);

            while (_run)
            {
                _al.GetSourceProperty(sourceId, GetSourceInteger.BuffersProcessed, out var processed);
                if (processed <= 0)
                {
                    if (!finished && !IsPlaying(sourceId))
                    {
                        Interlocked.Increment(ref _underruns);
                        _al.SourcePlay(sourceId);
                    }
                    Thread.Sleep(2);
                    continue;
                }

                for (var i = 0; i < processed; i++)
                {
                    uint bufferId;
                    _al.SourceUnqueueBuffers(sourceId, 1, &bufferId);
                    if (finished)
                    {
                        continue;
                    }
                    var written = FillBlock(source, channels, block.AsSpan(), FramesPerBlock, ref readFrame, out finished);
                    QueueBuffer(sourceId, bufferId, block, sampleRate, channels);
                    Interlocked.Add(ref _samples, written);
                }

                if (finished && !IsPlaying(sourceId))
                {
                    break;
                }
            }

            _al.SourceStop(sourceId);
            _al.DeleteSource(sourceId);
            _al.DeleteBuffers(buffers);
            _state = EngineState.Stopped;
            _alc.MakeContextCurrent(null);
        }
    }

    private bool IsPlaying(uint sourceId)
    {
        unsafe
        {
            _al.GetSourceProperty(sourceId, GetSourceInteger.SourceState, out var state);
            return (SourceState)state == SourceState.Playing;
        }
    }

    private void QueueBuffer(uint sourceId, uint bufferId, float[] block, int sampleRate, int channels)
    {
        var format = (BufferFormat)(channels == 2 ? AlFormatStereoFloat32 : AlFormatMonoFloat32);
        _al.BufferData(bufferId, format, block, sampleRate);
        unsafe
        {
            _al.SourceQueueBuffers(sourceId, 1, &bufferId);
        }
    }
}
