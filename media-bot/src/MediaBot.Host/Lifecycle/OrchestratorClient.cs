using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Host.Lifecycle;

/// <summary>POSTs the call-ended event to the orchestrator (/webhooks/call-ended).</summary>
public sealed class OrchestratorClient : ICallEndedNotifier
{
    private readonly HttpClient _http;
    private readonly BotOptions _opts;
    private readonly ILogger<OrchestratorClient> _log;

    public OrchestratorClient(HttpClient http, BotOptions opts, ILogger<OrchestratorClient> log)
    {
        _http = http;
        _opts = opts;
        _log = log;
    }

    public async Task NotifyAsync(CallEndedEvent ev, CancellationToken ct)
    {
        var url = $"{_opts.OrchestratorBaseUrl.TrimEnd('/')}/webhooks/call-ended";
        try
        {
            var res = await _http.PostAsJsonAsync(url, ev, ct);
            _log.LogInformation("call-ended -> {Url} : {Status}", url, (int)res.StatusCode);
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            // The MoM pipeline can still be triggered manually; do not crash the call teardown.
            _log.LogError(ex, "failed to POST call-ended for {MeetingId}", ev.MeetingId);
        }
    }
}
