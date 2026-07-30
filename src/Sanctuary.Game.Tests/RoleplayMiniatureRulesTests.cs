using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class RoleplayMiniatureRulesTests
{
    [TestMethod]
    public void IsValidName_AcceptsReadableNames()
    {
        Assert.IsTrue(RoleplayMiniatureRules.IsValidName("Captain Bramblehook"));
        Assert.IsTrue(RoleplayMiniatureRules.IsValidName("Guard-Captain's Echo"));
    }

    [TestMethod]
    public void IsValidName_RejectsBlankMultilineAndOversizedNames()
    {
        Assert.IsFalse(RoleplayMiniatureRules.IsValidName(" "));
        Assert.IsFalse(RoleplayMiniatureRules.IsValidName("Guard\n!mini clear all"));
        Assert.IsFalse(RoleplayMiniatureRules.IsValidName(
            new string('a', RoleplayMiniatureRules.MaximumNameLength + 1)));
    }

    [TestMethod]
    public void TryNormalizeGroup_NormalizesCommandSyntax()
    {
        Assert.IsTrue(RoleplayMiniatureRules.TryNormalizeGroup("@City_Guards", out var group));
        Assert.AreEqual("city_guards", group);
    }

    [TestMethod]
    public void TryNormalizeGroup_RejectsUnsafeLabels()
    {
        Assert.IsFalse(RoleplayMiniatureRules.TryNormalizeGroup("@city guards", out _));
        Assert.IsFalse(RoleplayMiniatureRules.TryNormalizeGroup("@guards\nall", out _));
        Assert.IsFalse(RoleplayMiniatureRules.TryNormalizeGroup(
            new string('a', RoleplayMiniatureRules.MaximumGroupLength + 1), out _));
    }
}
