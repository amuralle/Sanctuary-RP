using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class ClientHousingPacketPlaceFixtureRequest : BaseHousingPacket, IDeserializable<ClientHousingPacketPlaceFixtureRequest>
{
    public new const short OpCode = 1;
    public int ItemId;
    public int Unknown2;
    public int Unknown3;
    public bool Unknown4;

    public ClientHousingPacketPlaceFixtureRequest() : base(OpCode) { }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketPlaceFixtureRequest value)
    {
        value = new();
        var reader = new PacketReader(data);
        return value.TryRead(ref reader) && reader.TryRead(out value.ItemId)
            && reader.TryRead(out value.Unknown2) && reader.TryRead(out value.Unknown3)
            && reader.TryRead(out value.Unknown4) && reader.RemainingLength == 0;
    }
}
