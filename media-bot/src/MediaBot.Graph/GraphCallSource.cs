using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Web;
using Mahdar.MediaBot.Audio;
using Mahdar.MediaBot.Calls;
using Mahdar.MediaBot.Configuration;
using Mahdar.MediaBot.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Skype.Bots.Media;

// Microsoft.Graph also defines a `Participant` type; our lifecycle DTO is distinct.
using LifecycleParticipant = Mahdar.MediaBot.Lifecycle.Participant;

namespace Mahdar.MediaBot.Graph;

/// <summary>
/// WINDOWS-ONLY real call source backed by the Graph Communications media platform.
/// Implements the same ICallSource contract FakeCallSource does, so the Host, CallRunner,
/// forwarder and lifecycle code are identical across dev (mac) and prod.
///
/// Builds/runs ONLY on the Windows VM: targets net8.0-windows and depends on the native
/// Skype/Graph media libraries. The exact SDK type/member names below track the
/// microsoft/graph-comms-samples RecordingBot sample for the pinned package versions —
/// reconcile against the packages actually restored on the VM (see MediaBot.Graph.csproj).
/// </summary>
public sealed class GraphCallSource : ICallSource
{
    private readonly ICommunicationsClient _client;
    private readonly ICall _call;
    private readonly ILocalMediaSession _mediaSession;
    private readonly ILogger _log;

    public CallInfo Info { get; }

    public GraphCallSource(
        ICommunicationsClient client, ICall call, ILocalMediaSession mediaSession,
        CallInfo info, ILogger log)
    {
        _client = client;
        _call = call;
        _mediaSession = mediaSession;
        Info = info;
        _log = log;
    }

    public async Task<CallSummary> PumpAsync(IAudioSink sink, CancellationToken ct)
    {
        var ended = new TaskCompletionSource<CallSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var audioSocket = _mediaSession.AudioSocket;

        void OnAudioMediaReceived(object? sender, AudioMediaReceivedEventArgs e)
        {
            try
            {
                int len = (int)e.Buffer.Length;
                if (len > 0)
                {
                    // SDK hands us unmanaged PCM 16k/16-bit/mono (mixed socket). Copy out
                    // before disposing the buffer; the sink forwards it as-is.
                    var pcm = new byte[len];
                    Marshal.Copy(e.Buffer.Data, pcm, 0, len);
                    sink.OnAudioFrame(pcm);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "audio frame forward failed for {MeetingId}", Info.MeetingId);
            }
            finally
            {
                e.Buffer.Dispose();
            }
        }

        async void OnParticipantsUpdated(IParticipantCollection sender, CollectionEventArgs<IParticipant> args)
        {
            try
            {
                var roster = BuildRoster(sender);
                await sink.SendRosterAsync(RosterUpdate.Create(Info.MeetingId, roster), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "roster update failed for {MeetingId}", Info.MeetingId);
            }
        }

        void OnCallUpdated(ICall sender, ResourceEventArgs<Call> args)
        {
            if (sender.Resource.State == CallState.Terminated)
            {
                var roster = BuildRoster(_call.Participants);
                ended.TrySetResult(new CallSummary(roster, DateTimeOffset.UtcNow));
            }
        }

        audioSocket.AudioMediaReceived += OnAudioMediaReceived;
        _call.Participants.OnUpdated += OnParticipantsUpdated;
        _call.OnUpdated += OnCallUpdated;

        using var reg = ct.Register(() => ended.TrySetResult(
            new CallSummary(BuildRoster(_call.Participants), DateTimeOffset.UtcNow)));

        try
        {
            await sink.SendRosterAsync(RosterUpdate.Create(Info.MeetingId, Info.Participants), ct)
                .ConfigureAwait(false);
            _log.LogInformation("graph call {MeetingId} pumping audio", Info.MeetingId);
            return await ended.Task.ConfigureAwait(false);
        }
        finally
        {
            audioSocket.AudioMediaReceived -= OnAudioMediaReceived;
            _call.Participants.OnUpdated -= OnParticipantsUpdated;
            _call.OnUpdated -= OnCallUpdated;
        }
    }

    private static IReadOnlyList<LifecycleParticipant> BuildRoster(IParticipantCollection participants)
    {
        var roster = new List<LifecycleParticipant>();
        foreach (var p in participants)
        {
            var user = p.Resource?.Info?.Identity?.User;
            if (user is null) continue;
            roster.Add(new LifecycleParticipant(user.DisplayName ?? user.Id ?? "unknown", user.Id ?? ""));
        }
        return roster;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_call.Resource.State != CallState.Terminated)
                await _call.DeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "graph call {MeetingId} dispose/leave failed", Info.MeetingId);
        }
        _mediaSession.Dispose();
    }
}

/// <summary>WINDOWS-ONLY factory: builds one shared communications client, then joins
/// meetings with application-hosted media.</summary>
public sealed class GraphCallSourceFactory : ICallSourceFactory
{
    private const string GraphServiceBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string CallingNotificationPath = "/api/calling";

    private readonly BotOptions _opts;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _log;
    private readonly Lazy<ICommunicationsClient> _client;

    public GraphCallSourceFactory(BotOptions opts, ILoggerFactory loggers)
    {
        _opts = opts;
        _loggers = loggers;
        _log = loggers.CreateLogger("graph-call");
        _client = new Lazy<ICommunicationsClient>(BuildClient);
    }

    private ICommunicationsClient BuildClient()
    {
        var appName = GetType().Assembly.GetName().Name!;
        var graphLogger = new GraphLogger(appName);
        var auth = new GraphAuthProvider(_opts);

        var mediaSettings = new MediaPlatformSettings
        {
            ApplicationId = _opts.BotAppId,
            MediaPlatformInstanceSettings = new MediaPlatformInstanceSettings
            {
                CertificateThumbprint = _opts.CertificateThumbprint,
                InstanceInternalPort = _opts.MediaPort,
                InstancePublicPort = _opts.MediaPort,
                InstancePublicIPAddress = ResolvePublicIp(_opts.PublicHostname),
                ServiceFqdn = _opts.PublicHostname,
            },
        };

        return new CommunicationsClientBuilder(appName, _opts.BotAppId, graphLogger)
            .SetAuthenticationProvider(auth)
            .SetNotificationUrl(new Uri($"https://{_opts.PublicHostname}{CallingNotificationPath}"))
            .SetMediaPlatformSettings(mediaSettings)
            .SetServiceBaseUrl(new Uri(GraphServiceBaseUrl))
            .Build();
    }

    public async Task<ICallSource> JoinAsync(string joinUrl, string meetingId, CancellationToken ct)
    {
        var (threadId, organizerId, tenantId) = JoinUrlParser.Parse(joinUrl);
        var client = _client.Value;

        var mediaSession = client.CreateMediaSession(
            new AudioSocketSettings
            {
                StreamDirections = StreamDirection.Recvonly,
                SupportedAudioFormat = AudioFormat.Pcm16K,
            },
            new VideoSocketSettings { StreamDirections = StreamDirection.Inactive });

        var meetingInfo = new OrganizerMeetingInfo
        {
            Organizer = new IdentitySet
            {
                User = new Identity { Id = organizerId },
            },
        };
        meetingInfo.Organizer.User.SetTenantId(tenantId);

        var chatInfo = new ChatInfo { ThreadId = threadId, MessageId = "0" };

        var joinParams = new JoinMeetingParameters(chatInfo, meetingInfo, mediaSession)
        {
            TenantId = tenantId,
        };

        _log.LogInformation("joining meeting {MeetingId} (thread {ThreadId})", meetingId, threadId);
        var call = await client.Calls().AddAsync(joinParams, ct).ConfigureAwait(false);

        var info = new CallInfo(
            MeetingId: meetingId,
            JoinUrl: joinUrl,
            ChatThreadId: threadId,
            OrganizerId: organizerId,
            Participants: Array.Empty<LifecycleParticipant>(),
            StartedAt: DateTimeOffset.UtcNow);

        return new GraphCallSource(client, call, mediaSession, info, _log);
    }

    private static IPAddress ResolvePublicIp(string hostname)
    {
        if (IPAddress.TryParse(hostname, out var literal)) return literal;
        var addresses = Dns.GetHostAddresses(hostname);
        return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
               ?? addresses.First();
    }
}

/// <summary>
/// Deterministic parser for a Teams meeting join URL. SDK-free and unit-testable.
/// Extracts the chat thread id and the organizer/tenant from the <c>context</c> query.
/// Example: https://teams.microsoft.com/l/meetup-join/19%3ameeting_...%40thread.v2/0?context=%7b%22Tid%22%3a..%2c%22Oid%22%3a..%7d
/// </summary>
public static class JoinUrlParser
{
    public static (string threadId, string organizerId, string tenantId) Parse(string joinUrl)
    {
        if (string.IsNullOrWhiteSpace(joinUrl)) throw new ArgumentException("joinUrl is empty");

        var uri = new Uri(joinUrl);
        const string marker = "/meetup-join/";
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) throw new ArgumentException("not a Teams meetup-join URL", nameof(joinUrl));

        var rest = path[(idx + marker.Length)..];
        var slash = rest.IndexOf('/');
        var threadId = slash >= 0 ? rest[..slash] : rest;
        if (string.IsNullOrEmpty(threadId)) throw new ArgumentException("could not parse thread id", nameof(joinUrl));

        var context = HttpUtility.ParseQueryString(uri.Query).Get("context")
            ?? throw new ArgumentException("join URL missing context", nameof(joinUrl));
        using var doc = JsonDocument.Parse(context);
        var root = doc.RootElement;
        var organizerId = root.TryGetProperty("Oid", out var oid) ? oid.GetString() ?? "" : "";
        var tenantId = root.TryGetProperty("Tid", out var tid) ? tid.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(organizerId) || string.IsNullOrEmpty(tenantId))
            throw new ArgumentException("join URL context missing Oid/Tid", nameof(joinUrl));

        return (threadId, organizerId, tenantId);
    }
}
