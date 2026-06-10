using System;
using System.IO;
using System.Text;
using Mahdar.MediaBot.Audio;
using Xunit;

namespace Mahdar.MediaBot.Tests;

public class WavWriterTests
{
    private static byte[] WriteWav(byte[] pcm)
    {
        var ms = new MemoryStream();
        using (var w = new WavWriter(ms, leaveOpen: true))
        {
            // write in two chunks to exercise streaming + size patching
            w.WriteSamples(pcm.AsSpan(0, pcm.Length / 2));
            w.WriteSamples(pcm.AsSpan(pcm.Length / 2));
        }
        return ms.ToArray();
    }

    [Fact]
    public void Header_matches_canonical_16k_mono_pcm_contract()
    {
        var pcm = new byte[16000 * 2]; // 1s of silence, 16-bit mono @16k
        var bytes = WriteWav(pcm);

        Assert.Equal(44 + pcm.Length, bytes.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal((uint)(36 + pcm.Length), BitConverter.ToUInt32(bytes, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(16u, BitConverter.ToUInt32(bytes, 16));     // Subchunk1Size
        Assert.Equal((short)1, BitConverter.ToInt16(bytes, 20)); // PCM
        Assert.Equal((short)1, BitConverter.ToInt16(bytes, 22)); // mono
        Assert.Equal(16000u, BitConverter.ToUInt32(bytes, 24));  // sample rate
        Assert.Equal(32000u, BitConverter.ToUInt32(bytes, 28));  // byte rate
        Assert.Equal((short)2, BitConverter.ToInt16(bytes, 32)); // block align
        Assert.Equal((short)16, BitConverter.ToInt16(bytes, 34));// bits/sample
        Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
        Assert.Equal((uint)pcm.Length, BitConverter.ToUInt32(bytes, 40));
    }

    [Fact]
    public void Rejects_odd_byte_count()
    {
        var ms = new MemoryStream();
        using var w = new WavWriter(ms, leaveOpen: true);
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new byte[3]));
    }

    [Fact]
    public void Samples_are_written_through_unchanged()
    {
        var pcm = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var bytes = WriteWav(pcm);
        Assert.Equal(pcm, bytes[44..]);
    }

    [Fact]
    public void Cross_tool_oracle_file_is_conformant()
    {
        // Emits a file that tools/check_wav.py must accept. On the Windows VM run:
        //   python3 tools/check_wav.py <printed path>
        var path = Path.Combine(Path.GetTempPath(), "mahdar_wavwriter_xtool.wav");
        var pcm = new byte[16000];
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var w = new WavWriter(fs, leaveOpen: true))
            w.WriteSamples(pcm);
        Assert.True(File.Exists(path));
        Console.WriteLine($"cross-tool WAV written: {path}");
    }
}
