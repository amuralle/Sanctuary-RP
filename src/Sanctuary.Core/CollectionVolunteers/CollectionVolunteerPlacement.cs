using System;
using System.Text.Json.Serialization;

namespace Sanctuary.Core.CollectionVolunteers;

[JsonConverter(typeof(JsonStringEnumConverter<CollectionVolunteerPlacementStatus>))]
public enum CollectionVolunteerPlacementStatus
{
    Pending,
    Accepted,
    Rejected
}

public sealed class CollectionVolunteerPlacement
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public string Pool { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public string? Note { get; set; }
    public ulong SubmittedByUserId { get; set; }
    public ulong SubmittedByCharacterId { get; set; }
    public string SubmittedByCharacterName { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }
    public CollectionVolunteerPlacementStatus Status { get; set; }
    public int? HardPointId { get; set; }
    public ulong? ReviewedByUserId { get; set; }
    public string? ReviewedByUsername { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public enum CollectionVolunteerAddResult
{
    Success,
    Duplicate,
    RateLimited,
    PendingLimitReached,
    Invalid
}
