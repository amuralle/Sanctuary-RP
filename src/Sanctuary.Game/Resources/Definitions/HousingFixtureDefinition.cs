using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class HousingFixtureDefinition
{
    public int ItemDefinitionId { get; set; }
    public int ModelId { get; set; }

    public FixtureDefinition CreateClientDefinition() => new()
    {
        Id = ItemDefinitionId,
        ItemDefinitionId = ItemDefinitionId,
        // The client skips mouse placement for type 0; ordinary furniture uses 1 (00abb630).
        Unknown3 = 1,
        ModelId = ModelId,
        Category = "Furniture",
        Unknown14 = 0.25f,
        Unknown15 = 4f
    };
}
