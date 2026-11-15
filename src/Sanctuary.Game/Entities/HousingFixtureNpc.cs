using System;
using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Core.Extensions;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Entities;

public sealed class HousingFixtureNpc : Npc
{
    private Quaternion _fixtureRotation;
    public int ItemDefinitionId { get; }
    public override bool TracksVisibleNpcs => false;

    public HousingFixtureNpc(IZone zone, ulong guid, DbHouseFixture fixture, HousingFixtureDefinition definition,
        ClientItemDefinition item) : base(zone)
    {
        ArgumentOutOfRangeException.ThrowIfZero(guid);
        Guid = guid;
        ItemDefinitionId = fixture.ItemDefinitionId;
        ModelId = definition.ModelId;
        NameId = item.NameId;
        TextureAlias = item.TextureAlias;
        TintAlias = item.TintAlias;
        TintId = fixture.Tint;
        HideNamePlate = true;
        HasCursor = false;
        // The native client uses this flag to register its physics body, not just its cursor (00963790).
        IsInteractable = true;
        ApplyFixture(fixture);
    }

    public void ApplyFixture(DbHouseFixture fixture)
    {
        // Housing rotations are heading/pitch/roll, not normalized quaternions.
        _fixtureRotation = new Quaternion(fixture.RotationX, fixture.RotationY, fixture.RotationZ, fixture.RotationW);
        Tilt = new Vector4(fixture.RotationX, fixture.RotationY, fixture.RotationZ, fixture.RotationW);
        Scale = fixture.Scale;
        UpdatePosition(new Vector4(fixture.PositionX, fixture.PositionY, fixture.PositionZ, 1),
            QuaternionExtensions.FromHeading(fixture.RotationX));
    }

    public HousingPacketUpdateFixturePosition GetFixturePositionPacket() => new()
    {
        NpcGuid = Guid,
        Position = Position,
        Rotation = _fixtureRotation
    };

    // Static furniture needs player observers, not a growing cache of every nearby decoration.
    public override void OnAddVisibleNpcs(params IEnumerable<Npc> npcs) { }

    public override void OnRemoveVisibleNpcs(params IEnumerable<Npc> npcs) { }

    // Editor saves move furniture directly; it has no autonomous movement or scripts.
    public override void UpdateEveryTick() { }

    public override void UpdateEverySecond() { }
}
