using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class PlayerUpdatePacketUpdateTemporaryAppearance : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 14;

    public int TemporaryAppearance;
    public ulong Guid;

    public PlayerUpdatePacketUpdateTemporaryAppearance() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);
        writer.Write(TemporaryAppearance);
        writer.Write(Guid);

        return writer.Buffer;
    }
}
