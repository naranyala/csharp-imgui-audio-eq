using AudioEq.Core.Audio;
using Xunit;
using static AudioEq.Core.Tests.TestAudio;

namespace AudioEq.Core.Tests;

public class WavDataTests
{
    private static byte[] MakeWav(ushort format, ushort channels, int sampleRate, ushort bits, byte[] data, byte[]? extraChunk = null)
    {
        var fmtSize = 16;
        var extraLen = extraChunk?.Length ?? 0;
        var total = 12 + 8 + fmtSize + extraLen + 8 + data.Length;
        var bytes = new byte[total];

        "RIFF"u8.CopyTo(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), total - 8);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));

        "fmt "u8.CopyTo(bytes.AsSpan(12));
        BitConverter.TryWriteBytes(bytes.AsSpan(16), fmtSize);
        BitConverter.TryWriteBytes(bytes.AsSpan(20), format);
        BitConverter.TryWriteBytes(bytes.AsSpan(22), channels);
        BitConverter.TryWriteBytes(bytes.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), sampleRate * channels * bits / 8);
        BitConverter.TryWriteBytes(bytes.AsSpan(32), (ushort)(channels * bits / 8));
        BitConverter.TryWriteBytes(bytes.AsSpan(34), bits);

        var pos = 12 + 8 + fmtSize;
        if (extraChunk is not null)
        {
            extraChunk.CopyTo(bytes, pos);
            pos += extraChunk.Length;
        }

        "data"u8.CopyTo(bytes.AsSpan(pos));
        BitConverter.TryWriteBytes(bytes.AsSpan(pos + 4), data.Length);
        data.CopyTo(bytes, pos + 8);

        return bytes;
    }

    [Fact]
    public void Pcm16RoundTripWithinQuantizationError()
    {
        var samples = new float[257];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (i - 128) / 160f; // spans roughly -0.8 .. +0.8
        }
        var wav = new WavData(SampleRate, 1, samples);

        var reread = WavData.Read(wav.WritePcm16());

        Assert.Equal(SampleRate, reread.SampleRate);
        Assert.Equal(1, reread.Channels);
        Assert.Equal(samples.Length, reread.Samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.True(Math.Abs(reread.Samples[i] - samples[i]) < 1e-4,
                $"index {i}: {reread.Samples[i]} vs {samples[i]}");
        }
    }

    [Fact]
    public void ReadsFloat32Format()
    {
        var data = new byte[12];
        BitConverter.TryWriteBytes(data.AsSpan(0), 0.0f);
        BitConverter.TryWriteBytes(data.AsSpan(4), 0.5f);
        BitConverter.TryWriteBytes(data.AsSpan(8), -0.5f);
        var bytes = MakeWav(format: 3, channels: 1, sampleRate: 44100, bits: 32, data: data);

        var wav = WavData.Read(bytes);

        Assert.Equal(3, wav.Samples.Length);
        Assert.Equal(0.0f, wav.Samples[0]);
        Assert.Equal(0.5f, wav.Samples[1]);
        Assert.Equal(-0.5f, wav.Samples[2]);
    }

    [Fact]
    public void WriteClampsOutOfRangeSamples()
    {
        var wav = new WavData(SampleRate, 1, new[] { 2.0f, -3.0f, 0.0f });
        var reread = WavData.Read(wav.WritePcm16());

        Assert.InRange(reread.Samples[0], 0.99f, 1.0f);
        Assert.InRange(reread.Samples[1], -1.0f, -0.99f);
        Assert.Equal(0.0f, reread.Samples[2], 3);
    }

    [Fact]
    public void NotRiffThrowsInvalidData()
    {
        var bytes = new byte[64];
        "NOTA"u8.CopyTo(bytes);
        Assert.Throws<InvalidDataException>(() => WavData.Read(bytes));
    }

    [Fact]
    public void MissingDataChunkThrowsInvalidData()
    {
        // fmt only, no data chunk
        var bytes = MakeWav(format: 1, channels: 1, sampleRate: 44100, bits: 16, data: Array.Empty<byte>()).ToArray();
        // truncate the empty data chunk entirely (keep fmt only)
        var truncated = bytes.AsSpan(0, 12 + 8 + 16).ToArray();
        Assert.Throws<InvalidDataException>(() => WavData.Read(truncated));
    }

    [Fact]
    public void UnsupportedFormatThrowsNotSupported()
    {
        var bytes = MakeWav(format: 2, channels: 1, sampleRate: 44100, bits: 8, data: new byte[8]);
        Assert.Throws<NotSupportedException>(() => WavData.Read(bytes));
    }

    [Fact]
    public void OddSizedUnknownChunkBeforeDataIsSkipped()
    {
        // LIST chunk with odd size (3) — parser must honor the pad byte.
        var extra = "LISTxyz"u8.ToArray(); // id(4) + size(3 body bytes) = 7 ... build properly below
        extra = new byte[8 + 3 + 1];
        "LIST"u8.CopyTo(extra);
        BitConverter.TryWriteBytes(extra.AsSpan(4), 3);
        extra[8] = (byte)'x';
        extra[9] = (byte)'y';
        extra[10] = (byte)'z';
        extra[11] = 0; // pad byte

        var samples = new float[] { 0.25f, -0.25f, 0.75f };
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(data.AsSpan(i * 2), (short)(samples[i] * 32767f));
        }

        var bytes = MakeWav(format: 1, channels: 1, sampleRate: 44100, bits: 16, data: data, extraChunk: extra);
        var wav = WavData.Read(bytes);

        Assert.Equal(3, wav.Samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.True(Math.Abs(wav.Samples[i] - samples[i]) < 1e-4, $"index {i}");
        }
    }

    [Fact]
    public void StereoWriteReadPreservesChannelAssignment()
    {
        var left = Sine(1000, seconds: 0.05);
        var right = new float[left.Length]; // silence
        var interleaved = new float[left.Length * 2];
        for (var i = 0; i < left.Length; i++)
        {
            interleaved[i * 2] = left[i];
        }
        var wav = new WavData(SampleRate, 2, interleaved);

        var reread = WavData.Read(wav.WritePcm16());
        Assert.Equal(2, reread.Channels);
        Assert.Equal(wav.Frames, reread.Frames);

        var channels = reread.ChannelData();
        Assert.True(Rms(channels[0]) > 0.2);
        Assert.True(Rms(channels[1]) < 1e-3);
    }

    [Fact]
    public void SampleCountMustAlignWithChannelCount()
    {
        Assert.Throws<ArgumentException>(() => new WavData(SampleRate, 2, new float[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavData(0, 1, Array.Empty<float>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavData(SampleRate, 0, Array.Empty<float>()));
    }
}
