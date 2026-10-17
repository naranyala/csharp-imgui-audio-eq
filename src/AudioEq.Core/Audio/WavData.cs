namespace AudioEq.Core.Audio;

/// <summary>
/// In-memory PCM audio: interleaved float samples in [-1, 1].
/// Minimal RIFF/WAVE reader-writer (PCM16 + IEEE float32) — enough for
/// offline EQ rendering without native dependencies.
/// </summary>
public sealed class WavData
{
    public int SampleRate { get; }
    public int Channels { get; }
    public float[] Samples { get; }

    public WavData(int sampleRate, int channels, float[] interleavedSamples)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }
        if (interleavedSamples.Length % channels != 0)
        {
            throw new ArgumentException("Sample count must be a multiple of channel count");
        }

        SampleRate = sampleRate;
        Channels = channels;
        Samples = interleavedSamples;
    }

    public int Frames => Samples.Length / Channels;

    /// <summary>Deinterleaved channel data (each array length == Frames).</summary>
    public float[][] ChannelData()
    {
        var result = new float[Channels][];
        for (var c = 0; c < Channels; c++)
        {
            result[c] = new float[Frames];
            for (var i = 0; i < Frames; i++)
            {
                result[c][i] = Samples[i * Channels + c];
            }
        }
        return result;
    }

    /// <summary>Parse a RIFF/WAVE byte stream. Supports PCM 16-bit and IEEE float 32-bit.</summary>
    public static WavData Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 ||
            !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a RIFF/WAVE file");
        }

        int? format = null, sampleRate = null, channels = null, bitsPerSample = null;
        ReadOnlySpan<byte> data = default;

        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var chunkId = bytes.Slice(pos, 4);
            var chunkSize = BitConverter.ToInt32(bytes.Slice(pos + 4, 4));
            var body = pos + 8;
            if (chunkSize < 0 || body + chunkSize > bytes.Length)
            {
                throw new InvalidDataException("Corrupt chunk size");
            }

            if (chunkId.SequenceEqual("fmt "u8))
            {
                format = BitConverter.ToUInt16(bytes.Slice(body, 2));
                channels = BitConverter.ToUInt16(bytes.Slice(body + 2, 2));
                sampleRate = BitConverter.ToInt32(bytes.Slice(body + 4, 4));
                bitsPerSample = BitConverter.ToUInt16(bytes.Slice(body + 14, 2));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                data = bytes.Slice(body, chunkSize);
            }

            pos = body + chunkSize + (chunkSize & 1); // chunks are word-aligned
        }

        if (format is null || sampleRate is null || channels is null || bitsPerSample is null || data.IsEmpty)
        {
            throw new InvalidDataException("Missing fmt or data chunk");
        }

        var samples = format switch
        {
            1 when bitsPerSample == 16 => ReadPcm16(data),
            3 when bitsPerSample == 32 => ReadFloat32(data),
            _ => throw new NotSupportedException($"Unsupported WAV format {format}/{bitsPerSample}-bit"),
        };

        return new WavData(sampleRate.Value, channels.Value, samples);
    }

    private static float[] ReadPcm16(ReadOnlySpan<byte> data)
    {
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(data.Slice(i * 2, 2)) / 32768f;
        }
        return samples;
    }

    private static float[] ReadFloat32(ReadOnlySpan<byte> data)
    {
        var samples = new float[data.Length / 4];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToSingle(data.Slice(i * 4, 4));
        }
        return samples;
    }

    /// <summary>Serialize as 16-bit PCM RIFF/WAVE.</summary>
    public byte[] WritePcm16()
    {
        var dataSize = Samples.Length * 2;
        var bytes = new byte[44 + dataSize];

        "RIFF"u8.CopyTo(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), 36 + dataSize);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12));
        BitConverter.TryWriteBytes(bytes.AsSpan(16), 16);            // fmt chunk size
        BitConverter.TryWriteBytes(bytes.AsSpan(20), (ushort)1);     // PCM
        BitConverter.TryWriteBytes(bytes.AsSpan(22), (ushort)Channels);
        BitConverter.TryWriteBytes(bytes.AsSpan(24), SampleRate);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), SampleRate * Channels * 2); // byte rate
        BitConverter.TryWriteBytes(bytes.AsSpan(32), (ushort)(Channels * 2));    // block align
        BitConverter.TryWriteBytes(bytes.AsSpan(34), (ushort)16);    // bits per sample
        "data"u8.CopyTo(bytes.AsSpan(36));
        BitConverter.TryWriteBytes(bytes.AsSpan(40), dataSize);

        for (var i = 0; i < Samples.Length; i++)
        {
            var v = Math.Clamp(Samples[i], -1f, 1f);
            BitConverter.TryWriteBytes(bytes.AsSpan(44 + i * 2), (short)(v * 32767f));
        }

        return bytes;
    }
}
