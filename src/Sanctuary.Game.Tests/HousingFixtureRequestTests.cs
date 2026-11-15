using System;
using System.Buffers.Binary;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingFixtureRequestTests
{
    [TestMethod]
    public void FixtureAsset_MatchesNativeReaderWithoutAnExtraCouplingId()
    {
        var asset = new HousingPacketFixtureAsset
        {
            ModelId = 3895,
            ItemId = 82,
            Definition = new FixtureDefinition { Id = 27943, ItemDefinitionId = 27943, ModelId = 3895 },
            CouplingDisplay = new CouplingDisplayData
            {
                Id = 123,
                NpcGuids = [456UL, 789UL],
                CompositeEffect = 321,
                EffectType = 2
            },
            PlaceImmediately = true
        };
        var data = asset.Serialize();

        // Native reader 00ac1f50: header, model, inventory ID, definition, aliases,
        // tint, unknown, XML, GUID list, effect, effect type, and final boolean.
        Assert.AreEqual(111, data.Length);
        Assert.AreEqual(3895, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)));
        Assert.AreEqual(82, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)));
        Assert.AreEqual(27943, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12)));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(82)));
        Assert.AreEqual(456UL, BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(86)));
        Assert.AreEqual(789UL, BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(94)));
        Assert.AreEqual(321, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(102)));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(106)));
        Assert.AreEqual((byte)1, data[110]);
    }

    [TestMethod]
    public void PlaceRequests_ReadTheInventoryIdRatherThanTheItemDefinition()
    {
        using var selection = new PacketWriter();
        new BaseHousingPacket(1).Write(selection);
        selection.Write(82);
        selection.Write(-1);
        selection.Write(-1);
        selection.Write(false);
        Assert.IsTrue(ClientHousingPacketPlaceFixtureRequest.TryDeserialize(selection.Buffer, out var requested));
        Assert.AreEqual(82, requested.ItemId);

        using var placement = new PacketWriter();
        new BaseHousingPacket(2).Write(placement);
        placement.Write(requested.ItemId);
        placement.Write(new Vector4(1, 2, 3, 1));
        placement.Write(Quaternion.Identity);
        placement.Write(1f);
        Assert.IsTrue(ClientHousingPacketPlaceFixture.TryDeserialize(placement.Buffer, out var placed));
        Assert.AreEqual(requested.ItemId, placed.ItemId);
    }

    [TestMethod]
    public void SaveRequest_ReadsScaleAfterCustomizationRatherThanThePrecedingInteger()
    {
        using var writer = new PacketWriter();
        new BaseHousingPacket(5).Write(writer);
        writer.Write(2UL);
        writer.Write(new Vector4(64.5f, 4.675f, 65.25f, 1));
        writer.Write(new Quaternion(-0.25f, 0.5f, 0.75f, 0));
        // Native sender 00ac8790 copies the integer separately from the final float scale.
        writer.Write(0);
        new CustomizationDetail
        {
            Type = 1, TextureAlias = "fun-hsg_chair_lodge_01-L1", TintAlias = "dyetint", TintId = 231
        }.Serialize(writer);
        writer.Write(1.5f);

        Assert.IsTrue(ClientHousingPacketSaveFixture.TryDeserialize(writer.Buffer, out var saved));
        Assert.AreEqual(2UL, saved.FixtureGuid);
        Assert.AreEqual(new Vector4(64.5f, 4.675f, 65.25f, 1), saved.Position);
        Assert.AreEqual(new Quaternion(-0.25f, 0.5f, 0.75f, 0), saved.Rotation);
        Assert.AreEqual(0, saved.Unknown);
        Assert.AreEqual(1, saved.Customization.Type);
        Assert.AreEqual("fun-hsg_chair_lodge_01-L1", saved.Customization.TextureAlias);
        Assert.AreEqual("dyetint", saved.Customization.TintAlias);
        Assert.AreEqual(231, saved.Customization.TintId);
        Assert.AreEqual(1.5f, saved.Scale);
    }

    [TestMethod]
    public void SaveRequest_RejectsEveryTruncatedPrefixAndTrailingBytes()
    {
        using var writer = new PacketWriter();
        new BaseHousingPacket(5).Write(writer);
        writer.Write(1UL);
        writer.Write(new Vector4(1, 2, 3, 1));
        writer.Write(Quaternion.Identity);
        writer.Write(0);
        new CustomizationDetail().Serialize(writer);
        writer.Write(1f);
        var data = writer.Buffer;
        Assert.IsTrue(ClientHousingPacketSaveFixture.TryDeserialize(data, out _));
        for (var length = 0; length < data.Length; length++)
            Assert.IsFalse(ClientHousingPacketSaveFixture.TryDeserialize(data.AsSpan(0, length), out _));
        Assert.IsFalse(ClientHousingPacketSaveFixture.TryDeserialize([.. data, 0], out _));
    }

    [TestMethod]
    public void SaveRequest_RejectsNegativeOrOversizedStringLengthsWithoutThrowing()
    {
        foreach (var length in new[] { -1, int.MinValue, int.MaxValue, 1025 })
        {
            using var writer = new PacketWriter();
            new BaseHousingPacket(5).Write(writer);
            writer.Write(1UL);
            writer.Write(Vector4.One);
            writer.Write(Quaternion.Identity);
            writer.Write(0);
            writer.Write(0);
            writer.Write(length);
            Assert.IsFalse(ClientHousingPacketSaveFixture.TryDeserialize(writer.Buffer, out _));
        }
    }
}
