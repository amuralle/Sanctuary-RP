using System.Numerics;

namespace Sanctuary.Core.RoleplayMiniatures;

public sealed class RoleplayMiniatureSnapshot
{
    public int ZoneDefinitionId { get; set; }
    public int Id { get; set; }
    public int ModelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public ulong OwnerGuid { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public Vector4 Position { get; set; }
    public Quaternion Rotation { get; set; }
}
