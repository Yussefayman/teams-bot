using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Audio;

/// <summary>
/// IAudioSink that streams PCM frames to the STT service over a WebSocket and, when
/// DUMP_AUDIO is set, also writes them to a per-call .wav (the M1 proof). On socket
/// loss it buffers up to ReconnectBufferSeconds of audio in a ring buffer and
/// reconnects with exponential backoff, replaying the buffer after a session_resume
/// (plan §4.3). Uses only System.Net.WebSockets — no Graph SDK — so it is testable
/// against a local echo WebSocket server.
/// </summary>
public sealed class SttWebSocketForwarder : IAudioSink
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly BotOptions _opts;
    private readonly ILogger _log;
    private readonly Uri _ingestUri;
    private readonly string _meetingId;
    private readonly AudioRingBuffer _ring;
    private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    private ClientWebSocket? _ws;
    private WavWriter? _wav;
    private SessionStart? _start;
    private Task? _pump;

    public SttWebSocketForwarder(BotOptions opts, string meetingId, ILogger log)
    {
        _opts = opts;
        _log = log;
        _meetingId = meetingId;
        _ring = AudioRingBuffer.ForSeconds(opts.ReconnectBufferSeconds);
        var baseUrl = opts.SttWsBaseUrl.TrimEnd('/');
        _ingestUri = new Uri($"{baseUrl}/ingest/{Uri.EscapeDataString(meetingId)}");
    }

    public async Task StartAsync(SessionStart start, CancellationToken ct)
    {
        _start = start;
        if (_opts.DumpAudio)
        {
            Directory.CreateDirectory(_opts.DumpDir);
            var path = Path.Combine(_opts.DumpDir, $"dump_{_meetingId}.wav");
            _wav = WavWriter.Create(path);
            _log.LogInformation("DUMP_AUDIO on: writing {Path}", path);
        }
        await ConnectAndHandshakeAsync(isResume: false, ct);
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public void OnAudioFrame(ReadOnlySpan<byte> pcm)
    {
        _wav?.WriteSamples(pcm);
        var copy = pcm.ToArray();           // SDK reuses its buffer; copy before queueing
        if (!_frames.Writer.TryWrite(copy))
            _log.LogWarning("frame queue rejected a write for {MeetingId}", _meetingId);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        while (await _frames.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_frames.Reader.TryRead(out var frame))
            {
                try
                {
                    if (_ws is { State: WebSocketState.Open })
                    {
                        await _ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, ct);
                    }
                    else
                    {
                        _ring.Write(frame);
                        await ReconnectAsync(ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "send failed; buffering and reconnecting");
                    _ring.Write(frame);
                    await ReconnectAsync(ct);
                }
            }
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(250);
        var max = TimeSpan.FromSeconds(10);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndHandshakeAsync(isResume: true, ct);
                var buffered = _ring.Drain();
                if (buffered.Length > 0)
                    await _ws!.SendAsync(new ArraySegment<byte>(buffered), WebSocketMessageType.Binary, true, ct);
                _log.LogInformation("reconnected; replayed {Bytes} buffered bytes", buffered.Length);
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "reconnect attempt failed; retrying in {Delay}", delay);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(max.TotalMilliseconds, delay.TotalMilliseconds * 2));
            }
        }
    }

    private async Task ConnectAndHandshakeAsync(bool isResume, CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_ingestUri, ct);
        if (isResume)
            await SendJsonAsync(SessionResume.Create(_meetingId), ct);
        else if (_start is not null)
            await SendJsonAsync(_start, ct);
    }

    public Task SendRosterAsync(RosterUpdate roster, CancellationToken ct) => SendJsonAsync(roster, ct);

    private async Task SendJsonAsync<T>(T message, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open }) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public async Task EndAsync(CancellationToken ct)
    {
        try { await SendJsonAsync(SessionEnd.Create(_meetingId), ct); }
        catch (Exception ex) { _log.LogWarning(ex, "failed to send session_end"); }
    }

    public async ValueTask DisposeAsync()
    {
        _frames.Writer.TryComplete();
        _cts.Cancel();
        if (_pump is not null)
        {
            try { await _pump; } catch { /* shutdown */ }
        }
        _wav?.Dispose();
        if (_ws is { State: WebSocketState.Open })
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
            catch { /* best effort */ }
        }
        _ws?.Dispose();
        _cts.Dispose();
    }
}
