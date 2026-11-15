using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class ClientHousingPacketPickupFixture : BaseHousingPacket, IDeserializable<ClientHousingPacketPickupFixture>
{
    public new const short OpCode = 3;
    public ulong FixtureGuid;

    public ClientHousingPacketPickupFixture() : base(OpCode) { }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketPickupFixture value)
    {
        value = new();
        var reader = new PacketReader(data);
        return value.TryRead(ref reader) && reader.TryRead(out value.FixtureGuid) && reader.RemainingLength == 0;
    }
}
