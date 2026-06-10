using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mahdar.MediaBot.Lifecycle;

/// <summary>
/// JSON control messages the bot sends over the STT WebSocket, and the call-ended
/// webhook body POSTed to the orchestrator. These mirror shared/schemas/*.json —
/// keep them in sync (call_event.schema.json validates CallEndedEvent).
/// </summary>
public sealed record Participant(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("aadId")] string AadId,
    [property: JsonPropertyName("email")] string? Email = null);

public sealed record SessionStart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("meetingId")] string MeetingId,
    [property: JsonPropertyName("joinUrl")] string JoinUrl,
    [property: JsonPropertyName("participants")] IReadOnlyList<Participant> Participants,
    [property: JsonPropertyName("chatThreadId")] string ChatThreadId,
    [property: JsonPropertyName("organizerId")] string OrganizerId)
{
    public static SessionStart Create(string meetingId, string joinUrl,
        IReadOnlyList<Participant> participants, string chatThreadId, string organizerId)
        => new("session_start", meetingId, joinUrl, participants, chatThreadId, organizerId);
}

public sealed record RosterUpdate(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("meetingId")] string MeetingId,
    [property: JsonPropertyName("participants")] IReadOnlyList<Participant> Participants)
{
    public static RosterUpdate Create(string meetingId, IReadOnlyList<Participant> participants)
        => new("roster_update", meetingId, participants);
}

public sealed record SessionEnd(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("meetingId")] string MeetingId)
{
    public static SessionEnd Create(string meetingId) => new("session_end", meetingId);
}

public sealed record SessionResume(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("meetingId")] string MeetingId)
{
    public static SessionResume Create(string meetingId) => new("session_resume", meetingId);
}

/// <summary>POSTed to orchestrator /webhooks/call-ended. Must satisfy call_event.schema.json.</summary>
public sealed record CallEndedEvent(
    [property: JsonPropertyName("meetingId")] string MeetingId,
    [property: JsonPropertyName("chatThreadId")] string ChatThreadId,
    [property: JsonPropertyName("organizerId")] string OrganizerId,
    [property: JsonPropertyName("participants")] IReadOnlyList<Participant> Participants,
    [property: JsonPropertyName("startedAt")] string StartedAt,
    [property: JsonPropertyName("endedAt")] string EndedAt);
