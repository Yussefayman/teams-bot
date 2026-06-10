using System;

namespace Mahdar.MediaBot.Audio;

/// <summary>
/// Fixed-capacity byte ring buffer used to retain recent PCM while the STT
/// WebSocket is reconnecting (plan §4.3). When full, the oldest bytes are
/// overwritten — we keep the freshest ~N seconds, never block the audio thread.
/// Pure and SDK-free; unit-testable.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly byte[] _buf;
    private int _start;     // index of oldest byte
    private int _count;     // valid bytes

    public AudioRingBuffer(int capacityBytes)
    {
        if (capacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBytes));
        _buf = new byte[capacityBytes];
    }

    public static AudioRingBuffer ForSeconds(double seconds)
        => new((int)(seconds * WavWriter.SampleRate * WavWriter.Channels * WavWriter.BitsPerSample / 8));

    public int Capacity => _buf.Length;
    public int Count => _count;
    public bool IsFull => _count == _buf.Length;

    public void Write(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            int idx = (_start + _count) % _buf.Length;
            _buf[idx] = b;
            if (_count == _buf.Length)
                _start = (_start + 1) % _buf.Length;   // overwrite oldest
            else
                _count++;
        }
    }

    /// <summary>Copy all retained bytes (oldest first) and clear the buffer.</summary>
    public byte[] Drain()
    {
        var outArr = new byte[_count];
        for (int i = 0; i < _count; i++)
            outArr[i] = _buf[(_start + i) % _buf.Length];
        _start = 0;
        _count = 0;
        return outArr;
    }
}
