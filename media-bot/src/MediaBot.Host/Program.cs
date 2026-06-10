using System;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Host.Configuration;
using Mahdar.MediaBot.Host.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var opts = BotOptionsLoader.Load(builder.Configuration);
builder.Services.AddSingleton(opts);
builder.Services.AddSingleton<IAudioSinkFactory, SttSinkFactory>();
builder.Services.AddHttpClient<ICallEndedNotifier, OrchestratorClient>();
builder.Services.AddSingleton<CallRunner>();

// Pick the audio source. "fake" (default) replays a WAV and runs anywhere.
// "graph" is the real Teams media socket and is only available in the Windows build.
var callSource = (Environment.GetEnvironmentVariable("CALL_SOURCE") ?? "fake").ToLowerInvariant();
builder.Services.AddSingleton<ICallSourceFactory>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    if (callSource == "graph")
    {
#if WINDOWS
        return new Mahdar.MediaBot.Graph.GraphCallSourceFactory(opts, loggerFactory);
#else
        throw new PlatformNotSupportedException(
            "CALL_SOURCE=graph requires the Windows build (MediaBot.Graph). " +
            "Use CALL_SOURCE=fake on macOS/Linux.");
#endif
    }
    var wav = Environment.GetEnvironmentVariable("FAKE_WAV_PATH")
              ?? throw new InvalidOperationException("FAKE_WAV_PATH must be set when CALL_SOURCE=fake");
    return new FakeCallSourceFactory(wav, loggerFactory.CreateLogger("fake-call"));
});

var app = builder.Build();
app.Logger.LogInformation("media-bot starting (CALL_SOURCE={Source}, DUMP_AUDIO={Dump})",
    callSource, opts.DumpAudio);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    callSource,
    dumpAudio = opts.DumpAudio,
    sttConfigured = !string.IsNullOrEmpty(opts.SttWsBaseUrl),
}));

// POST /api/joinCall { joinUrl, meetingId } -> joins and runs the call to completion.
app.MapPost("/api/joinCall", (JoinCallRequest req, CallRunner runner, ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(req.JoinUrl) || string.IsNullOrWhiteSpace(req.MeetingId))
        return Results.BadRequest(new { error = "joinUrl and meetingId are required" });

    // Fire-and-forget: the call runs for its full duration; return 202 immediately.
    _ = Task.Run(async () =>
    {
        try { await runner.RunAsync(req.JoinUrl, req.MeetingId, CancellationToken.None); }
        catch (Exception ex) { log.LogError(ex, "call {MeetingId} failed", req.MeetingId); }
    });
    return Results.Accepted($"/api/calls/{req.MeetingId}", new { accepted = true, req.MeetingId });
});

// POST /api/calling — the Azure Bot calling webhook (real notifications handled in the
// Windows/Graph build). Present here so the route exists; returns 200.
app.MapPost("/api/calling", () => Results.Ok());

app.Run();

internal sealed record JoinCallRequest(string JoinUrl, string MeetingId);

public partial class Program { } // for potential WebApplicationFactory tests
