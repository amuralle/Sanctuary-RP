using System;
using System.Numerics;
using System.Text;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class ClientHousingPacketSaveFixture : BaseHousingPacket, IDeserializable<ClientHousingPacketSaveFixture>
{
    public new const short OpCode = 5;
    public ulong FixtureGuid;
    public Vector4 Position;
    public Quaternion Rotation;
    public int Unknown;
    public CustomizationDetail Customization = new();
    public float Scale;

    public ClientHousingPacketSaveFixture() : base(OpCode) { }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketSaveFixture value)
    {
        value = new();
        if (data.Length > 4096)
            return false;
        var reader = new PacketReader(data);
        return value.TryRead(ref reader) && reader.TryRead(out value.FixtureGuid)
            // Native serializer 00abf620 writes scale after customization, unlike PlaceFixture.
            && reader.TryRead(out value.Position) && reader.TryRead(out value.Rotation) && reader.TryRead(out value.Unknown)
            && reader.TryRead(out value.Customization.Type) && TryReadText(ref reader, out value.Customization.TextureAlias)
            && TryReadText(ref reader, out value.Customization.TintAlias) && reader.TryRead(out value.Customization.TintId)
            && TryReadText(ref reader, out value.Customization.TextureOverride) && reader.TryRead(out value.Scale)
            && reader.RemainingLength == 0;
    }

    private static bool TryReadText(ref PacketReader reader, out string? value)
    {
        value = null;
        if (!reader.TryRead(out int length) || length < 0 || length > 1024 || !reader.TryReadExact(length, out var bytes))
            return false;
        value = Encoding.UTF8.GetString(bytes);
        return true;
    }
}
