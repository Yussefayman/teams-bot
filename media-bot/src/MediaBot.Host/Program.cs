using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Graph;
using Mahdar.MediaBot.Host.Configuration;
using Mahdar.MediaBot.Host.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Host;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static async Task Main()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var opts = BotOptionsLoader.Load(config);
        var callSource = (Environment.GetEnvironmentVariable("CALL_SOURCE") ?? "fake").ToLowerInvariant();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(opts);
        services.AddSingleton<IAudioSinkFactory, SttSinkFactory>();
        services.AddHttpClient<ICallEndedNotifier, OrchestratorClient>();
        services.AddSingleton<CallRunner>();
        services.AddSingleton<ICallSourceFactory>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            if (callSource == "graph")
                return new GraphCallSourceFactory(opts, loggerFactory);

            var wav = Environment.GetEnvironmentVariable("FAKE_WAV_PATH")
                      ?? throw new InvalidOperationException("FAKE_WAV_PATH must be set when CALL_SOURCE=fake");
            return new FakeCallSourceFactory(wav, loggerFactory.CreateLogger("fake-call"));
        });

        using var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<ILoggerFactory>().CreateLogger("media-bot");
        log.LogInformation("media-bot starting (CALL_SOURCE={Source}, DUMP_AUDIO={Dump})", callSource, opts.DumpAudio);

        // HttpListener uses http.sys; for https the cert is bound to the port via
        // `netsh http add sslcert` (see deploy.ps1), not a Kestrel pfx. Default to
        // https on 443; override with HTTP_PREFIX (e.g. http://+:9442/ behind a proxy).
        var prefix = Environment.GetEnvironmentVariable("HTTP_PREFIX");
        if (string.IsNullOrEmpty(prefix)) prefix = "https://+:443/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        log.LogInformation("listening on {Prefix}", prefix);

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };

        while (!shutdown.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (shutdown.IsCancellationRequested) { break; }
            catch (HttpListenerException) { break; }

            _ = Task.Run(() => HandleAsync(ctx, provider, opts, callSource, log));
        }

        listener.Stop();
    }

    private static async Task HandleAsync(
        HttpListenerContext ctx, IServiceProvider provider, BotOptions opts, string callSource, ILogger log)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        try
        {
            if (req.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    status = "ok",
                    callSource,
                    dumpAudio = opts.DumpAudio,
                    sttConfigured = !string.IsNullOrEmpty(opts.SttWsBaseUrl),
                }).ConfigureAwait(false);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/joinCall")
            {
                JoinCallRequest? body = null;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(raw))
                        body = JsonSerializer.Deserialize<JoinCallRequest>(raw, Json);
                }

                if (body is null || string.IsNullOrWhiteSpace(body.JoinUrl) || string.IsNullOrWhiteSpace(body.MeetingId))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "joinUrl and meetingId are required" }).ConfigureAwait(false);
                    return;
                }

                var runner = provider.GetRequiredService<CallRunner>();
                var joinUrl = body.JoinUrl;
                var meetingId = body.MeetingId;

                // Fire-and-forget: the call runs for its full duration; return 202 immediately.
                _ = Task.Run(async () =>
                {
                    try { await runner.RunAsync(joinUrl, meetingId, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { log.LogError(ex, "call {MeetingId} failed", meetingId); }
                });

                await WriteJsonAsync(ctx.Response, 202, new { accepted = true, meetingId }).ConfigureAwait(false);
                return;
            }

            // The Azure Bot calling webhook. Real notifications are handled by the Graph
            // SDK's own notification endpoint; this route just exists and returns 200.
            if (req.HttpMethod == "POST" && path == "/api/calling")
            {
                await WriteJsonAsync(ctx.Response, 200, new { }).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(ctx.Response, 404, new { error = "not found" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "request {Method} {Path} failed", req.HttpMethod, path);
            try { await WriteJsonAsync(ctx.Response, 500, new { error = "internal error" }).ConfigureAwait(false); }
            catch { /* response already closed */ }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, int status, object body)
    {
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        res.OutputStream.Close();
    }
}

internal sealed record JoinCallRequest(string JoinUrl, string MeetingId);
