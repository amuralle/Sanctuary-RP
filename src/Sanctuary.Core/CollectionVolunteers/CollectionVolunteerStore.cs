using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Sanctuary.Core.CollectionVolunteers;

public sealed class CollectionVolunteerStore
{
    public const int MaximumNoteLength = 200;
    public const int MaximumPendingPerUser = 500;
    public const float DuplicateRadius = 1.5f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _storagePath;
    private readonly string _lockPath;

    public CollectionVolunteerStore(string storagePath)
    {
        _storagePath = Path.GetFullPath(storagePath);
        _lockPath = $"{_storagePath}.lock";

        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
    }

    public IReadOnlyList<CollectionVolunteerPlacement> GetAll()
    {
        return WithLock(placements => placements
            .OrderBy(placement => placement.Id)
            .Select(Clone)
            .ToArray());
    }

    public CollectionVolunteerAddResult TryAdd(
        CollectionVolunteerPlacement placement,
        out CollectionVolunteerPlacement? stored)
    {
        var proposed = Clone(placement);
        proposed.Pool = NormalizePool(proposed.Pool);
        proposed.Note = NormalizeNote(proposed.Note);
        proposed.Status = CollectionVolunteerPlacementStatus.Pending;

        if (!IsValid(proposed))
        {
            stored = null;
            return CollectionVolunteerAddResult.Invalid;
        }

        var result = WithLock(placements =>
        {
            var now = DateTimeOffset.UtcNow;
            var latestSubmission = placements
                .Where(candidate => candidate.SubmittedByUserId == proposed.SubmittedByUserId)
                .MaxBy(candidate => candidate.SubmittedAt);

            if (latestSubmission is not null && now - latestSubmission.SubmittedAt < TimeSpan.FromSeconds(2))
                return (CollectionVolunteerAddResult.RateLimited, (CollectionVolunteerPlacement?)null);

            if (placements.Count(candidate =>
                candidate.SubmittedByUserId == proposed.SubmittedByUserId &&
                candidate.Status == CollectionVolunteerPlacementStatus.Pending) >= MaximumPendingPerUser)
            {
                return (CollectionVolunteerAddResult.PendingLimitReached, (CollectionVolunteerPlacement?)null);
            }

            var duplicateRadiusSquared = DuplicateRadius * DuplicateRadius;
            var duplicate = placements.Any(candidate =>
                candidate.ZoneId == proposed.ZoneId &&
                candidate.Pool == proposed.Pool &&
                candidate.Status != CollectionVolunteerPlacementStatus.Rejected &&
                DistanceSquared(candidate, proposed) <= duplicateRadiusSquared);

            if (duplicate)
                return (CollectionVolunteerAddResult.Duplicate, (CollectionVolunteerPlacement?)null);

            var created = Clone(proposed);
            created.Id = placements.Select(candidate => candidate.Id).DefaultIfEmpty().Max() + 1;
            created.SubmittedAt = now;
            created.Status = CollectionVolunteerPlacementStatus.Pending;
            created.ReviewedByUserId = null;
            created.ReviewedByUsername = null;
            created.ReviewedAt = null;

            placements.Add(created);
            Save(placements);
            return (CollectionVolunteerAddResult.Success, (CollectionVolunteerPlacement?)Clone(created));
        });

        stored = result.Item2;
        return result.Item1;
    }

    public bool TryUpdate(
        int id,
        ulong actorUserId,
        string actorUsername,
        bool actorIsAdmin,
        string pool,
        float x,
        float y,
        float z,
        float heading,
        string? note,
        CollectionVolunteerPlacementStatus status,
        out CollectionVolunteerPlacement? updated)
    {
        var result = WithLock(placements =>
        {
            var placement = placements.FirstOrDefault(candidate => candidate.Id == id);
            if (placement is null)
                return (Success: false, Placement: (CollectionVolunteerPlacement?)null);

            var ownsPendingPlacement = placement.SubmittedByUserId == actorUserId &&
                placement.Status == CollectionVolunteerPlacementStatus.Pending &&
                status == CollectionVolunteerPlacementStatus.Pending;

            if (!actorIsAdmin && !ownsPendingPlacement)
                return (Success: false, Placement: (CollectionVolunteerPlacement?)null);

            var proposed = Clone(placement);
            proposed.Pool = NormalizePool(pool);
            proposed.X = x;
            proposed.Y = y;
            proposed.Z = z;
            proposed.Heading = heading;
            proposed.Note = NormalizeNote(note);
            proposed.Status = status;

            var movesMaterializedHardPoint = placement.HardPointId.HasValue &&
                (proposed.Pool != placement.Pool ||
                    proposed.X != placement.X ||
                    proposed.Y != placement.Y ||
                    proposed.Z != placement.Z ||
                    proposed.Heading != placement.Heading);

            if (!IsValid(proposed) || movesMaterializedHardPoint)
                return (Success: false, Placement: (CollectionVolunteerPlacement?)null);

            var duplicateRadiusSquared = DuplicateRadius * DuplicateRadius;
            var duplicate = proposed.Status != CollectionVolunteerPlacementStatus.Rejected && placements.Any(candidate =>
                candidate.Id != proposed.Id &&
                candidate.ZoneId == proposed.ZoneId &&
                candidate.Pool == proposed.Pool &&
                candidate.Status != CollectionVolunteerPlacementStatus.Rejected &&
                DistanceSquared(candidate, proposed) <= duplicateRadiusSquared);

            if (duplicate)
                return (Success: false, Placement: (CollectionVolunteerPlacement?)null);

            if (actorIsAdmin && placement.Status != status)
            {
                proposed.ReviewedByUserId = actorUserId;
                proposed.ReviewedByUsername = actorUsername;
                proposed.ReviewedAt = DateTimeOffset.UtcNow;
            }

            var index = placements.IndexOf(placement);
            placements[index] = proposed;
            Save(placements);
            return (Success: true, Placement: Clone(proposed));
        });

        updated = result.Placement;
        return result.Success;
    }

    public bool TryAttachHardPoint(
        int id,
        ulong submittedByUserId,
        int hardPointId,
        out CollectionVolunteerPlacement? updated)
    {
        var result = WithLock(placements =>
        {
            var placement = placements.FirstOrDefault(candidate => candidate.Id == id);
            if (placement is null ||
                placement.SubmittedByUserId != submittedByUserId ||
                placement.Status != CollectionVolunteerPlacementStatus.Pending ||
                placement.HardPointId.HasValue ||
                hardPointId <= 0 ||
                placements.Any(candidate => candidate.HardPointId == hardPointId))
            {
                return (Success: false, Placement: (CollectionVolunteerPlacement?)null);
            }

            placement.HardPointId = hardPointId;
            Save(placements);
            return (Success: true, Placement: Clone(placement));
        });

        updated = result.Placement;
        return result.Success;
    }

    public bool TryDetachHardPoint(
        int id,
        ulong submittedByUserId,
        int hardPointId,
        out CollectionVolunteerPlacement? updated)
    {
        var result = WithLock(placements =>
        {
            var placement = placements.FirstOrDefault(candidate => candidate.Id == id);
            if (placement is null ||
                placement.SubmittedByUserId != submittedByUserId ||
                placement.Status != CollectionVolunteerPlacementStatus.Pending ||
                placement.HardPointId != hardPointId)
            {
                return (Success: false, Placement: (CollectionVolunteerPlacement?)null);
            }

            placement.HardPointId = null;
            Save(placements);
            return (Success: true, Placement: Clone(placement));
        });

        updated = result.Placement;
        return result.Success;
    }

    public bool TryDelete(int id, ulong actorUserId, bool actorIsAdmin)
    {
        return WithLock(placements =>
        {
            var placement = placements.FirstOrDefault(candidate => candidate.Id == id);
            if (placement is null)
                return false;

            if (!actorIsAdmin &&
                (placement.SubmittedByUserId != actorUserId ||
                    placement.Status != CollectionVolunteerPlacementStatus.Pending))
            {
                return false;
            }

            if (placement.HardPointId.HasValue)
                return false;

            placements.Remove(placement);
            Save(placements);
            return true;
        });
    }

    public static string NormalizePool(string? pool)
    {
        return pool?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private T WithLock<T>(Func<List<CollectionVolunteerPlacement>, T> action)
    {
        lock (_gate)
        {
            using var fileLock = AcquireFileLock();
            var placements = Load();
            return action(placements);
        }
    }

    private FileStream AcquireFileLock()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 39)
            {
                Thread.Sleep(25);
            }
        }

        throw new IOException($"Could not acquire the collection volunteer store lock at {_lockPath}.");
    }

    private List<CollectionVolunteerPlacement> Load()
    {
        if (!File.Exists(_storagePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<CollectionVolunteerPlacement>>(
                File.ReadAllText(_storagePath), JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Collection volunteer data at {_storagePath} is invalid.", exception);
        }
    }

    private void Save(List<CollectionVolunteerPlacement> placements)
    {
        var temporaryPath = $"{_storagePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(placements, JsonOptions));
            File.Move(temporaryPath, _storagePath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool IsValid(CollectionVolunteerPlacement placement)
    {
        return placement.ZoneId > 0 &&
            IsValidPool(placement.Pool) &&
            IsFinite(placement.X) &&
            IsFinite(placement.Y) &&
            IsFinite(placement.Z) &&
            IsFinite(placement.Heading) &&
            Enum.IsDefined(placement.Status) &&
            placement.SubmittedByUserId > 0 &&
            placement.SubmittedByCharacterId > 0 &&
            !string.IsNullOrWhiteSpace(placement.SubmittedByCharacterName) &&
            (placement.Note is null ||
                (placement.Note.Length <= MaximumNoteLength &&
                    !placement.Note.Contains('\n') &&
                    !placement.Note.Contains('\r')));
    }

    private static bool IsValidPool(string pool)
    {
        return pool.Length is > 0 and <= 64 &&
            pool.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float DistanceSquared(
        CollectionVolunteerPlacement left,
        CollectionVolunteerPlacement right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        var z = left.Z - right.Z;
        return x * x + y * y + z * z;
    }

    private static CollectionVolunteerPlacement Clone(CollectionVolunteerPlacement placement)
    {
        return new CollectionVolunteerPlacement
        {
            Id = placement.Id,
            ZoneId = placement.ZoneId,
            Pool = placement.Pool,
            X = placement.X,
            Y = placement.Y,
            Z = placement.Z,
            Heading = placement.Heading,
            Note = placement.Note,
            SubmittedByUserId = placement.SubmittedByUserId,
            SubmittedByCharacterId = placement.SubmittedByCharacterId,
            SubmittedByCharacterName = placement.SubmittedByCharacterName,
            SubmittedAt = placement.SubmittedAt,
            Status = placement.Status,
            HardPointId = placement.HardPointId,
            ReviewedByUserId = placement.ReviewedByUserId,
            ReviewedByUsername = placement.ReviewedByUsername,
            ReviewedAt = placement.ReviewedAt
        };
    }
}
