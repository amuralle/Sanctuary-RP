using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.CollectionVolunteers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class CollectionVolunteerStoreTests
{
    private string _temporaryDirectory = null!;
    private CollectionVolunteerStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"sanctuary-volunteer-tests-{Guid.NewGuid():N}");
        _store = new CollectionVolunteerStore(
            Path.Combine(_temporaryDirectory, "placements.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    [TestMethod]
    public void AddPersistsNormalizedPlacement()
    {
        var result = _store.TryAdd(CreatePlacement(1, 10, " Mushrooms "), out var stored);

        Assert.AreEqual(CollectionVolunteerAddResult.Success, result);
        Assert.IsNotNull(stored);
        Assert.AreEqual(1, stored.Id);
        Assert.AreEqual("mushrooms", stored.Pool);

        var reloaded = new CollectionVolunteerStore(
            Path.Combine(_temporaryDirectory, "placements.json")).GetAll().Single();
        Assert.AreEqual(stored.Id, reloaded.Id);
        Assert.AreEqual(stored.SubmittedByCharacterName, reloaded.SubmittedByCharacterName);
    }

    [TestMethod]
    public void AddRejectsNearbyPointInSameZoneAndPool()
    {
        _store.TryAdd(CreatePlacement(1, 10, "mushrooms"), out _);
        var duplicate = CreatePlacement(2, 20, "mushrooms");
        duplicate.X += 1;

        var result = _store.TryAdd(duplicate, out var stored);

        Assert.AreEqual(CollectionVolunteerAddResult.Duplicate, result);
        Assert.IsNull(stored);
    }

    [TestMethod]
    public void DuplicateCheckKeepsZonesAndPoolsIndependent()
    {
        _store.TryAdd(CreatePlacement(1, 10, "mushrooms"), out _);
        var otherPool = CreatePlacement(2, 20, "rare-mushrooms");
        var otherZone = CreatePlacement(3, 30, "mushrooms");
        otherZone.ZoneId = 6;

        Assert.AreEqual(
            CollectionVolunteerAddResult.Success,
            _store.TryAdd(otherPool, out _));
        Assert.AreEqual(
            CollectionVolunteerAddResult.Success,
            _store.TryAdd(otherZone, out _));
    }

    [TestMethod]
    public void ReviewLocksPlacementFromContributorEdits()
    {
        _store.TryAdd(CreatePlacement(1, 10, "mushrooms"), out var stored);
        Assert.IsNotNull(stored);

        var reviewed = _store.TryUpdate(
            stored.Id,
            actorUserId: 99,
            actorUsername: "reviewer",
            actorIsAdmin: true,
            stored.Pool,
            stored.X,
            stored.Y,
            stored.Z,
            stored.Heading,
            stored.Note,
            CollectionVolunteerPlacementStatus.Accepted,
            out var updated);
        var contributorEdit = _store.TryUpdate(
            stored.Id,
            actorUserId: stored.SubmittedByUserId,
            actorUsername: "contributor",
            actorIsAdmin: false,
            stored.Pool,
            stored.X + 5,
            stored.Y,
            stored.Z,
            stored.Heading,
            stored.Note,
            CollectionVolunteerPlacementStatus.Pending,
            out _);

        Assert.IsTrue(reviewed);
        Assert.AreEqual(CollectionVolunteerPlacementStatus.Accepted, updated?.Status);
        Assert.AreEqual("reviewer", updated?.ReviewedByUsername);
        Assert.IsFalse(contributorEdit);
    }

    [TestMethod]
    public void AttachedHardPointPersistsAndLocksPhysicalPlacement()
    {
        _store.TryAdd(CreatePlacement(1, 10, "mushrooms"), out var stored);
        Assert.IsNotNull(stored);

        Assert.IsTrue(_store.TryAttachHardPoint(
            stored.Id, stored.SubmittedByUserId, 77, out var attached));
        Assert.AreEqual(77, attached?.HardPointId);

        var moved = _store.TryUpdate(
            stored.Id,
            actorUserId: 99,
            actorUsername: "reviewer",
            actorIsAdmin: true,
            stored.Pool,
            stored.X + 5,
            stored.Y,
            stored.Z,
            stored.Heading,
            stored.Note,
            CollectionVolunteerPlacementStatus.Pending,
            out _);

        Assert.IsFalse(moved);
        Assert.AreEqual(77, new CollectionVolunteerStore(
            Path.Combine(_temporaryDirectory, "placements.json")).GetAll().Single().HardPointId);
    }

    [TestMethod]
    public void AttachedHardPointMustBeDetachedBeforeDeletion()
    {
        _store.TryAdd(CreatePlacement(1, 10, "mushrooms"), out var stored);
        Assert.IsNotNull(stored);
        _store.TryAttachHardPoint(stored.Id, stored.SubmittedByUserId, 77, out _);

        Assert.IsFalse(_store.TryDelete(stored.Id, stored.SubmittedByUserId, actorIsAdmin: false));
        Assert.IsTrue(_store.TryDetachHardPoint(
            stored.Id, stored.SubmittedByUserId, 77, out var detached));
        Assert.IsNull(detached?.HardPointId);
        Assert.IsTrue(_store.TryDelete(stored.Id, stored.SubmittedByUserId, actorIsAdmin: false));
        Assert.AreEqual(0, _store.GetAll().Count);
    }

    [TestMethod]
    public void DeployedReaderLoadsZoneAndPoolFromResourcePath()
    {
        var zonePath = Path.Combine(_temporaryDirectory, "1");
        Directory.CreateDirectory(zonePath);
        File.WriteAllText(
            Path.Combine(zonePath, "briarwood-mushrooms.json"),
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    Id = 7,
                    Position = new[] { -803.5f, -50.01f, 1718.5f },
                    Heading = 1.4f
                }
            }));

        var placement = new DeployedCollectionPlacementReader(_temporaryDirectory)
            .GetAll()
            .Single();

        Assert.AreEqual(7, placement.Id);
        Assert.AreEqual(1, placement.ZoneId);
        Assert.AreEqual("briarwood-mushrooms", placement.Pool);
        Assert.AreEqual(-803.5f, placement.X);
        Assert.AreEqual(1718.5f, placement.Z);
    }

    private static CollectionVolunteerPlacement CreatePlacement(
        ulong userId,
        ulong characterId,
        string pool)
    {
        return new CollectionVolunteerPlacement
        {
            ZoneId = 5,
            Pool = pool,
            X = 100,
            Y = 20,
            Z = -40,
            Heading = 1.2f,
            Note = "Beside the blue tree",
            SubmittedByUserId = userId,
            SubmittedByCharacterId = characterId,
            SubmittedByCharacterName = $"Tester {characterId}",
            Status = CollectionVolunteerPlacementStatus.Pending
        };
    }
}
