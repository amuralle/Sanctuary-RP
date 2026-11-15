using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public sealed class HousingPacketFixtureAsset : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 42;
    public int ModelId;
    // The client echoes this inventory ID in ClientHousingPacketPlaceFixture.
    public int ItemId;
    public FixtureDefinition Definition = new();
    public string? Unknown3;
    public string? Unknown4;
    public int Tint;
    public int Unknown5;
    public string? XmlData;
    public CouplingDisplayData CouplingDisplay = new();
    public bool PlaceImmediately;

    public HousingPacketFixtureAsset() : base(OpCode) { }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        Write(writer);
        writer.Write(ModelId);
        writer.Write(ItemId);
        Definition.Serialize(writer);
        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(Tint);
        writer.Write(Unknown5);
        writer.Write(XmlData);
        // Asset responses carry the coupling list without an ID (client reader 00ac1f50).
        writer.Write(CouplingDisplay.NpcGuids);
        writer.Write(CouplingDisplay.CompositeEffect);
        writer.Write(CouplingDisplay.EffectType);
        writer.Write(PlaceImmediately);
        return writer.Buffer;
    }
}
