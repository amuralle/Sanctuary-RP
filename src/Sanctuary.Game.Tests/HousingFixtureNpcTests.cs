using System;
using System.Buffers.Binary;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingFixtureNpcTests
{
    [TestMethod]
    public void FixtureActor_IdleUpdatesDoNotRunNpcSimulationOrChangeItsTransform()
    {
        using var zone = TestZone.Create();
        var saved = new DbHouseFixture { Id = 1, PositionX = 10, PositionZ = 20, Scale = 2, RotationX = 0.5f };
        var actor = new HousingFixtureNpc(zone, 100_000_000_000UL, saved,
            new HousingFixtureDefinition(), new ClientItemDefinition());
        actor.UpdateEveryTickAction = () => Assert.Fail("Static furniture ran an NPC tick callback.");
        actor.UpdateEverySecondAction = () => Assert.Fail("Static furniture ran an NPC second callback.");
        var before = actor.GetFixturePositionPacket().Serialize();

        IEntity entity = actor;
        for (var index = 0; index < 1000; index++)
        {
            entity.UpdateEveryTick();
            entity.UpdateEverySecond();
        }

        Assert.IsFalse(actor.TracksVisibleNpcs);
        Assert.AreEqual(2f, actor.Scale);
        CollectionAssert.AreEqual(before, actor.GetFixturePositionPacket().Serialize());
    }

    [TestMethod]
    public void FixtureActor_LinksTheSavedFixtureToTheStaticWorldModel()
    {
        using var zone = TestZone.Create();
        const ulong npcGuid = 100_000_000_000UL;
        var saved = new DbHouseFixture
        {
            Id = 1, ItemId = 82, ItemDefinitionId = 27943, Tint = 231,
            PositionX = 62.5f, PositionY = 4.675f, PositionZ = 61.75f,
            RotationX = -MathF.PI / 12, Scale = 1
        };
        var actor = new HousingFixtureNpc(zone, npcGuid, saved,
            new HousingFixtureDefinition { ItemDefinitionId = 27943, ModelId = 3895 },
            new ClientItemDefinition { TextureAlias = "chair-texture", TintAlias = "dyetint" });
        var instance = HousingFixtureManager.CreateClientInstance(saved, 20760UL, actor.Guid);
        var spawn = actor.GetAddNpcPacket();

        Assert.IsFalse(actor.Visible);
        Assert.AreEqual(npcGuid, instance.NpcGuid);
        Assert.AreEqual(instance.NpcGuid, spawn.Guid);
        Assert.AreEqual(GuidHelper.GetFixtureGuid(1), instance.Guid);
        Assert.AreEqual(3895, spawn.ModelId);
        Assert.AreEqual(instance.Position, spawn.Position);
        Assert.AreEqual(231, spawn.TintId);
        Assert.AreEqual("chair-texture", spawn.TextureAlias);
        Assert.IsTrue(spawn.HasTilt);
        Assert.AreEqual(0, spawn.MovementType);
        Assert.AreEqual(saved.RotationX, spawn.Tilt.X);
        Assert.AreEqual(1f, instance.Tilt.Y, 0.00001f);
        Assert.IsTrue(spawn.IsInteractable);
        Assert.IsTrue(actor.Collidable);
        Assert.IsFalse(spawn.DisableCollision);
        Assert.IsTrue(spawn.HideNamePlate);

        actor.OnAddVisibleNpcs(new Npc(zone) { Guid = npcGuid + 1 });
        Assert.AreEqual(0, actor.VisibleNpcs.Count);
        var observer = new Player(zone, null!, null!, null!) { Guid = 81 };
        actor.OnAddVisiblePlayers(observer);
        Assert.IsTrue(actor.VisiblePlayers.ContainsKey(observer.Guid));
        actor.OnRemoveVisiblePlayers(observer);

        saved.PositionX = 64;
        saved.RotationX = 0;
        saved.RotationY = 0.2f;
        saved.Scale = 2;
        actor.ApplyFixture(saved);
        var update = actor.GetFixturePositionPacket();
        var bytes = update.Serialize();
        Assert.AreEqual(npcGuid, update.NpcGuid);
        Assert.AreEqual(64f, update.Position.X);
        Assert.AreEqual(new Quaternion(0, 0.2f, 0, 0), update.Rotation);
        Assert.AreEqual(2f, actor.Scale);
        Assert.AreEqual(new Vector4(0, 0.2f, 0, 0), actor.GetAddNpcPacket().Tilt);
        Assert.IsFalse(actor.GetAddNpcPacket().DisableCollision);

        // Native reader 00ab2d10: housing header, actor GUID, position and angle vector.
        Assert.AreEqual(44, bytes.Length);
        Assert.AreEqual((short)127, BinaryPrimitives.ReadInt16LittleEndian(bytes));
        Assert.AreEqual((short)51, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2)));
        Assert.AreEqual(npcGuid, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4)));
    }
}
