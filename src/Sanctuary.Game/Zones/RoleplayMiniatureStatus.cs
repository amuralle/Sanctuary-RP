using System;
using System.Collections.Generic;
using System.Numerics;

namespace Sanctuary.Game.Zones;

public enum RoleplayMiniatureCreateResult
{
    Success,
    InvalidModel,
    InvalidName,
    InvalidGroup,
    PlayerLimitReached,
    ZoneLimitReached,
    RateLimited
}

public enum RoleplayMiniatureSpeakResult
{
    Success,
    NotFound,
    InvalidMessage,
    RateLimited
}

public enum RoleplayMiniatureMoveResult
{
    Success,
    NotFound,
    TooFar,
    RateLimited
}

public enum RoleplayMiniatureEmoteResult
{
    Success,
    NotFound,
    RateLimited
}

public sealed record RoleplayMiniatureStatus(
    int Id,
    ulong Guid,
    int ModelId,
    string Name,
    string Group,
    ulong OwnerGuid,
    string OwnerName,
    Vector4 Position);

public static class RoleplayMiniatureRules
{
    public const string DefaultGroup = "scene";
    public const int MaximumNameLength = 40;
    public const int MaximumGroupLength = 24;
    public const int MaximumSpeechLength = 180;
    public const int MaximumPerPlayer = 40;
    public const int MaximumPerZone = 200;
    public const float MaximumMovementDistance = 250f;
    public const float MovementSpeed = 6.25f;
    public const float WaypointTolerance = 0.25f;
    public static readonly TimeSpan SpawnCooldown = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan SpeechCooldown = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MovementCooldown = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan EmoteCooldown = TimeSpan.FromMilliseconds(500);

    public static IReadOnlyDictionary<string, int> Emotes { get; } = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["cheer"] = 3306,
        ["confused"] = 3307,
        ["dance"] = 3201,
        ["exclaim"] = 3103,
        ["give"] = 3314,
        ["inquire"] = 3102,
        ["pose"] = 3251,
        ["pose2"] = 3252,
        ["receive"] = 3323,
        ["shrug"] = 3326,
        ["talk"] = 3101,
        ["taunt"] = 3330,
        ["thanks"] = 3331,
        ["wave"] = 3333,
        ["yawn"] = 3334
    };

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        if (trimmed.Length > MaximumNameLength)
            return false;

        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
                return false;
        }

        return true;
    }

    public static bool TryNormalizeGroup(string? group, out string normalized)
    {
        normalized = (group ?? DefaultGroup).Trim().TrimStart('@').ToLowerInvariant();

        if (normalized.Length is < 1 or > MaximumGroupLength)
            return false;

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
                return false;
        }

        return true;
    }

    public static bool IsValidSpeech(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var trimmed = message.Trim();
        if (trimmed.Length > MaximumSpeechLength)
            return false;

        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
                return false;
        }

        return true;
    }

    public static bool IsMovementWithinLimit(Vector3 origin, Vector3 destination)
    {
        return Vector3.DistanceSquared(origin, destination) <= MaximumMovementDistance * MaximumMovementDistance;
    }
}
