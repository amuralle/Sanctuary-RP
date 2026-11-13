using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class HousingPacketUpdateFixturePosition : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 51;
    public ulong NpcGuid;
    public Vector4 Position;
    public Quaternion Rotation;

    public HousingPacketUpdateFixturePosition() : base(OpCode) { }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        Write(writer);
        writer.Write(NpcGuid);
        writer.Write(Position);
        writer.Write(Rotation);
        return writer.Buffer;
    }
}
