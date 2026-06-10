using System.Linq;
using Mahdar.MediaBot.Audio;
using Xunit;

namespace Mahdar.MediaBot.Tests;

public class AudioRingBufferTests
{
    [Fact]
    public void Retains_in_order_when_under_capacity()
    {
        var rb = new AudioRingBuffer(8);
        rb.Write(new byte[] { 1, 2, 3 });
        Assert.Equal(3, rb.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, rb.Drain());
        Assert.Equal(0, rb.Count);
    }

    [Fact]
    public void Overwrites_oldest_when_full_keeping_freshest()
    {
        var rb = new AudioRingBuffer(4);
        rb.Write(new byte[] { 1, 2, 3, 4, 5, 6 }); // 1,2 dropped
        Assert.True(rb.IsFull);
        Assert.Equal(new byte[] { 3, 4, 5, 6 }, rb.Drain());
    }

    [Fact]
    public void ForSeconds_sizes_to_pcm_byte_rate()
    {
        var rb = AudioRingBuffer.ForSeconds(1.0);
        Assert.Equal(32000, rb.Capacity); // 16000 * 1 * 16/8
    }

    [Fact]
    public void Drain_resets_so_next_window_is_independent()
    {
        var rb = new AudioRingBuffer(4);
        rb.Write(new byte[] { 9, 9 });
        rb.Drain();
        rb.Write(new byte[] { 7 });
        Assert.Equal(new byte[] { 7 }, rb.Drain());
    }
}
