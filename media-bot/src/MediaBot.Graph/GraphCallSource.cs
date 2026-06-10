using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Graph;

/// <summary>
/// WINDOWS-ONLY real call source backed by the Graph Communications media platform.
/// Implements the same ICallSource contract the FakeCallSource does, so the Host,
/// CallRunner, forwarder and lifecycle code are identical across dev (mac) and prod.
///
/// This file is a structured skeleton: the SDK wiring (StatefulCall, the mixed
/// AudioSocket, roster events) must be completed on the Windows VM against the pinned
/// Microsoft.Graph.Communications.* packages, following the RecordingBot sample. The
/// integration points are marked with TODO(graph-sdk).
/// </summary>
public sealed class GraphCallSource : ICallSource
{
    private readonly BotOptions _opts;
    private readonly ILogger _log;

    public CallInfo Info { get; }

    public GraphCallSource(BotOptions opts, CallInfo info, ILogger log)
    {
        _opts = opts;
        Info = info;
        _log = log;
    }

    public Task<CallSummary> PumpAsync(IAudioSink sink, CancellationToken ct)
    {
        // TODO(graph-sdk): subscribe to the mixed AudioSocket and on each frame call
        //   sink.OnAudioFrame(buffer.UnsafeData -> ReadOnlySpan<byte>);  // PCM 16k/16-bit/mono
        // TODO(graph-sdk): on ParticipantsChanged, build a roster and call
        //   sink.SendRosterAsync(RosterUpdate.Create(Info.MeetingId, participants), ct);
        // TODO(graph-sdk): complete this Task when the call ends (hang-up / bot removed),
        //   returning the final roster + end time.
        throw new NotImplementedException(
            "GraphCallSource.PumpAsync: wire the AudioSocket on the Windows VM. " +
            "The audio-forwarding contract is already proven by the FakeCallSource pipeline test.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>WINDOWS-ONLY factory: resolves the join URL via Graph and joins with app-hosted media.</summary>
public sealed class GraphCallSourceFactory : ICallSourceFactory
{
    private readonly BotOptions _opts;
    private readonly ILoggerFactory _loggers;

    public GraphCallSourceFactory(BotOptions opts, ILoggerFactory loggers)
    {
        _opts = opts;
        _loggers = loggers;
    }

    public Task<ICallSource> JoinAsync(string joinUrl, string meetingId, CancellationToken ct)
    {
        // TODO(graph-sdk): use ICommunicationsClient.Calls().AddAsync(...) with
        // application-hosted media + the meeting join info resolved from joinUrl; capture
        // the chat thread id and organizer id for the CallInfo below.
        throw new NotImplementedException(
            "GraphCallSourceFactory.JoinAsync: implement the Graph join on the Windows VM " +
            "(see RecordingBot sample). CallInfo must carry chatThreadId + organizerId.");
    }
}
