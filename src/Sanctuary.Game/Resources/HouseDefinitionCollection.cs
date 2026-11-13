using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Resources;

public class HouseDefinitionCollection : ObservableConcurrentDictionary<int, HouseDefinition>
{
    private readonly ILogger _logger;

    public HouseDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var entries = JsonSerializer.Deserialize<List<HouseDefinition>>(fileStream, jsonSerializerOptions);

            if (entries is null)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            if (entries.Count == 0 || entries.Any(entry => entry is null || entry.Id <= 0 || entry.Id >= (1 << 30) || entry.ZoneId <= 0)
                || entries.Select(entry => entry.Id).Distinct().Count() != entries.Count
                || entries.Select(entry => entry.ZoneId).Distinct().Count() != entries.Count)
            {
                _logger.LogError("Invalid or duplicate house definitions found in \"{file}\".", filePath);
                return false;
            }

            // Validate before replacing the catalog; bit 30 is reserved by housing instance keys.
            Clear();
            foreach (var entry in entries)
                TryAdd(entry.Id, entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }

        if (Count == 0)
        {
            _logger.LogError("No data was loaded. \"{file}\"", filePath);
            return false;
        }

        return true;
    }
}
