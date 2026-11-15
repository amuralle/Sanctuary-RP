using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class AtomicHousingCatalogTests
{
    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(ResourceManager.BaseDirectory);

    [TestMethod]
    public void Replacement_PreservesExistingEnumeratorsAndRejectsDuplicateKeysBeforePublication()
    {
        var catalog = new TestCatalog();
        catalog.Replace([new(1, 10), new(2, 10)]);
        using var before = ((IEnumerable<KeyValuePair<int, int>>)catalog).GetEnumerator();
        catalog.Replace([new(3, 20)]);
        var previous = new List<int>();
        while (before.MoveNext())
            previous.Add(before.Current.Value);
        CollectionAssert.AreEquivalent(new[] { 10, 10 }, previous);
        Assert.AreEqual(1, catalog.Count);
        Assert.AreEqual(20, catalog[3]);
        Assert.ThrowsExactly<ArgumentException>(() => catalog.Replace([new(1, 30), new(1, 40)]));
        Assert.AreEqual(1, catalog.Count);
        Assert.AreEqual(20, catalog[3]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentReload_ReadersOnlySeeCompleteOldOrNewHousingCatalog(bool fixtures)
    {
        var first = Path.GetTempFileName();
        var second = Path.GetTempFileName();
        using var cancellation = new CancellationTokenSource();
        using var readerReady = new ManualResetEventSlim();
        Task? reader = null;
        try
        {
            var houses = new HouseDefinitionCollection(NullLogger.Instance);
            var furniture = new HousingFixtureDefinitionCollection(NullLogger.Instance);
            Func<string, bool> load = fixtures ? path => furniture.Load(path) : houses.Load;
            Func<int[]> snapshot = fixtures
                ? () => furniture.Values.Select(entry => entry.ModelId).ToArray()
                : () => houses.Values.Select(entry => entry.NameId).ToArray();
            string Json(int generation) => fixtures
                ? JsonSerializer.Serialize(Enumerable.Range(1, 256).Select(id => new HousingFixtureDefinition { ItemDefinitionId = id, ModelId = generation }))
                : JsonSerializer.Serialize(Enumerable.Range(1, 256).Select(id => new HouseDefinition { Id = id, ZoneId = id, NameId = generation }));
            File.WriteAllText(first, Json(1));
            File.WriteAllText(second, Json(2));
            Assert.IsTrue(load(first));
            reader = Task.Factory.StartNew(() =>
            {
                do
                {
                    var values = snapshot();
                    Assert.AreEqual(256, values.Length, "Reload published an empty or partial catalog.");
                    Assert.AreEqual(1, values.Distinct().Count(), "Reload exposed a mixture of catalog generations.");
                    readerReady.Set();
                } while (!cancellation.IsCancellationRequested);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.IsTrue(readerReady.Wait(TimeSpan.FromSeconds(5)));
            for (var index = 0; index < 100; index++)
                Assert.IsTrue(load(index % 2 == 0 ? second : first));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                if (reader is not null)
                    await reader.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                File.Delete(first);
                File.Delete(second);
            }
        }
    }

    [TestMethod]
    [DataRow(1, 2)] // Missing model.
    [DataRow(2, 1)] // Missing item.
    public void InvalidReferenceReload_KeepsTheExistingFurnitureCatalog(int itemId, int modelId)
    {
        var path = Path.GetTempFileName();
        using var resources = new ResourceManager(NullLogger<ResourceManager>.Instance);
        try
        {
            resources.ClientItemDefinitions.Add(1, new ClientItemDefinition { Id = 1, Type = 29, ModelName = "chair.adr" });
            resources.Models.Add(1, new ModelDefinition { Id = 1, ModelFileName = "chair.adr" });
            File.WriteAllText(path, "[{\"ItemDefinitionId\":1,\"ModelId\":1}]");
            Assert.IsTrue(resources.LoadHousingFixtures(path));
            var original = resources.HousingFixtures[1];
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { new HousingFixtureDefinition { ItemDefinitionId = itemId, ModelId = modelId } }));
            Assert.IsFalse(resources.LoadHousingFixtures(path));
            Assert.AreSame(original, resources.HousingFixtures[1]);
            Assert.AreEqual(1, resources.HousingFixtures.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WrongTypeOrModelReload_IsRejectedBeforePublication()
    {
        var path = Path.GetTempFileName();
        using var resources = new ResourceManager(NullLogger<ResourceManager>.Instance);
        try
        {
            var item = new ClientItemDefinition { Id = 1, Type = 29, ModelName = "chair.adr" };
            resources.ClientItemDefinitions.Add(1, item);
            resources.Models.Add(1, new ModelDefinition { Id = 1, ModelFileName = "CHAIR.ADR" });
            File.WriteAllText(path, "[{\"ItemDefinitionId\":1,\"ModelId\":1}]");
            Assert.IsTrue(resources.LoadHousingFixtures(path));
            var original = resources.HousingFixtures[1];
            item.Type = 1;
            Assert.IsFalse(resources.LoadHousingFixtures(path));
            item.Type = 29;
            item.ModelName = "table.adr";
            Assert.IsFalse(resources.LoadHousingFixtures(path));
            Assert.AreSame(original, resources.HousingFixtures[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class TestCatalog : ObservableConcurrentDictionary<int, int>
    {
        public void Replace(IEnumerable<KeyValuePair<int, int>> entries) => ReplaceAll(entries);
    }
}
