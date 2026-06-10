using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mahdar.MediaBot.Tests;

/// <summary>
/// Runs the whole media-bot pipeline on this machine (no Windows, no Graph SDK):
/// FakeCallSource -> CallRunner -> SttWebSocketForwarder -> in-process WS capture
/// server, plus the DUMP_AUDIO WAV path and the call-ended notifier. This is the
/// M1 happy path, exercised end-to-end on macOS/Linux.
/// </summary>
public class PipelineIntegrationTests
{
    private sealed class CapturingNotifier : ICallEndedNotifier
    {
        public CallEndedEvent? Received;
        public Task NotifyAsync(CallEndedEvent ev, CancellationToken ct)
        {
            Received = ev;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FakeCall_streams_audio_and_lifecycle_to_stt_and_dumps_conformant_wav()
    {
        // --- in-process STT WebSocket capture server ---
        var textMessages = new ConcurrentQueue<string>();
        long binaryBytes = 0;
        var firstFrameSeen = new TaskCompletionSource();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/ingest/{meetingId}", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var buf = new byte[8192];
            while (ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }
                if (res.MessageType == WebSocketMessageType.Text)
                    textMessages.Enqueue(Encoding.UTF8.GetString(buf, 0, res.Count));
                else
                {
                    Interlocked.Add(ref binaryBytes, res.Count);
                    firstFrameSeen.TrySetResult();
                }
            }
        });
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;

        try
        {
            // --- a small conformant WAV fixture (0.25s of non-zero PCM) ---
            var tmp = Directory.CreateTempSubdirectory("mahdar_pipeline_");
            var wavPath = Path.Combine(tmp.FullName, "input.wav");
            int samples = WavWriter.SampleRate / 4;
            var pcm = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                short v = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / WavWriter.SampleRate));
                pcm[2 * i] = (byte)(v & 0xff);
                pcm[2 * i + 1] = (byte)((v >> 8) & 0xff);
            }
            using (var w = WavWriter.Create(wavPath)) w.WriteSamples(pcm);

            // --- run the real pipeline against the capture server ---
            var opts = new BotOptions
            {
                SttWsBaseUrl = $"ws://127.0.0.1:{port}",
                DumpAudio = true,
                DumpDir = Path.Combine(tmp.FullName, "dumps"),
                ReconnectBufferSeconds = 5,
            };
            var notifier = new CapturingNotifier();
            var runner = new CallRunner(
                new FakeCallSourceFactory(wavPath, NullLogger.Instance, realtime: false),
                new SttSinkFactory(opts, NullLoggerFactory.Instance),
                notifier,
                NullLogger<CallRunner>.Instance);

            await runner.RunAsync("https://teams.example/meet/xyz", "mtg-itest-001", CancellationToken.None);

            // give the capture server a moment to drain the socket
            await Task.WhenAny(firstFrameSeen.Task, Task.Delay(2000));
            await Task.Delay(200);

            // --- assertions ---
            Assert.True(binaryBytes > 0, "STT server received no audio frames");
            Assert.Equal(pcm.Length, (int)binaryBytes); // all PCM forwarded, nothing lost

            string allText = string.Join("\n", textMessages);
            Assert.Contains("session_start", allText);
            Assert.Contains("mtg-itest-001", allText);
            Assert.Contains("session_end", allText);

            var start = JsonSerializer.Deserialize<JsonElement>(textMessages.First());
            Assert.Equal("session_start", start.GetProperty("type").GetString());
            Assert.Equal("19:fake_meeting@thread.v2", start.GetProperty("chatThreadId").GetString());

            Assert.NotNull(notifier.Received);
            Assert.Equal("mtg-itest-001", notifier.Received!.MeetingId);
            Assert.Equal(2, notifier.Received.Participants.Count);

            // DUMP_AUDIO wrote a conformant WAV
            var dump = Path.Combine(opts.DumpDir, "dump_mtg-itest-001.wav");
            Assert.True(File.Exists(dump), "DUMP_AUDIO file was not written");
            var head = File.ReadAllBytes(dump);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(head, 0, 4));
            Assert.Equal(16000u, BitConverter.ToUInt32(head, 24));   // sample rate
            Assert.Equal((short)1, BitConverter.ToInt16(head, 22));  // mono
            Assert.Equal((short)16, BitConverter.ToInt16(head, 34)); // bits
            Assert.Equal((uint)pcm.Length, BitConverter.ToUInt32(head, 40)); // data size
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
