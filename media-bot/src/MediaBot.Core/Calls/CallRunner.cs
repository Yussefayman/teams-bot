using System;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Calls;

/// <summary>Builds the per-call IAudioSink (the STT forwarder + optional WAV dump).</summary>
public interface IAudioSinkFactory
{
    IAudioSink Create(CallInfo info);
}

/// <summary>Delivers the call-ended event to the orchestrator (POST /webhooks/call-ended).</summary>
public interface ICallEndedNotifier
{
    Task NotifyAsync(CallEndedEvent ev, CancellationToken ct);
}

/// <summary>
/// End-to-end driver for one call, independent of how audio is sourced. Joins via the
/// factory, opens the sink, pumps audio/roster, sends session_end, then notifies the
/// orchestrator. Fully cross-platform and unit-testable (fake source + fake notifier +
/// real forwarder against a local WebSocket).
/// </summary>
public sealed class CallRunner
{
    private readonly ICallSourceFactory _sources;
    private readonly IAudioSinkFactory _sinks;
    private readonly ICallEndedNotifier _notifier;
    private readonly ILogger<CallRunner> _log;

    public CallRunner(ICallSourceFactory sources, IAudioSinkFactory sinks,
        ICallEndedNotifier notifier, ILogger<CallRunner> log)
    {
        _sources = sources;
        _sinks = sinks;
        _notifier = notifier;
        _log = log;
    }

    public async Task RunAsync(string joinUrl, string meetingId, CancellationToken ct)
    {
        _log.LogInformation("joining call {MeetingId}", meetingId);
        await using var source = await _sources.JoinAsync(joinUrl, meetingId, ct);
        var info = source.Info;

        await using var sink = _sinks.Create(info);
        await sink.StartAsync(
            SessionStart.Create(info.MeetingId, info.JoinUrl, info.Participants,
                info.ChatThreadId, info.OrganizerId), ct);

        _log.LogInformation("pumping audio for {MeetingId}", meetingId);
        var summary = await source.PumpAsync(sink, ct);

        await sink.EndAsync(ct);

        var ev = new CallEndedEvent(
            info.MeetingId, info.ChatThreadId, info.OrganizerId,
            summary.Participants,
            info.StartedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            summary.EndedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));

        _log.LogInformation("call {MeetingId} ended; notifying orchestrator", meetingId);
        await _notifier.NotifyAsync(ev, ct);
    }
}
