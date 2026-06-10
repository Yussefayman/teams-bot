using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Calls;

/// <summary>
/// Cross-platform call source that replays a 16 kHz/16-bit/mono WAV as if it were live
/// meeting audio, in 20 ms PCM frames. Lets the entire bot pipeline (forwarder, WAV
/// dump, lifecycle, call-ended webhook) run and be tested on macOS/Linux without the
/// Windows media SDK. Select it with CALL_SOURCE=fake and FAKE_WAV_PATH=...
/// </summary>
public sealed class FakeCallSource : ICallSource
{
    private const int FrameMs = 20;
    private const int FrameBytes = WavWriter.SampleRate / 1000 * FrameMs * 2; // 640 bytes

    private readonly string _wavPath;
    private readonly bool _realtime;
    private readonly ILogger _log;

    public CallInfo Info { get; }

    public FakeCallSource(string wavPath, CallInfo info, ILogger log, bool realtime = true)
    {
        _wavPath = wavPath;
        Info = info;
        _log = log;
        _realtime = realtime;
    }

    public async Task<CallSummary> PumpAsync(IAudioSink sink, CancellationToken ct)
    {
        var (offset, length) = LocateDataChunk(_wavPath);
        await using var fs = new FileStream(_wavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);

        var buf = new byte[FrameBytes];
        long remaining = length;
        long framesSent = 0;
        while (remaining > 0 && !ct.IsCancellationRequested)
        {
            int want = (int)Math.Min(FrameBytes, remaining);
            int read = await fs.ReadAsync(buf.AsMemory(0, want), ct);
            if (read <= 0) break;
            // pad final short frame to whole samples
            if ((read & 1) != 0) read--;
            if (read > 0) sink.OnAudioFrame(buf.AsSpan(0, read));
            remaining -= read;
            framesSent++;
            if (_realtime) await Task.Delay(FrameMs, ct);
        }

        _log.LogInformation("fake source replayed {Frames} frames from {Path}", framesSent, _wavPath);
        return new CallSummary(Info.Participants, DateTimeOffset.UtcNow);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Find the PCM 'data' chunk in a RIFF file (skips any chunks before it).</summary>
    private static (long offset, long length) LocateDataChunk(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var r = new BinaryReader(fs);
        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        r.ReadUInt32();
        if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");
        while (fs.Position < fs.Length)
        {
            var id = new string(r.ReadChars(4));
            uint size = r.ReadUInt32();
            if (id == "data") return (fs.Position, size);
            fs.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("no data chunk found");
    }
}

/// <summary>Creates FakeCallSource instances with a synthetic roster for dev/test on Mac.</summary>
public sealed class FakeCallSourceFactory : ICallSourceFactory
{
    private readonly string _wavPath;
    private readonly bool _realtime;
    private readonly ILogger _log;

    public FakeCallSourceFactory(string wavPath, ILogger log, bool realtime = true)
    {
        _wavPath = wavPath;
        _log = log;
        _realtime = realtime;
    }

    public Task<ICallSource> JoinAsync(string joinUrl, string meetingId, CancellationToken ct)
    {
        var participants = new List<Participant>
        {
            new("يوسف أيمن", "00000000-0000-0000-0000-000000000001", "youssef@dev.onmicrosoft.com"),
            new("Sara Al-Otaibi", "00000000-0000-0000-0000-000000000002"),
        };
        var info = new CallInfo(
            MeetingId: meetingId,
            JoinUrl: joinUrl,
            ChatThreadId: "19:fake_meeting@thread.v2",
            OrganizerId: participants[0].AadId,
            Participants: participants,
            StartedAt: DateTimeOffset.UtcNow);
        ICallSource src = new FakeCallSource(_wavPath, info, _log, _realtime);
        return Task.FromResult(src);
    }
}
