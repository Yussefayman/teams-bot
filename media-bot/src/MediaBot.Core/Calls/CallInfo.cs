using System;
using System.Collections.Generic;
using Mahdar.MediaBot.Lifecycle;

namespace Mahdar.MediaBot.Calls;

/// <summary>Metadata about an established call, known once the bot has joined.</summary>
public sealed record CallInfo(
    string MeetingId,
    string JoinUrl,
    string ChatThreadId,
    string OrganizerId,
    IReadOnlyList<Participant> Participants,
    DateTimeOffset StartedAt);

/// <summary>Final state of a call after its audio has finished pumping.</summary>
public sealed record CallSummary(
    IReadOnlyList<Participant> Participants,
    DateTimeOffset EndedAt);
