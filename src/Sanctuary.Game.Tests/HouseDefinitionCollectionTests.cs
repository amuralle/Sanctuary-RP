using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HouseDefinitionCollectionTests
{
    [TestMethod]
    public void Load_ReplacesChangedDefinitionsAndRemovesDeletedEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"houses-{Guid.NewGuid():N}.json");
        var houses = new HouseDefinitionCollection(NullLogger.Instance);

        try
        {
            File.WriteAllText(path, "[{\"Id\":1,\"ZoneId\":100},{\"Id\":2,\"ZoneId\":101}]");
            Assert.IsTrue(houses.Load(path));

            File.WriteAllText(path, "[{\"Id\":1,\"ZoneId\":100,\"SpawnPosition\":[560,0,545.5]}]");
            Assert.IsTrue(houses.Load(path));

            Assert.AreEqual(1, houses.Count);
            Assert.AreEqual(560f, houses[1].SpawnPosition.X);
            Assert.IsFalse(houses.ContainsKey(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("[null]")]
    [DataRow("[{\"Id\":0,\"ZoneId\":100}]")]
    [DataRow("[{\"Id\":1073741824,\"ZoneId\":100}]")]
    [DataRow("[{\"Id\":1,\"ZoneId\":0}]")]
    [DataRow("[{\"Id\":1,\"ZoneId\":100},{\"Id\":1,\"ZoneId\":101}]")]
    [DataRow("[{\"Id\":1,\"ZoneId\":100},{\"Id\":2,\"ZoneId\":100}]")]
    [DataRow("invalid json")]
    public void Load_InvalidReloadPreservesTheExistingCatalog(string invalidJson)
    {
        var path = Path.Combine(Path.GetTempPath(), $"houses-{Guid.NewGuid():N}.json");
        var houses = new HouseDefinitionCollection(NullLogger.Instance);

        try
        {
            File.WriteAllText(path, "[{\"Id\":1,\"ZoneId\":100,\"NameId\":6514}]");
            Assert.IsTrue(houses.Load(path));
            var original = houses[1];

            File.WriteAllText(path, invalidJson);
            Assert.IsFalse(houses.Load(path));

            Assert.AreEqual(1, houses.Count);
            Assert.AreSame(original, houses[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_AllowsDefinitionIdsBeyondTheRetiredEightBitHouseGuidLayout()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[{\"Id\":256,\"ZoneId\":100}]");
            var houses = new HouseDefinitionCollection(NullLogger.Instance);
            Assert.IsTrue(houses.Load(path));
            Assert.AreEqual(100, houses[256].ZoneId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
