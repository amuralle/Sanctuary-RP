using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class CollectionNodeTypeDefinitionTests
{
    [TestMethod]
    public void Load_RejectsNonPositiveModelId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Key\":\"test\",\"Name\":\"Test\",\"ModelId\":0}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsFalse(definitions.Load(path));
            Assert.AreEqual(0, definitions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_PreservesMultipleCompositeEffects()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Key\":\"water\",\"Name\":\"Sparkling Water\",\"ModelId\":271," +
            "\"CompositeEffectId\":15381,\"ExtraCompositeEffectIds\":[5742]}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsTrue(definitions.Load(path));
            CollectionAssert.AreEqual(new[] { 5742 }, definitions["water"].ExtraCompositeEffectIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_RejectsInvalidExtraCompositeEffect()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Key\":\"water\",\"Name\":\"Sparkling Water\",\"ModelId\":271," +
            "\"ExtraCompositeEffectIds\":[0]}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsFalse(definitions.Load(path));
            Assert.AreEqual(0, definitions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

}
