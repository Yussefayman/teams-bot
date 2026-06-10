using System;
using System.IO;
using System.Text;

namespace Mahdar.MediaBot.Audio;

/// <summary>
/// Streams PCM 16 kHz / 16-bit / mono samples into a RIFF/WAVE file.
/// The total length is unknown while a call is in progress, so a 44-byte header
/// with placeholder sizes is written on construction and patched on Dispose.
/// Pure and SDK-free: drive it with any Stream so it is unit-testable.
/// Format contract: media-bot/AUDIO-FORMAT.md (oracle: tools/check_wav.py).
/// </summary>
public sealed class WavWriter : IDisposable
{
    public const int SampleRate = 16000;
    public const short BitsPerSample = 16;
    public const short Channels = 1;

    private const int HeaderSize = 44;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!_stream.CanWrite) throw new ArgumentException("stream must be writable", nameof(stream));
        if (!_stream.CanSeek) throw new ArgumentException("stream must be seekable to patch the header", nameof(stream));
        _leaveOpen = leaveOpen;
        WriteHeader(dataBytes: 0);
    }

    public static WavWriter Create(string path)
        => new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));

    public long DataBytes => _dataBytes;

    /// <summary>Append raw little-endian int16 PCM bytes exactly as received from the SDK socket.</summary>
    public void WriteSamples(ReadOnlySpan<byte> pcm)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WavWriter));
        if ((pcm.Length & 1) != 0)
            throw new ArgumentException("PCM byte count must be even (16-bit samples)", nameof(pcm));
        _stream.Write(pcm);
        _dataBytes += pcm.Length;
    }

    private void WriteHeader(long dataBytes)
    {
        const int byteRate = SampleRate * Channels * BitsPerSample / 8;
        const short blockAlign = Channels * BitsPerSample / 8;

        _stream.Seek(0, SeekOrigin.Begin);
        using var w = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write((uint)(36 + dataBytes));      // ChunkSize
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16u);                          // Subchunk1Size (PCM)
        w.Write((short)1);                     // AudioFormat = PCM
        w.Write(Channels);
        w.Write((uint)SampleRate);
        w.Write((uint)byteRate);
        w.Write(blockAlign);
        w.Write(BitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write((uint)dataBytes);              // Subchunk2Size
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Flush();
        WriteHeader(_dataBytes);               // patch real sizes
        _stream.Flush();
        if (!_leaveOpen) _stream.Dispose();
    }
}
