using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class PlayerUpdatePacketSetAnimation : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 8;

    public ulong Guid;
    public int AnimationId;
    public int Variation = -1;
    public byte Flags;

    public PlayerUpdatePacketSetAnimation() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);
        writer.Write(Guid);
        writer.Write(AnimationId);
        writer.Write(Variation);
        writer.Write(Flags);

        return writer.Buffer;
    }
}
