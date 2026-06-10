using System;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Audio;

namespace Mahdar.MediaBot.Calls;

/// <summary>
/// A joined call that produces audio + roster + lifecycle. Implemented by
/// FakeCallSource (replays a WAV, cross-platform) and GraphCallSource (real Teams
/// media socket, Windows-only). The rest of the bot depends only on this interface,
/// so the whole pipeline runs and is tested on macOS/Linux with the fake source.
/// </summary>
public interface ICallSource : IAsyncDisposable
{
    CallInfo Info { get; }

    /// <summary>
    /// Drive the sink for the lifetime of the call: forward every PCM frame and any
    /// roster changes. Completes when the call ends (file EOF for fake; hang-up /
    /// bot-removed for real). Returns the final call state.
    /// </summary>
    Task<CallSummary> PumpAsync(IAudioSink sink, CancellationToken ct);
}

/// <summary>Joins a meeting and returns a live <see cref="ICallSource"/>.</summary>
public interface ICallSourceFactory
{
    Task<ICallSource> JoinAsync(string joinUrl, string meetingId, CancellationToken ct);
}
