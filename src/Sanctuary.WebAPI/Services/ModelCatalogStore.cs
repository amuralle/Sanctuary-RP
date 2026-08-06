using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Models;
using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Services;

public sealed class ModelCatalogStore
{
    private readonly string _imageDirectory;
    private readonly string _metadataPath;
    private readonly HashSet<int> _validModelIds;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<int, ModelCatalogEntry> _entries;

    public ModelCatalogStore(IOptions<ModelCatalogOptions> options)
    {
        var settings = options.Value;

        Directory.CreateDirectory(settings.StoragePath);
        _imageDirectory = Path.Combine(settings.StoragePath, "images");
        _metadataPath = Path.Combine(settings.StoragePath, "catalog.json");
        Directory.CreateDirectory(_imageDirectory);

        var catalogPath = Path.IsPathRooted(settings.CatalogPath)
            ? settings.CatalogPath
            : Path.Combine(AppContext.BaseDirectory, settings.CatalogPath);

        _validModelIds = LoadValidModelIds(catalogPath);
        _entries = LoadEntries(_metadataPath)
            .Where(entry => _validModelIds.Contains(entry.ModelId))
            .ToDictionary(entry => entry.ModelId);
    }

    public bool IsValidModel(int modelId)
    {
        return _validModelIds.Contains(modelId);
    }

    public IReadOnlyList<ModelCatalogEntry> GetEntries()
    {
        _lock.Wait();

        try
        {
            return _entries.Values
                .OrderBy(entry => entry.ModelId)
                .Select(Clone)
                .ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool TryGetEntry(int modelId, out ModelCatalogEntry? entry)
    {
        _lock.Wait();

        try
        {
            if (!_entries.TryGetValue(modelId, out var storedEntry))
            {
                entry = null;
                return false;
            }

            entry = Clone(storedEntry);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetImagePath(int modelId)
    {
        return Path.Combine(_imageDirectory, $"{modelId}.webp");
    }

    public async Task<ModelCatalogEntry> ReplaceAsync(
        int modelId,
        byte[] imageBytes,
        int width,
        int height,
        ulong userId,
        string username,
        CancellationToken cancellationToken)
    {
        if (!IsValidModel(modelId))
            throw new ArgumentOutOfRangeException(nameof(modelId));

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var imagePath = GetImagePath(modelId);
            var temporaryImagePath = $"{imagePath}.{Guid.NewGuid():N}.tmp";
            var temporaryMetadataPath = $"{_metadataPath}.{Guid.NewGuid():N}.tmp";

            try
            {
                await File.WriteAllBytesAsync(temporaryImagePath, imageBytes, cancellationToken);
                File.Move(temporaryImagePath, imagePath, true);

                var entry = new ModelCatalogEntry
                {
                    ModelId = modelId,
                    UpdatedByUserId = userId,
                    UpdatedByUsername = username,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Width = width,
                    Height = height,
                    StoredBytes = imageBytes.LongLength
                };

                var updatedEntries = new Dictionary<int, ModelCatalogEntry>(_entries)
                {
                    [modelId] = entry
                };

                await using (var stream = File.Create(temporaryMetadataPath))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        updatedEntries.Values.OrderBy(value => value.ModelId),
                        cancellationToken: cancellationToken);
                }

                File.Move(temporaryMetadataPath, _metadataPath, true);
                _entries = updatedEntries;

                return Clone(entry);
            }
            finally
            {
                File.Delete(temporaryImagePath);
                File.Delete(temporaryMetadataPath);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static HashSet<int> LoadValidModelIds(string catalogPath)
    {
        if (!File.Exists(catalogPath))
            throw new InvalidOperationException($"Model catalog was not found at {catalogPath}.");

        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));

        return document.RootElement
            .GetProperty("models")
            .EnumerateArray()
            .Select(model => model.GetProperty("id").GetInt32())
            .ToHashSet();
    }

    private static IEnumerable<ModelCatalogEntry> LoadEntries(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<ModelCatalogEntry[]>(File.ReadAllText(metadataPath)) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Model catalog metadata at {metadataPath} is invalid.", exception);
        }
    }

    private static ModelCatalogEntry Clone(ModelCatalogEntry entry)
    {
        return new ModelCatalogEntry
        {
            ModelId = entry.ModelId,
            UpdatedByUserId = entry.UpdatedByUserId,
            UpdatedByUsername = entry.UpdatedByUsername,
            UpdatedAt = entry.UpdatedAt,
            Width = entry.Width,
            Height = entry.Height,
            StoredBytes = entry.StoredBytes
        };
    }
}
