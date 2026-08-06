using System;

namespace Sanctuary.WebAPI.Models;

public sealed class ModelCatalogEntry
{
    public int ModelId { get; set; }
    public ulong UpdatedByUserId { get; set; }
    public required string UpdatedByUsername { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long StoredBytes { get; set; }
}
