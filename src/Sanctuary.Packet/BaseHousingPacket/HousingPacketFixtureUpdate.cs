using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public sealed class HousingPacketFixtureUpdate : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 40;
    public FixtureInstance Instance = new();
    public FixtureInstanceInfo Info = new();
    public FixtureDefinition Definition = new();
    public CouplingDisplayData CouplingDisplay = new();
    public int Unknown;

    public HousingPacketFixtureUpdate() : base(OpCode) { }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        Write(writer);
        Instance.Serialize(writer);
        Info.Serialize(writer);
        Definition.Serialize(writer);
        // The update carries a second coupling list without its ID (00ac2180).
        writer.Write(CouplingDisplay.NpcGuids);
        writer.Write(CouplingDisplay.CompositeEffect);
        writer.Write(CouplingDisplay.EffectType);
        writer.Write(Unknown);
        return writer.Buffer;
    }
}
