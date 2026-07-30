using System;
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
    public const int MaximumPerPlayer = 40;
    public const int MaximumPerZone = 200;
    public static readonly TimeSpan SpawnCooldown = TimeSpan.FromMilliseconds(500);

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
}
