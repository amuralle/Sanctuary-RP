using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class PlayerUpdatePacketRemoveTemporaryAppearance : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 15;

    public ulong Guid;
    public int Unknown;

    public PlayerUpdatePacketRemoveTemporaryAppearance() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);
        writer.Write(Guid);
        writer.Write(Unknown);

        return writer.Buffer;
    }
}
