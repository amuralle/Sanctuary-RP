using System.IO;

namespace Sanctuary.WebAPI.Options;

public sealed class ModelCatalogOptions
{
    public const string Section = "ModelCatalog";

    public string StoragePath { get; set; } = Path.Combine("Data", "ModelCatalog");
    public string DataProtectionPath { get; set; } = Path.Combine("Data", "DataProtection");
    public string CatalogPath { get; set; } = "CatalogModels.json";

    public long MaximumUploadBytes { get; set; } = 4 * 1024 * 1024;
    public long MaximumStoredBytes { get; set; } = 512 * 1024;
    public long MaximumSourcePixels { get; set; } = 40_000_000;

    public int MaximumWidth { get; set; } = 1280;
    public int MaximumHeight { get; set; } = 960;
}
