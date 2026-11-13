using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingFixtureManagerTests
{
    private string _path = null!;
    private Factory _factory = null!;
    private ResourceManager _resources = null!;
    private HousingFixtureManager _manager = null!;
    private ulong _characterId;
    private readonly CommandLog _commands = new();
    private static readonly Vector4 Position = new(10, 0, 10, 1);
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"housing-fixtures-{Guid.NewGuid():N}.db");
        _factory = new Factory(new DbContextOptionsBuilder().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=2")
            .AddInterceptors(_commands).Options);
        _resources = new ResourceManager(NullLogger<ResourceManager>.Instance);
        _resources.Houses.Add(1, new HouseDefinition
        {
            Id = 1, ZoneId = 100,
            BuildAreas = [new BoundingBox { Min = new(0, -10, 0, 0), Max = new(100, 100, 100, 0) }]
        });
        _resources.HousingFixtures.Add(27943, new HousingFixtureDefinition { ItemDefinitionId = 27943, ModelId = 3895 });

        using var db = _factory.CreateDbContext();
        db.Database.Migrate();
        var character = new DbCharacter
        {
            FirstName = "Editor", Head = "head", HeadId = 1, Hair = "hair", HairId = 1,
            SkinTone = "skin", SkinToneId = 1,
            User = new DbUser { Username = "fixture-test", Password = "not-used" },
            Items = [new DbItem { Id = 1, Definition = 27943, Count = 1, Tint = 231 }]
        };
        db.Characters.Add(character);
        db.SaveChanges();
        _characterId = character.Id;
        db.HousingGuids.Add(new DbHousingGuid { Id = 100 });
        db.Houses.Add(new DbHouse { Id = 100, Key = 100, ZoneId = 100, CharacterId = _characterId });
        db.SaveChanges();
        _manager = new HousingFixtureManager(_resources, _factory, NullLogger<HousingFixtureManager>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _resources.Dispose();
        File.Delete(_path);
    }

    [TestMethod]
    public void PlaceMoveAndPickup_PersistAndConserveInventory()
    {
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out var placed, out var remaining));
        Assert.AreEqual(0, remaining);
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out _, out _));

        var restarted = new HousingFixtureManager(_resources, _factory, NullLogger<HousingFixtureManager>.Instance);
        Assert.AreEqual(placed.Id, restarted.GetFixtures(_characterId, 100).Single().Id);
        var moved = new Vector4(25, 3, 40, 1);
        Assert.IsTrue(restarted.TryMove(_characterId, 100, GuidHelper.GetFixtureGuid(placed.Id), moved, Quaternion.Identity, 2, out _));
        Assert.AreEqual(25f, restarted.GetFixtures(_characterId, 100).Single().PositionX);

        Assert.IsTrue(restarted.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(placed.Id), out _, out remaining));
        Assert.AreEqual(1, remaining);
        Assert.IsFalse(restarted.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(placed.Id), out _, out _));
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(1, db.Items.Single().Count);
        Assert.AreEqual(0, db.HouseFixtures.Count());
    }

    [TestMethod]
    public void TwoOwnedHomesOfTheSameType_KeepTheirFixturesSeparate()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.HousingGuids.Add(new DbHousingGuid { Id = 200 });
            db.Houses.Add(new DbHouse { Id = 200, Key = 200, ZoneId = 100, CharacterId = _characterId });
            db.Items.Single().Count = 2;
            db.SaveChanges();
        }
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var first, out _));
        Assert.IsTrue(_manager.TryPlace(_characterId, 200, 1, Position, default, 1, out var second, out _));
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.AreEqual(first.Id, _manager.GetFixtures(_characterId, 100).Single().Id);
        Assert.AreEqual(second.Id, _manager.GetFixtures(_characterId, 200).Single().Id);
        Assert.IsFalse(_manager.TryMove(_characterId, 100, GuidHelper.GetFixtureGuid(second.Id), Position, default, 1, out _));
        Assert.IsFalse(_manager.TryPickup(_characterId, 200, GuidHelper.GetFixtureGuid(first.Id), out _, out _));
        Assert.IsTrue(_manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(first.Id), out _, out var remaining));
        Assert.AreEqual(1, remaining);
        Assert.AreEqual(1, _manager.GetFixtureCount(_characterId, 200));
    }

    [TestMethod]
    public void FixtureIdentity_UsesTheSharedHousingSequenceAndNativePrefix()
    {
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out _));
        Assert.IsTrue(fixture.Id > 100);
        var guid = GuidHelper.GetFixtureGuid(fixture.Id);
        Assert.IsTrue(GuidHelper.IsFixtureGuid(guid));
        Assert.AreEqual(fixture.Id, GuidHelper.GetFixtureId(guid));
        Assert.AreNotEqual(GuidHelper.GetHouseGuid(100), guid);
        Assert.IsFalse(_manager.TryPickup(_characterId, 100, fixture.Id, out _, out _), "A raw DB ID was accepted as a native GUID.");
        using var db = _factory.CreateDbContext();
        Assert.IsTrue(db.HousingGuids.Any(row => row.Id == fixture.Id));
    }

    [TestMethod]
    public void FixtureIdsAboveSignedIntRange_StillLoadMoveAndReturnInventory()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.HousingGuids.Add(new DbHousingGuid { Id = (ulong)int.MaxValue + 1 });
            db.SaveChanges();
        }
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out _));
        Assert.IsTrue(fixture.Id > int.MaxValue);
        Assert.AreEqual(fixture.Id, _manager.GetFixtures(_characterId, 100).Single().Id);
        var guid = GuidHelper.GetFixtureGuid(fixture.Id);
        Assert.IsTrue(_manager.TryMove(_characterId, 100, guid, Position, default, 2, out _));
        Assert.IsTrue(_manager.TryPickup(_characterId, 100, guid, out _, out var remaining));
        Assert.AreEqual(1, remaining);
    }

    [TestMethod]
    public void UnrepresentableSnapshotId_DoesNotConsumeInventoryOrLeakAnIdentity()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.HousingGuids.Add(new DbHousingGuid { Id = uint.MaxValue });
            db.SaveChanges();
        }
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out _, out _));
        using var verify = _factory.CreateDbContext();
        Assert.AreEqual(1, verify.Items.Single().Count);
        Assert.AreEqual(0, verify.HouseFixtures.Count());
        Assert.AreEqual(2, verify.HousingGuids.Count());
    }

    [TestMethod]
    public void PreviewHouseIdentity_RejectsAllPersistentEditsEvenWithoutAPlayerFlag()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Items.Single().Count = 2;
            db.SaveChanges();
        }
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out _));
        using (var db = _factory.CreateDbContext())
        {
            db.Houses.Single().Key = 0;
            db.SaveChanges();
        }
        var guid = GuidHelper.GetFixtureGuid(fixture.Id);
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out _, out _));
        Assert.IsFalse(_manager.TryMove(_characterId, 100, guid, Position, default, 2, out _));
        Assert.IsFalse(_manager.TryPickup(_characterId, 100, guid, out _, out _));
        using var verify = _factory.CreateDbContext();
        Assert.AreEqual(1, verify.Items.Single().Count);
        Assert.AreEqual(1, verify.HouseFixtures.Count());
        Assert.AreEqual(1f, verify.HouseFixtures.Single().Scale);
    }

    [TestMethod]
    public void ReplayedPlacement_ReturnsTheSameFixtureWithoutConsumingAnotherUnit()
    {
        var operationId = Guid.NewGuid();
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out var first, out _, operationId));
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out var repeated, out _, operationId));
        Assert.AreEqual(first.Id, repeated.Id);
        Assert.AreEqual(1, _manager.GetFixtures(_characterId, 100).Count);
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(0, db.Items.Single().Count);
    }

    [TestMethod]
    public void ForeignOwnerOrHouse_CannotMoveOrReturnAnotherPlacement()
    {
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out var placed, out _));
        Assert.IsFalse(_manager.TryMove(_characterId + 1, 100, GuidHelper.GetFixtureGuid(placed.Id), Position, Quaternion.Identity, 1, out _));
        Assert.IsFalse(_manager.TryMove(_characterId, 101, GuidHelper.GetFixtureGuid(placed.Id), Position, Quaternion.Identity, 1, out _));
        Assert.IsFalse(_manager.TryPickup(_characterId + 1, 100, GuidHelper.GetFixtureGuid(placed.Id), out _, out _));
        Assert.IsFalse(_manager.TryPickup(_characterId, 101, GuidHelper.GetFixtureGuid(placed.Id), out _, out _));
        Assert.IsFalse(_manager.TryPickup(_characterId, 100, ulong.MaxValue, out _, out _));
        Assert.AreEqual(1, _manager.GetFixtures(_characterId, 100).Count);
    }

    [TestMethod]
    public void InvalidTransforms_DoNotConsumeItems()
    {
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, new(float.NaN, 0, 10, 1), Quaternion.Identity, 1, out _, out _));
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, new(101, 0, 10, 1), Quaternion.Identity, 1, out _, out _));
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, new Quaternion(0, float.NaN, 0, 0), 1, out _, out _));
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, float.PositiveInfinity, out _, out _));
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, -1, out _, out _));
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(1, db.Items.Single().Count);
        Assert.AreEqual(0, db.HouseFixtures.Count());
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-0.26179937f)]
    [DataRow(3.1415927f)]
    public void ClientAngleRotations_PersistWithoutQuaternionNormalization(float heading)
    {
        var rotation = new Quaternion(heading, 0, 0, 0);
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, rotation, 1, out var placed, out var remaining));
        Assert.AreEqual(0, remaining);
        var saved = _manager.GetFixtures(_characterId, 100).Single();
        Assert.AreEqual(rotation.X, saved.RotationX);
        Assert.AreEqual(0f, saved.RotationW);
        Assert.IsTrue(_manager.TryMove(_characterId, 100, GuidHelper.GetFixtureGuid(placed.Id), Position, default, 1, out _));
        Assert.IsTrue(_manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(placed.Id), out _, out remaining));
        Assert.AreEqual(1, remaining);
    }

    [TestMethod]
    public void FailedPersistence_RollsBackTheInventoryReservation()
    {
        using (var db = _factory.CreateDbContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_fixture BEFORE INSERT ON HouseFixtures BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out _, out _));
        using var verify = _factory.CreateDbContext();
        Assert.AreEqual(1, verify.Items.Single().Count);
        Assert.AreEqual(1, verify.Houses.Count());
        Assert.AreEqual(1, verify.HousingGuids.Count(), "A failed placement leaked a housing identity.");
    }

    [TestMethod]
    public async Task ConcurrentPlacements_CannotConsumeTheLastUnitTwice()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(() =>
            _manager.TryPlace(_characterId, 100, 1, Position, Quaternion.Identity, 1, out _, out _))));
        Assert.AreEqual(1, attempts.Count(success => success));
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(0, db.Items.Single().Count);
        Assert.AreEqual(1, db.HouseFixtures.Count());
    }

    [TestMethod]
    public async Task ConcurrentReplays_OnlyCreateOneFixture()
    {
        var operationId = Guid.NewGuid();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            _manager.TryPlace(_characterId, 100, 1, Position, default, 1, out _, out _, operationId))));
        Assert.IsTrue(attempts.All(success => success));
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(1, db.HouseFixtures.Count());
        Assert.AreEqual(0, db.Items.Single().Count);
    }

    [TestMethod]
    public async Task ConcurrentPickups_ReturnTheUnitExactlyOnce()
    {
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out _));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            _manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), out _, out _))));
        Assert.AreEqual(1, attempts.Count(success => success));
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(0, db.HouseFixtures.Count());
        Assert.AreEqual(1, db.Items.Single().Count);
    }

    [TestMethod]
    public void FailedPickup_RollsBackInventoryAndKeepsFixture()
    {
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out _));
        using (var db = _factory.CreateDbContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_pickup BEFORE DELETE ON HouseFixtures BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        Assert.IsFalse(_manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), out _, out _));
        using var verify = _factory.CreateDbContext();
        Assert.AreEqual(0, verify.Items.Single().Count);
        Assert.AreEqual(1, verify.HouseFixtures.Count());
    }

    [TestMethod]
    public void RetriedPickup_AfterLostCommitAcknowledgementStillReturnsSuccessExactlyOnce()
    {
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out _));
        var fault = new LostCommitAcknowledgement();
        var factory = new Factory(new DbContextOptionsBuilder()
            .UseSqlite($"Data Source={_path};Pooling=False", sqlite => sqlite.ExecutionStrategy(dependencies => new CommitRetryStrategy(dependencies)))
            .AddInterceptors(fault).Options);
        var manager = new HousingFixtureManager(_resources, factory, NullLogger<HousingFixtureManager>.Instance);

        Assert.IsTrue(manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), out var returned, out var remaining));
        Assert.AreEqual(1, fault.Failures);
        Assert.AreEqual(fixture.Id, returned.Id);
        Assert.AreEqual(1, remaining);
        Assert.IsFalse(manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), out _, out _));
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(0, db.HouseFixtures.Count());
        Assert.AreEqual(1, db.Items.Single().Count);
    }

    [TestMethod]
    public void RestartedFactory_RestoresTransformAndCanReturnInventory()
    {
        var rotation = new Quaternion(1.25f, 0.1f, -0.2f, 0);
        Assert.IsTrue(_manager.TryPlace(_characterId, 100, 1, Position, rotation, 1.5f, out var fixture, out _));

        // New connections and service instance: no tracked entity or actor state can supply the result.
        var factory = new Factory(new DbContextOptionsBuilder().UseSqlite($"Data Source={_path};Pooling=False").Options);
        var restarted = new HousingFixtureManager(_resources, factory, NullLogger<HousingFixtureManager>.Instance);
        var restored = restarted.GetFixtures(_characterId, 100).Single();
        Assert.AreEqual(fixture.PlacementId, restored.PlacementId);
        Assert.AreEqual(rotation.X, restored.RotationX);
        Assert.AreEqual(rotation.Y, restored.RotationY);
        Assert.AreEqual(rotation.Z, restored.RotationZ);
        Assert.AreEqual(1.5f, restored.Scale);
        Assert.AreEqual(231, restored.Tint);
        Assert.IsTrue(restarted.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(restored.Id), out _, out var remaining));
        Assert.AreEqual(1, remaining);
    }

    [TestMethod]
    [TestCategory("Stress")]
    public void RepeatedLifecycle_ConservesInventoryAcrossServiceRecreation()
    {
        const int cycles = 256;
        var elapsed = Stopwatch.StartNew();
        for (var index = 0; index < cycles; index++)
        {
            var manager = new HousingFixtureManager(_resources, _factory, NullLogger<HousingFixtureManager>.Instance);
            Assert.IsTrue(manager.TryPlace(_characterId, 100, 1, Position, default, 1, out var fixture, out var count), $"Place {index}");
            Assert.AreEqual(0, count);
            Assert.IsTrue(manager.TryMove(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), new(20, 2, 30, 1), new(1, 0, 0, 0), 2, out _), $"Move {index}");
            Assert.IsTrue(manager.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), out _, out count), $"Pickup {index}");
            Assert.AreEqual(1, count);
        }
        elapsed.Stop();
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(1, db.Items.Single().Count);
        Assert.AreEqual(0, db.HouseFixtures.Count());
        Assert.AreEqual("ok", db.Database.SqlQueryRaw<string>("PRAGMA quick_check").AsEnumerable().Single());
        TestContext.WriteLine($"{cycles * 3} committed fixture operations: {elapsed.Elapsed.TotalMilliseconds:F0} ms ({elapsed.Elapsed.TotalMilliseconds / (cycles * 3):F2} ms/operation). Local SQLite; not a player-capacity estimate.");
    }

    [TestMethod]
    [TestCategory("Stress")]
    public void FullHouse_CountDoesNotLoadFixturesAndCapacityFailureDoesNotConsumeInventory()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.HouseFixtures.AddRange(Enumerable.Range(0, HousingFixtureManager.MaxFixtureCount).Select(index => new DbHouseFixture
            {
                Id = (ulong)(1000 + index),
                PlacementId = Guid.NewGuid(), HouseId = 100, CharacterId = _characterId,
                ItemId = 1, ItemDefinitionId = 27943, Scale = 1, PositionX = 10, PositionZ = 10
            }));
            db.SaveChanges();
        }
        _commands.Commands.Clear();
        var elapsed = Stopwatch.StartNew();
        Assert.AreEqual(HousingFixtureManager.MaxFixtureCount, _manager.GetFixtureCount(_characterId, 100));
        elapsed.Stop();
        Assert.AreEqual(1, _commands.Commands.Count);
        StringAssert.Contains(_commands.Commands.Single(), "COUNT(*)");
        Assert.IsFalse(_manager.TryPlace(_characterId, 100, 1, Position, default, 1, out _, out _));
        using var verify = _factory.CreateDbContext();
        Assert.AreEqual(1, verify.Items.Single().Count);
        Assert.AreEqual(HousingFixtureManager.MaxFixtureCount, verify.HouseFixtures.Count());
        TestContext.WriteLine($"Count of {HousingFixtureManager.MaxFixtureCount} fixtures: {elapsed.Elapsed.TotalMilliseconds:F2} ms, one scalar query.");
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText);
            return result;
        }
    }

    private sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        private int _failures;
        public int Failures => _failures;

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (Interlocked.CompareExchange(ref _failures, 1, 0) == 0)
                throw new CommitAcknowledgementException();
        }
    }

    private sealed class CommitAcknowledgementException : DbException;

    private sealed class CommitRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is CommitAcknowledgementException;
    }

    private sealed class Factory(DbContextOptions options) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => new SqliteDatabaseContext(options);
    }
}
