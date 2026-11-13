using System;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Gateway.Handlers;

public static class HousingFixturePacketHandler
{
    public static bool HandleRequest(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketPlaceFixtureRequest.TryDeserialize(data, out var packet))
            return false;
        if (connection.Player.Zone is HousingZone zone)
            zone.RequestFixture(connection.Player, packet);
        return true;
    }

    public static bool HandlePlace(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketPlaceFixture.TryDeserialize(data, out var packet))
            return false;
        if (connection.Player.Zone is HousingZone zone)
            zone.PlaceFixture(connection.Player, packet);
        return true;
    }

    public static bool HandleSave(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketSaveFixture.TryDeserialize(data, out var packet))
            return false;
        if (connection.Player.Zone is HousingZone zone)
            zone.SaveFixture(connection.Player, packet);
        return true;
    }

    public static bool HandlePickup(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketPickupFixture.TryDeserialize(data, out var packet))
            return false;
        if (connection.Player.Zone is HousingZone zone)
            zone.PickupFixture(connection.Player, packet.FixtureGuid);
        return true;
    }
}
