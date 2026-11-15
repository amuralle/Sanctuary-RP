using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;

using Sanctuary.Core.IO;

namespace Sanctuary.Core.RoleplayMiniatures;

public sealed class RoleplayMiniatureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new Vector4JsonConverter(),
            new SnapshotRotationConverter()
        }
    };

    // Read legacy two-component rotations, but preserve all components in new saves.
    private sealed class SnapshotRotationConverter : QuaternionJsonConverter
    {
        public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteNumberValue(value.W);
            writer.WriteEndArray();
        }
    }

    private readonly object _gate = new();
    private readonly string _storagePath;
    private readonly string _lockPath;

    public RoleplayMiniatureStore(string storagePath)
    {
        _storagePath = Path.GetFullPath(storagePath);
        _lockPath = $"{_storagePath}.lock";

        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
    }

    public IReadOnlyList<RoleplayMiniatureSnapshot> GetForZone(int zoneDefinitionId)
    {
        return WithLock(snapshots => snapshots
            .Where(snapshot => snapshot.ZoneDefinitionId == zoneDefinitionId)
            .OrderBy(snapshot => snapshot.Id)
            .Select(Clone)
            .ToArray());
    }

    public void ReplaceZone(int zoneDefinitionId, IEnumerable<RoleplayMiniatureSnapshot> zoneSnapshots)
    {
        WithLock(snapshots =>
        {
            snapshots.RemoveAll(snapshot => snapshot.ZoneDefinitionId == zoneDefinitionId);
            snapshots.AddRange(zoneSnapshots.Select(Clone));
            Save(snapshots);
            return 0;
        });
    }

    public void RemoveZone(int zoneDefinitionId)
    {
        ReplaceZone(zoneDefinitionId, []);
    }

    private T WithLock<T>(Func<List<RoleplayMiniatureSnapshot>, T> action)
    {
        lock (_gate)
        {
            using var fileLock = AcquireFileLock();
            var snapshots = Load();
            return action(snapshots);
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

        throw new IOException($"Could not acquire the roleplay miniature store lock at {_lockPath}.");
    }

    private List<RoleplayMiniatureSnapshot> Load()
    {
        if (!File.Exists(_storagePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<RoleplayMiniatureSnapshot>>(
                File.ReadAllText(_storagePath), JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Roleplay miniature data at {_storagePath} is invalid.", exception);
        }
    }

    private void Save(List<RoleplayMiniatureSnapshot> snapshots)
    {
        var temporaryPath = $"{_storagePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshots, JsonOptions));
            File.Move(temporaryPath, _storagePath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static RoleplayMiniatureSnapshot Clone(RoleplayMiniatureSnapshot snapshot)
    {
        return new RoleplayMiniatureSnapshot
        {
            ZoneDefinitionId = snapshot.ZoneDefinitionId,
            Id = snapshot.Id,
            ModelId = snapshot.ModelId,
            Name = snapshot.Name,
            Group = snapshot.Group,
            OwnerGuid = snapshot.OwnerGuid,
            OwnerName = snapshot.OwnerName,
            Position = snapshot.Position,
            Rotation = snapshot.Rotation
        };
    }
}
