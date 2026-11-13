using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Resources;

public sealed class HousingFixtureDefinitionCollection(ILogger logger) : ObservableConcurrentDictionary<int, HousingFixtureDefinition>
{
    public bool Load(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var entries = JsonSerializer.Deserialize<List<HousingFixtureDefinition>>(stream);
            if (entries is null || entries.Count == 0 || entries.Any(entry => entry is null || entry.ItemDefinitionId <= 0 || entry.ModelId <= 0)
                || entries.Select(entry => entry.ItemDefinitionId).Distinct().Count() != entries.Count)
                return false;

            Clear();
            foreach (var entry in entries)
                TryAdd(entry.ItemDefinitionId, entry);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load housing fixtures from {file}.", filePath);
            return false;
        }
    }
}
