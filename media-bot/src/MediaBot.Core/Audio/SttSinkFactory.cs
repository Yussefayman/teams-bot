using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Microsoft.Extensions.Logging;

namespace Mahdar.MediaBot.Audio;

/// <summary>Creates one SttWebSocketForwarder per call (streams to STT + optional WAV dump).</summary>
public sealed class SttSinkFactory : IAudioSinkFactory
{
    private readonly BotOptions _opts;
    private readonly ILoggerFactory _loggers;

    public SttSinkFactory(BotOptions opts, ILoggerFactory loggers)
    {
        _opts = opts;
        _loggers = loggers;
    }

    public IAudioSink Create(CallInfo info)
        => new SttWebSocketForwarder(_opts, info.MeetingId,
            _loggers.CreateLogger($"stt:{info.MeetingId}"));
}
