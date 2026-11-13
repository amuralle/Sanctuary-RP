using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingFixtureCatalogTests
{
    [TestMethod]
    public void ZoneNpcDefinitions_DoNotCollideWithUpstreamNpcsAndAllSpawnsResolve()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Resources"));
        var npcs = new NpcDefinitionCollection(NullLogger.Instance);
        var zones = new ZoneNpcsCollection(NullLogger.Instance);
        Assert.IsTrue(npcs.Load(Path.Combine(path, "Npcs.json")));
        Assert.IsTrue(zones.Load(Path.Combine(path, "Zones", "Npcs")));
        Assert.IsTrue(zones.AddDefinitionsTo(npcs), "Zone-local definitions collide with the upstream NPC catalog.");
        foreach (var (name, zone) in zones)
        {
            foreach (var spawn in zone.Spawns)
                Assert.IsTrue(npcs.ContainsKey(spawn.NpcId), $"Zone {name} has an unknown spawn {spawn.NpcId}.");
        }
    }

    [TestMethod]
    public void HouseBuildAreas_KeepTheCarriedForwardFarmBoundsOrdered()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Resources"));
        var houses = new HouseDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(houses.Load(Path.Combine(path, "Houses.json")));
        foreach (var house in houses.Values)
        {
            Assert.IsTrue(house.BuildAreas.All(area => float.IsFinite(area.Min.X) && float.IsFinite(area.Min.Y)
                && float.IsFinite(area.Min.Z) && float.IsFinite(area.Max.X) && float.IsFinite(area.Max.Y)
                && float.IsFinite(area.Max.Z) && area.Min.X < area.Max.X && area.Min.Y < area.Max.Y
                && area.Min.Z < area.Max.Z), $"House {house.Id} has invalid build bounds.");
        }
    }

    [TestMethod]
    public void SupportedFurniture_HasInventoryDefinitionsAndMatchingWorldModels()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Resources"));
        var fixtures = new HousingFixtureDefinitionCollection(NullLogger.Instance);
        var items = new ClientItemDefinitionCollection(NullLogger.Instance);
        var models = new ModelDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(fixtures.Load(Path.Combine(path, "HousingFixtures.json")));
        Assert.IsTrue(items.Load(Path.Combine(path, "ClientItemDefinitions.json")));
        Assert.IsTrue(models.Load(Path.Combine(path, "Models.txt")));

        foreach (var fixture in fixtures.Values)
        {
            Assert.IsTrue(items.TryGetValue(fixture.ItemDefinitionId, out var item), $"Missing item {fixture.ItemDefinitionId}.");
            Assert.AreEqual(29, item.Type, $"Item {item.Id} is not furniture.");
            Assert.IsTrue(models.TryGetValue(fixture.ModelId, out var model), $"Missing model {fixture.ModelId}.");
            Assert.IsTrue(string.Equals(item.ModelName, model.ModelFileName, StringComparison.OrdinalIgnoreCase),
                $"Item {item.Id} uses {item.ModelName}, not fixture model {model.ModelFileName}.");
            var client = fixture.CreateClientDefinition();
            Assert.AreEqual(1, client.Unknown3, "Ordinary furniture must use the native mouse-placement path.");
            Assert.AreEqual(item.Id, client.ItemDefinitionId);
            Assert.AreEqual(fixture.ModelId, client.ModelId);
        }
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("[{\"ItemDefinitionId\":1,\"ModelId\":0}]")]
    [DataRow("[{\"ItemDefinitionId\":1,\"ModelId\":2},{\"ItemDefinitionId\":1,\"ModelId\":3}]")]
    [DataRow("not json")]
    public void InvalidReload_KeepsTheExistingCatalog(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var fixtures = new HousingFixtureDefinitionCollection(NullLogger.Instance);
            var previous = new HousingFixtureDefinition { ItemDefinitionId = 27943, ModelId = 3895 };
            fixtures.Add(previous.ItemDefinitionId, previous);
            Assert.IsFalse(fixtures.Load(path));
            Assert.AreEqual(1, fixtures.Count);
            Assert.AreSame(previous, fixtures[27943]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ValidReload_ReplacesChangedAndRemovedDefinitions()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[{\"ItemDefinitionId\":1,\"ModelId\":3}]");
            var fixtures = new HousingFixtureDefinitionCollection(NullLogger.Instance);
            fixtures.Add(1, new HousingFixtureDefinition { ItemDefinitionId = 1, ModelId = 2 });
            fixtures.Add(2, new HousingFixtureDefinition { ItemDefinitionId = 2, ModelId = 4 });
            Assert.IsTrue(fixtures.Load(path));
            Assert.AreEqual(1, fixtures.Count);
            Assert.AreEqual(3, fixtures[1].ModelId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
