using System;
using System.Collections.Generic;
using System.Linq;

using Sanctuary.Core.Collections;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionNodePoolDefinition
{
    public required string Key { get; set; }
    public int ZoneDefinitionId { get; set; }
    public required string NodeType { get; set; }
    public int MaxActiveNodes { get; set; }
    public int RespawnSeconds { get; set; } = 30;
    public List<CollectionNodeDropDefinition> DropTable { get; set; } = [];

    public int TotalDropWeight => DropTable.Sum(drop => drop.Weight);

    /// <summary>
    /// Selects the drop whose weighted range contains the supplied roll.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The roll is outside the drop table's weighted range.</exception>
    /// <exception cref="InvalidOperationException">The drop table does not contain a matching entry.</exception>
    public CollectionNodeDropDefinition SelectDrop(int roll)
    {
        if (roll < 0 || roll >= TotalDropWeight)
            throw new ArgumentOutOfRangeException(nameof(roll));

        foreach (var drop in DropTable)
        {
            if (roll < drop.Weight)
                return drop;

            roll -= drop.Weight;
        }

        throw new InvalidOperationException("The collection node drop table is invalid.");
    }
    private WeightedDropTable<CollectionNodeDropDefinition>? _table;
    public WeightedDropTable<CollectionNodeDropDefinition> Table =>
        _table ??= new WeightedDropTable<CollectionNodeDropDefinition>(DropTable);

    /// <summary>
    /// Gets the number of nodes that should be active for the supplied hardpoint count.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hardPointCount"/> is negative.</exception>
    public int GetTargetActiveCount(int hardPointCount)
    {
        if (hardPointCount < 0)
            throw new ArgumentOutOfRangeException(nameof(hardPointCount));

        // A zero cap enables every hard point while a pool is being authored.
        return MaxActiveNodes == 0 ? hardPointCount : Math.Min(MaxActiveNodes, hardPointCount);
    }

    /// <summary>
    /// Randomly selects inactive hardpoints up to the pool's configured capacity.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="hardPoints"/> or <paramref name="activeHardPointIds"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumToActivate"/> is negative.</exception>
    public IReadOnlyList<CollectionNodeSpawnDefinition> SelectSpawnsToActivate(
        IEnumerable<CollectionNodeSpawnDefinition> hardPoints, IReadOnlySet<int> activeHardPointIds,
        int maximumToActivate, int? avoidHardPointId = null)
    {
        ArgumentNullException.ThrowIfNull(hardPoints);
        ArgumentNullException.ThrowIfNull(activeHardPointIds);

        if (maximumToActivate < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumToActivate));

        var candidates = hardPoints
            .Where(spawn => spawn.Pool == Key && !activeHardPointIds.Contains(spawn.Id))
            .ToList();
        var targetActiveCount = GetTargetActiveCount(activeHardPointIds.Count + candidates.Count);
        var selectionCount = Math.Min(maximumToActivate,
            Math.Max(0, targetActiveCount - activeHardPointIds.Count));
        var selected = new List<CollectionNodeSpawnDefinition>(selectionCount);

        // Prefer a different point after pickup, but never leave the pool below its target.
        if (avoidHardPointId.HasValue && candidates.Count > selectionCount)
            candidates.RemoveAll(spawn => spawn.Id == avoidHardPointId.Value);

        while (selected.Count < selectionCount && candidates.Count > 0)
        {
            var index = Random.Shared.Next(candidates.Count);
            selected.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return selected;
    }
}
