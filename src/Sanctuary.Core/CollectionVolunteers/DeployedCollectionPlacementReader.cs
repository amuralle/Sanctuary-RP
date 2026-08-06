using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sanctuary.Core.CollectionVolunteers;

public sealed record DeployedCollectionPlacement(
    int Id,
    int ZoneId,
    string Pool,
    float X,
    float Y,
    float Z,
    float Heading);

public sealed class DeployedCollectionPlacementReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<DeployedCollectionPlacement> _placements;

    public DeployedCollectionPlacementReader(string rootPath)
    {
        var resolvedRoot = Path.GetFullPath(rootPath);
        _placements = Load(resolvedRoot);
    }

    public IReadOnlyList<DeployedCollectionPlacement> GetAll()
    {
        return _placements;
    }

    private static IReadOnlyList<DeployedCollectionPlacement> Load(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return [];

        var placements = new List<DeployedCollectionPlacement>();

        foreach (var zonePath in Directory.EnumerateDirectories(rootPath))
        {
            if (!int.TryParse(Path.GetFileName(zonePath), out var zoneId) || zoneId <= 0)
                continue;

            foreach (var filePath in Directory.EnumerateFiles(zonePath, "*.json"))
            {
                var pool = CollectionVolunteerStore.NormalizePool(
                    Path.GetFileNameWithoutExtension(filePath));
                var resources = JsonSerializer.Deserialize<List<ResourcePlacement>>(
                    File.ReadAllText(filePath), JsonOptions) ?? [];

                foreach (var resource in resources)
                {
                    if (resource.Id <= 0 ||
                        resource.Position is not { Length: 3 } ||
                        resource.Position.Any(value => !float.IsFinite(value)) ||
                        !float.IsFinite(resource.Heading))
                    {
                        throw new InvalidOperationException(
                            $"Collection hardpoint resource {filePath} contains an invalid placement.");
                    }

                    placements.Add(new DeployedCollectionPlacement(
                        resource.Id,
                        zoneId,
                        pool,
                        resource.Position[0],
                        resource.Position[1],
                        resource.Position[2],
                        resource.Heading));
                }
            }
        }

        var duplicate = placements
            .GroupBy(placement => (placement.ZoneId, placement.Pool, placement.Id))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Collection hardpoint {duplicate.Key.Id} is duplicated in zone " +
                $"{duplicate.Key.ZoneId}, pool {duplicate.Key.Pool}.");
        }

        return placements
            .OrderBy(placement => placement.ZoneId)
            .ThenBy(placement => placement.Pool)
            .ThenBy(placement => placement.Id)
            .ToArray();
    }

    private sealed class ResourcePlacement
    {
        public int Id { get; set; }
        public float[] Position { get; set; } = [];
        public float Heading { get; set; }
    }
}
