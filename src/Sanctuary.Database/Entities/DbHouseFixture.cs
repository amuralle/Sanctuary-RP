using System;

namespace Sanctuary.Database.Entities;

public sealed class DbHouseFixture
{
    public ulong Id { get; set; }
    public Guid PlacementId { get; set; }
    public ulong HouseId { get; set; }
    public ulong CharacterId { get; set; }
    public DbHouse House { get; set; } = null!;
    public int ItemId { get; set; }
    public DbItem Item { get; set; } = null!;
    public int ItemDefinitionId { get; set; }
    public int Tint { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float RotationW { get; set; }
    public float Scale { get; set; } = 1f;
}
