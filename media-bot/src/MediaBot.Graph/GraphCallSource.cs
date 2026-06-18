using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
using Microsoft.Graph.Communications.Resources;
using Microsoft.Graph.Models;
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
    private readonly Lazy<GraphAuthProvider> _auth;
    private readonly HttpClient _http;

    public GraphCallSourceFactory(BotOptions opts, ILoggerFactory loggers)
    {
        _opts = opts;
        _loggers = loggers;
        _log = loggers.CreateLogger("graph-call");
        _auth = new Lazy<GraphAuthProvider>(() => new GraphAuthProvider(_opts));
        _client = new Lazy<ICommunicationsClient>(BuildClient);
        _http = new HttpClient();
    }

    private ICommunicationsClient BuildClient()
    {
        var appName = GetType().Assembly.GetName().Name!;
        var graphLogger = new GraphLogger(appName);
        var auth = _auth.Value;

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
        var client = _client.Value;
        var mediaSession = CreateMediaSession(client);
        try
        {
            var target = await ResolveJoinTargetAsync(joinUrl, ct).ConfigureAwait(false);

            var joinParams = new JoinMeetingParameters(target.ChatInfo, target.MeetingInfo, mediaSession)
            {
                TenantId = target.TenantId,
            };

            _log.LogInformation("joining meeting {MeetingId} (thread {ThreadId})", meetingId, target.ThreadId);
            var call = await client.Calls().AddAsync(joinParams, Guid.NewGuid(), ct).ConfigureAwait(false);

            var info = new CallInfo(
                MeetingId: meetingId,
                JoinUrl: joinUrl,
                ChatThreadId: target.ThreadId,
                OrganizerId: target.OrganizerId,
                Participants: Array.Empty<LifecycleParticipant>(),
                StartedAt: DateTimeOffset.UtcNow);

            return new GraphCallSource(client, call, mediaSession, info, _log);
        }
        catch (Exception ex)
        {
            mediaSession.Dispose();
            _log.LogError(ex, "join failed for meeting {MeetingId} (url {JoinUrl}): {Error}",
                meetingId, joinUrl, ex.Message);
            throw;
        }
    }

    // Resolves a join URL to the chat thread + organizer/tenant the SDK needs. Work/school
    // meetup-join URLs carry everything in the URL; personal (teams.live.com) URLs do not,
    // so we look the meeting up via Graph, then fall back to the SDK's own URL parser.
    private async Task<JoinTarget> ResolveJoinTargetAsync(string joinUrl, CancellationToken ct)
    {
        var parsed = JoinUrlParser.Parse(joinUrl);
        if (parsed.Kind == JoinUrlKind.MeetupJoin)
            return BuildTarget(parsed.ThreadId!, parsed.OrganizerId!, parsed.TenantId!);

        try
        {
            var resolved = await ResolvePersonalMeetingViaGraphAsync(joinUrl, ct).ConfigureAwait(false);
            return BuildTarget(resolved.ThreadId!, resolved.OrganizerId!, resolved.TenantId!);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Graph onlineMeetings resolution failed for {JoinUrl}; "
                + "falling back to SDK join-URL parse: {Error}", joinUrl, ex.Message);

            var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(joinUrl);
            var threadId = chatInfo.ThreadId ?? "";
            var organizerId = (meetingInfo as OrganizerMeetingInfo)?.Organizer?.User?.Id ?? "";
            return new JoinTarget(chatInfo, meetingInfo, threadId, organizerId, _opts.TenantId);
        }
    }

    private async Task<ParsedJoinUrl> ResolvePersonalMeetingViaGraphAsync(string joinUrl, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"joinWebUrl eq '{joinUrl}'");
        var requestUri = $"{GraphServiceBaseUrl}/me/onlineMeetings?$filter={filter}";

        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await _auth.Value.AuthenticateOutboundRequestAsync(req, _opts.TenantId).ConfigureAwait(false);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Graph onlineMeetings lookup returned {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw new InvalidOperationException("Graph onlineMeetings lookup returned no meeting for the join URL");

        var meeting = value[0];
        var threadId = meeting.GetProperty("chatInfo").GetProperty("threadId").GetString();
        var user = meeting.GetProperty("participants").GetProperty("organizer")
            .GetProperty("identity").GetProperty("user");
        var organizerId = user.GetProperty("id").GetString();
        var tenantId = user.TryGetProperty("tenantId", out var tid) ? tid.GetString() : null;

        if (string.IsNullOrEmpty(threadId) || string.IsNullOrEmpty(organizerId))
            throw new InvalidOperationException("Graph onlineMeetings response missing threadId or organizer id");

        return new ParsedJoinUrl(JoinUrlKind.Personal, threadId, organizerId,
            string.IsNullOrEmpty(tenantId) ? _opts.TenantId : tenantId);
    }

    private static JoinTarget BuildTarget(string threadId, string organizerId, string tenantId)
    {
        var meetingInfo = new OrganizerMeetingInfo
        {
            Organizer = new IdentitySet
            {
                User = new Identity { Id = organizerId },
            },
        };
        meetingInfo.Organizer.AdditionalData = new Dictionary<string, object>
        {
            { "tenantId", tenantId }
        };

        var chatInfo = new ChatInfo { ThreadId = threadId, MessageId = "0" };
        return new JoinTarget(chatInfo, meetingInfo, threadId, organizerId, tenantId);
    }

    private sealed record JoinTarget(
        ChatInfo ChatInfo, MeetingInfo MeetingInfo, string ThreadId, string OrganizerId, string TenantId);

    // Mirrors PolicyRecordingBot.Bot.CreateLocalMediaSession: the SDK overload requires a
    // video socket list + a VBSS socket alongside the audio socket. We only consume the
    // mixed Pcm16K audio socket (MoM is audio-only), so video/VBSS are recv-only and unread.
    private static ILocalMediaSession CreateMediaSession(ICommunicationsClient client)
    {
        var videoSockets = new List<VideoSocketSettings>
        {
            new VideoSocketSettings
            {
                StreamDirections = StreamDirection.Recvonly,
                ReceiveColorFormat = VideoColorFormat.H264,
            },
        };

        var vbssSocket = new VideoSocketSettings
        {
            StreamDirections = StreamDirection.Recvonly,
            ReceiveColorFormat = VideoColorFormat.H264,
            MediaType = MediaType.Vbss,
            SupportedSendVideoFormats = new List<VideoFormat> { VideoFormat.H264_1920x1080_1_875Fps },
        };

        return client.CreateMediaSession(
            new AudioSocketSettings
            {
                StreamDirections = StreamDirection.Recvonly,
                SupportedAudioFormat = AudioFormat.Pcm16K,
            },
            videoSockets,
            vbssSocket,
            mediaSessionId: Guid.NewGuid());
    }

    private static IPAddress ResolvePublicIp(string hostname)
    {
        if (IPAddress.TryParse(hostname, out var literal)) return literal;
        var addresses = Dns.GetHostAddresses(hostname);
        return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
               ?? addresses.First();
    }
}

public enum JoinUrlKind
{
    // Work/school: teams.microsoft.com/l/meetup-join/... — thread + Oid/Tid live in the URL.
    MeetupJoin,
    // Personal: teams.live.com/meet/<id> — no thread/organizer in the URL; resolved via Graph.
    Personal,
}

/// <summary>
/// The pieces a join URL yields up-front. For <see cref="JoinUrlKind.MeetupJoin"/> every field
/// is populated; for <see cref="JoinUrlKind.Personal"/> the ids are null and must be resolved
/// out-of-band (Graph onlineMeetings lookup).
/// </summary>
public sealed record ParsedJoinUrl(JoinUrlKind Kind, string? ThreadId, string? OrganizerId, string? TenantId);

/// <summary>
/// Deterministic parser for a Teams meeting join URL. SDK-free and unit-testable.
/// Recognizes two formats:
///   work/school — https://teams.microsoft.com/l/meetup-join/19%3ameeting_...%40thread.v2/0?context=%7b%22Tid%22%3a..%2c%22Oid%22%3a..%7d
///   personal    — https://teams.live.com/meet/9328001500416?p=...
/// </summary>
public static class JoinUrlParser
{
    public static ParsedJoinUrl Parse(string joinUrl)
    {
        if (string.IsNullOrWhiteSpace(joinUrl)) throw new ArgumentException("joinUrl is empty");

        var uri = new Uri(joinUrl);
        var path = Uri.UnescapeDataString(uri.AbsolutePath);

        const string meetupMarker = "/meetup-join/";
        var idx = path.IndexOf(meetupMarker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = path[(idx + meetupMarker.Length)..];
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

            return new ParsedJoinUrl(JoinUrlKind.MeetupJoin, threadId, organizerId, tenantId);
        }

        const string personalMarker = "/meet/";
        if (uri.Host.IndexOf("teams.live.com", StringComparison.OrdinalIgnoreCase) >= 0
            && path.IndexOf(personalMarker, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ParsedJoinUrl(JoinUrlKind.Personal, null, null, null);
        }

        throw new ArgumentException("unrecognized Teams join URL format", nameof(joinUrl));
    }
}
