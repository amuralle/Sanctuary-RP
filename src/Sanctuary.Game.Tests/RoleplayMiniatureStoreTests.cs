using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.RoleplayMiniatures;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class RoleplayMiniatureStoreTests
{
    [TestMethod]
    public void ReadsLegacyTwoComponentRotation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"miniature-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "snapshots.json");
            File.WriteAllText(path, "[{\"ZoneDefinitionId\":1,\"Id\":1,\"Position\":[1,2,3,1],\"Rotation\":[0.5,0.75]}]");
            var snapshot = new RoleplayMiniatureStore(path).GetForZone(1).Single();
            Assert.AreEqual(new Quaternion(0.5f, 0f, 0.75f, 0f), snapshot.Rotation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceZone_PreservesOtherScenesAndSurvivesReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"miniature-store-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "snapshots.json");
            var store = new RoleplayMiniatureStore(path);
            var first = new RoleplayMiniatureSnapshot
            {
                ZoneDefinitionId = 1, Id = 42, ModelId = 100, Name = "Guard", Group = "guards",
                OwnerName = "Test", Position = new Vector4(1, 2, 3, 1), Rotation = Quaternion.Identity
            };
            store.ReplaceZone(1, [first]);
            store.ReplaceZone(2, [new RoleplayMiniatureSnapshot
            {
                ZoneDefinitionId = 2, Id = 1, Name = "Visitor", Group = "default", OwnerName = "Test"
            }]);
            var loaded = new RoleplayMiniatureStore(path).GetForZone(1).Single();
            Assert.AreEqual(first.Position, loaded.Position);
            Assert.AreEqual(first.Rotation, loaded.Rotation);
            Assert.AreEqual(42, loaded.Id);
            loaded.Name = "Changed outside store";
            Assert.AreEqual("Guard", store.GetForZone(1).Single().Name);
            store.RemoveZone(1);
            Assert.AreEqual(0, new RoleplayMiniatureStore(path).GetForZone(1).Count);
            Assert.AreEqual("Visitor", store.GetForZone(2).Single().Name);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
