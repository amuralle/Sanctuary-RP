using System;
using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class ClientHousingPacketPlaceFixture : BaseHousingPacket, IDeserializable<ClientHousingPacketPlaceFixture>
{
    public new const short OpCode = 2;
    public int ItemId;
    public Vector4 Position;
    public Quaternion Rotation;
    public float Scale;

    public ClientHousingPacketPlaceFixture() : base(OpCode) { }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketPlaceFixture value)
    {
        value = new();
        var reader = new PacketReader(data);
        return value.TryRead(ref reader) && reader.TryRead(out value.ItemId)
            && reader.TryRead(out value.Position) && reader.TryRead(out value.Rotation)
            && reader.TryRead(out value.Scale) && reader.RemainingLength == 0;
    }
}
