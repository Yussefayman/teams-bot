using System;
using System.Threading;
using System.Threading.Tasks;
using Mahdar.MediaBot.Lifecycle;

namespace Mahdar.MediaBot.Audio;

/// <summary>
/// Where a call's audio + lifecycle events go. The CallHandler depends only on this
/// interface, so audio routing is testable without the Graph Communications SDK.
/// </summary>
public interface IAudioSink : IAsyncDisposable
{
    Task StartAsync(SessionStart start, CancellationToken ct);

    /// <summary>Forward one PCM 16k/16-bit/mono frame exactly as received from the SDK socket.</summary>
    void OnAudioFrame(ReadOnlySpan<byte> pcm);

    Task SendRosterAsync(RosterUpdate roster, CancellationToken ct);

    Task EndAsync(CancellationToken ct);
}
