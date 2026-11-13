using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Scripting;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingFixturePermissionTests
{
    public enum Access
    {
        Visitor,
        OwnerPreview,
        OwnerWrongZone,
        ReadOnlyOwner,
        DisposedZone
    }

    private const int HouseId = 100;
    private const int ChairDefinition = 27943;
    private const int AvailableItemId = 2;
    private static readonly Vector4 Position = new(10, 0, 10, 1);
    private static readonly Vector4 MovedPosition = new(25, 3, 40, 1);
    private string _path = null!;
    private Factory _factory = null!;
    private ResourceManager _resources = null!;
    private ServiceProvider _services = null!;
    private UdpManager<UdpConnection> _udp = null!;
    private HousingFixtureManager _manager = null!;
    private HousingZone _zone = null!;
    private HousingZone _otherZone = null!;
    private Player _owner = null!;
    private Player _visitor = null!;
    private Player? _thirdViewer;
    private ulong _characterId;
    private ulong _fixtureGuid;
    private readonly CommandLog _commands = new();

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"housing-permissions-{Guid.NewGuid():N}.db");
        _factory = new Factory(new DbContextOptionsBuilder()
            .UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=2")
            .AddInterceptors(_commands).Options);
        _resources = new ResourceManager(NullLogger<ResourceManager>.Instance);
        _resources.Houses.Add(1, new HouseDefinition
        {
            Id = 1, ZoneId = HouseId,
            BuildAreas = [new BoundingBox { Min = new(0, -10, 0, 0), Max = new(100, 100, 100, 0) }]
        });
        _resources.HousingFixtures.Add(ChairDefinition,
            new HousingFixtureDefinition { ItemDefinitionId = ChairDefinition, ModelId = 3895 });
        _resources.ClientItemDefinitions.Add(ChairDefinition, new ClientItemDefinition
        {
            Id = ChairDefinition, TextureAlias = "chair-texture", TintAlias = "dyetint"
        });

        using var db = _factory.CreateDbContext();
        db.Database.Migrate();
        var owner = CreateCharacter("Owner");
        owner.Items.Add(new DbItem { Id = 1, Definition = ChairDefinition, Count = 1, Tint = 232 });
        var visitor = CreateCharacter("Visitor");
        db.Characters.AddRange(owner, visitor);
        db.SaveChanges();
        _characterId = owner.Id;
        db.HousingGuids.Add(new DbHousingGuid { Id = HouseId });
        db.Houses.Add(new DbHouse { Id = HouseId, Key = HouseId, ZoneId = HouseId, CharacterId = _characterId });
        db.SaveChanges();

        _manager = new HousingFixtureManager(_resources, _factory, NullLogger<HousingFixtureManager>.Instance);
        Assert.IsTrue(_manager.TryPlace(owner.Id, HouseId, 1, Position, Quaternion.Identity, 1,
            out var fixture, out var remaining));
        Assert.AreEqual(0, remaining);
        _fixtureGuid = GuidHelper.GetFixtureGuid(fixture.Id);

        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            .AddSingleton<IResourceManager>(_resources)
            .AddSingleton<IDbContextFactory<DatabaseContext>>(_factory)
            .AddSingleton<IScriptManager>(new ScriptManager(NullLoggerFactory.Instance))
            .AddSingleton<IZoneManager, ZoneManager>()
            .AddSingleton<IRewardManager, RewardManager>()
            .AddSingleton<IQuestManager, QuestManager>()
            .AddSingleton(_manager)
            .BuildServiceProvider();
        _udp = new UdpManager<UdpConnection>(new UdpParams(ManagerRole.ExternalServer)
        {
            BindIpAddress = IPAddress.Loopback.ToString(), Port = 0
        }, _services);
        _zone = CreateZone();
        _otherZone = CreateZone();
        _owner = CreatePlayer(owner.Id);
        _visitor = CreatePlayer(visitor.Id);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _owner?.Dispose();
        _visitor?.Dispose();
        _thirdViewer?.Dispose();
        _zone?.Dispose();
        _otherZone?.Dispose();
        _udp?.Dispose();
        _services?.Dispose();
        _resources?.Dispose();
        if (_path is not null)
            File.Delete(_path);
    }

    [TestMethod]
    public void MissingHouseDuringOwnerLookup_DoesNotPermanentlyPoisonInitialization()
    {
        using var zone = CreateZone(houseId: 9999);
        Assert.IsFalse(zone.IsOwner(_owner));
        using (var db = _factory.CreateDbContext())
        {
            db.HousingGuids.Add(new DbHousingGuid { Id = 9999 });
            db.Houses.Add(new DbHouse { Id = 9999, Key = 9999, ZoneId = HouseId, CharacterId = _characterId });
            db.SaveChanges();
        }
        Assert.IsTrue(zone.IsOwner(_owner));
    }

    [TestMethod]
    [DataRow(Access.Visitor)]
    [DataRow(Access.OwnerPreview)]
    [DataRow(Access.OwnerWrongZone)]
    [DataRow(Access.ReadOnlyOwner)]
    [DataRow(Access.DisposedZone)]
    public void RequestFixture_WithoutEditAccess_DoesNotChangeDatabaseOrAuthorizePlacement(Access access)
    {
        var player = RestrictAccess(access);
        var before = Snapshot();

        _zone.RequestFixture(player, new ClientHousingPacketPlaceFixtureRequest { ItemId = AvailableItemId });
        Assert.AreEqual(before, Snapshot(), "A denied request changed persisted housing or inventory.");

        // Restoring access must not make a request sent without permission usable.
        _owner.IsPreviewingHouse = false;
        _owner.Zone = _zone;
        if (!_zone.InEditMode)
            _zone.SetEditMode(_owner, true);
        _zone.PlaceFixture(_owner, PlaceRequest());
        Assert.AreEqual(before, Snapshot(), "A denied request authorized a later placement.");
    }

    [TestMethod]
    [DataRow(Access.Visitor)]
    [DataRow(Access.OwnerPreview)]
    [DataRow(Access.OwnerWrongZone)]
    [DataRow(Access.ReadOnlyOwner)]
    [DataRow(Access.DisposedZone)]
    public void PlaceFixture_WithoutEditAccess_DoesNotConsumeInventoryOrAddFixture(Access access)
    {
        _zone.SetEditMode(_owner, true);
        _zone.RequestFixture(_owner, new ClientHousingPacketPlaceFixtureRequest { ItemId = AvailableItemId });
        var player = RestrictAccess(access);
        var before = Snapshot();

        _zone.PlaceFixture(player, PlaceRequest());

        Assert.AreEqual(before, Snapshot());
        Assert.AreEqual(1, player.Items.Single(item => item.Id == AvailableItemId).Count);
    }

    [TestMethod]
    [DataRow(Access.Visitor)]
    [DataRow(Access.OwnerPreview)]
    [DataRow(Access.OwnerWrongZone)]
    [DataRow(Access.ReadOnlyOwner)]
    [DataRow(Access.DisposedZone)]
    public void SaveFixture_WithoutEditAccess_DoesNotChangeSavedTransform(Access access)
    {
        var player = RestrictAccess(access);
        var before = Snapshot();

        _zone.SaveFixture(player, new ClientHousingPacketSaveFixture
        {
            FixtureGuid = _fixtureGuid, Position = MovedPosition,
            Rotation = new Quaternion(0.5f, 0.1f, 0, 0), Scale = 2
        });

        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    [DataRow(Access.Visitor)]
    [DataRow(Access.OwnerPreview)]
    [DataRow(Access.OwnerWrongZone)]
    [DataRow(Access.ReadOnlyOwner)]
    [DataRow(Access.DisposedZone)]
    public void PickupFixture_WithoutEditAccess_DoesNotRemoveFixtureOrReturnInventory(Access access)
    {
        var player = RestrictAccess(access);
        var before = Snapshot();

        _zone.PickupFixture(player, _fixtureGuid);

        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void Owner_CanEnableAndDisableEditMode_WithoutChangingDatabase()
    {
        var before = Snapshot();
        Assert.IsTrue(_zone.IsOwner(_owner));
        Assert.IsFalse(_zone.InEditMode);

        _zone.SetEditMode(_owner, true);
        Assert.IsTrue(_zone.InEditMode);
        _zone.SetEditMode(_owner, false);
        Assert.IsFalse(_zone.InEditMode);

        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    [DataRow(Access.Visitor)]
    [DataRow(Access.OwnerPreview)]
    [DataRow(Access.OwnerWrongZone)]
    public void NonEditingPlayer_CannotDisableOrEnableOwnerEditMode(Access access)
    {
        var player = RestrictAccess(access);
        var before = Snapshot();
        _zone.SetEditMode(player, false);
        Assert.IsTrue(_zone.InEditMode);

        _owner.IsPreviewingHouse = false;
        _owner.Zone = _zone;
        _zone.SetEditMode(_owner, false);
        player = RestrictAccess(access, enableEditMode: false);
        _zone.SetEditMode(player, true);
        Assert.IsFalse(_zone.InEditMode);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void DisposedZone_ClearsEditMode_AndOwnerCannotReenableIt()
    {
        _zone.SetEditMode(_owner, true);
        _zone.RequestFixture(_owner, new ClientHousingPacketPlaceFixtureRequest { ItemId = AvailableItemId });
        var before = Snapshot();

        _zone.Dispose();
        Assert.IsFalse(_zone.InEditMode);
        _zone.SetEditMode(_owner, true);
        Assert.IsFalse(_zone.InEditMode);
        _zone.PlaceFixture(_owner, PlaceRequest());

        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public async Task Dispose_OverlappingPlacement_WaitsThenRemovesActorButRetainsSavedFixture()
    {
        var timeout = TimeSpan.FromSeconds(5);
        using var placementPaused = new ManualResetEventSlim();
        using var releasePlacement = new ManualResetEventSlim();
        using var disposalStarted = new ManualResetEventSlim();
        _zone.SetEditMode(_owner, true);
        _zone.RequestFixture(_owner, new ClientHousingPacketPlaceFixtureRequest { ItemId = AvailableItemId });
        ulong publishedActorGuid = 0;
        _commands.BeforeCommand = command =>
        {
            if (command.CommandText.StartsWith("INSERT INTO \"HouseFixtures\"", StringComparison.OrdinalIgnoreCase))
            {
                placementPaused.Set();
                Assert.IsTrue(releasePlacement.Wait(timeout), "Timed out releasing the paused placement.");
            }
            else if (placementPaused.IsSet && CommandLog.IsFixtureCount(command.CommandText))
            {
                publishedActorGuid = _zone.Npcs.Single().Guid;
            }
        };
        var placement = Task.Factory.StartNew(() => _zone.PlaceFixture(_owner, PlaceRequest()),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task? disposal = null;
        try
        {
            Assert.IsTrue(placementPaused.Wait(timeout), "Placement did not reach the fixture insert.");
            disposal = Task.Factory.StartNew(() =>
            {
                disposalStarted.Set();
                _zone.Dispose();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.IsTrue(disposalStarted.Wait(timeout), "Disposal did not start.");
            Assert.IsFalse(disposal.Wait(TimeSpan.FromMilliseconds(150)), "Disposal overtook an in-flight placement.");
        }
        finally
        {
            releasePlacement.Set();
            try
            {
                await Task.WhenAll(placement, disposal ?? Task.CompletedTask).WaitAsync(timeout);
            }
            finally
            {
                _commands.BeforeCommand = null;
            }
        }

        Assert.AreNotEqual(0UL, publishedActorGuid, "Placement did not publish its actor before disposal.");
        Assert.IsFalse(_zone.InEditMode);
        Assert.IsFalse(_zone.Npcs.Any());
        Assert.IsFalse(_zone.TryGetEntity(publishedActorGuid, out _));
        using var db = _factory.CreateDbContext();
        var saved = db.HouseFixtures.Single(fixture => fixture.CharacterId == _characterId && fixture.ItemId == AvailableItemId);
        Assert.AreEqual((ulong)HouseId, saved.HouseId);
        Assert.AreEqual(Position.X, saved.PositionX);
        Assert.AreEqual(Position.Z, saved.PositionZ);
        Assert.AreEqual(0, db.Items.Single(item => item.CharacterId == _characterId && item.Id == AvailableItemId).Count);
    }

    [TestMethod]
    public void EditingOwner_CanRequestPlaceSaveAndPickupFixture()
    {
        _zone.SetEditMode(_owner, true);
        _zone.RequestFixture(_owner, new ClientHousingPacketPlaceFixtureRequest { ItemId = AvailableItemId });
        _zone.PlaceFixture(_owner, PlaceRequest());
        var placed = _manager.GetFixtures(_characterId, HouseId).Single(fixture => fixture.ItemId == AvailableItemId);
        Assert.AreEqual(0, _owner.Items.Single(item => item.Id == AvailableItemId).Count);

        _zone.SaveFixture(_owner, new ClientHousingPacketSaveFixture
        {
            FixtureGuid = GuidHelper.GetFixtureGuid(placed.Id), Position = MovedPosition, Rotation = Quaternion.Identity, Scale = 2
        });
        var saved = _manager.GetFixtures(_characterId, HouseId).Single(fixture => fixture.Id == placed.Id);
        Assert.AreEqual(MovedPosition.X, saved.PositionX);
        Assert.AreEqual(MovedPosition.Y, saved.PositionY);
        Assert.AreEqual(MovedPosition.Z, saved.PositionZ);
        Assert.AreEqual(2f, saved.Scale);

        _zone.PickupFixture(_owner, GuidHelper.GetFixtureGuid(placed.Id));
        Assert.AreEqual(1, _owner.Items.Single(item => item.Id == AvailableItemId).Count);
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(1, db.Items.Single(item => item.CharacterId == _characterId && item.Id == AvailableItemId).Count);
        Assert.AreEqual(_fixtureGuid, GuidHelper.GetFixtureGuid(db.HouseFixtures.Single().Id));
    }

    [TestMethod]
    public void SaveFixture_WithThreeViewers_OnlyReadsAndUpdatesTheTargetFixture()
    {
        RegisterViewers();
        _zone.SetEditMode(_owner, true);
        _commands.Commands.Clear();

        _zone.SaveFixture(_owner, new ClientHousingPacketSaveFixture
        {
            FixtureGuid = _fixtureGuid, Position = MovedPosition, Rotation = Quaternion.Identity, Scale = 2
        });

        Assert.AreEqual(2, _commands.Commands.Count, string.Join(Environment.NewLine, _commands.Commands));
        Assert.IsFalse(_commands.Commands.Any(CommandLog.IsFixtureCount), "Saving queried fixture capacity.");
        var reads = _commands.Commands.Where(command => command.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.AreEqual(1, reads.Length);
        StringAssert.Contains(reads[0], "FROM \"HouseFixtures\"");
        StringAssert.Contains(reads[0], "\"Id\" =");
        StringAssert.Contains(reads[0], "LIMIT 2");
        var saved = _manager.GetFixtures(_characterId, HouseId).Single();
        Assert.AreEqual(MovedPosition.X, saved.PositionX);
        Assert.AreEqual(2f, saved.Scale);
        Assert.AreEqual(1, _zone.Npcs.Count());
    }

    [TestMethod]
    public void PickupFixture_WhenPostCommitCountFails_StillRemovesActorAndReturnsInventory()
    {
        RegisterViewers();
        _zone.SetEditMode(_owner, true);
        _zone.SaveFixture(_owner, new ClientHousingPacketSaveFixture
        {
            FixtureGuid = _fixtureGuid, Position = Position, Rotation = Quaternion.Identity, Scale = 1
        });
        var actor = _zone.Npcs.Single();
        _commands.BeforeFailedCount = () =>
        {
            Assert.IsFalse(_zone.Npcs.Any(), "The actor survived until the fallible count query.");
            using var db = _factory.CreateDbContext();
            Assert.IsFalse(db.HouseFixtures.Any(), "The injected count failure did not happen after commit.");
            Assert.AreEqual(1, db.Items.Single(item => item.CharacterId == _characterId && item.Id == 1).Count);
        };
        _commands.FailFixtureCounts = true;

        try
        {
            _zone.PickupFixture(_owner, _fixtureGuid);
        }
        finally
        {
            _commands.FailFixtureCounts = false;
        }

        Assert.AreEqual(1, _commands.FailedCounts, "The post-commit count failure was not exercised.");
        Assert.IsFalse(_zone.Npcs.Any());
        Assert.IsFalse(_zone.TryGetEntity(actor.Guid, out _));
        Assert.AreEqual(1, _owner.Items.Single(item => item.Id == 1).Count);
        using var verify = _factory.CreateDbContext();
        Assert.IsFalse(verify.HouseFixtures.Any());
        Assert.AreEqual(1, verify.Items.Single(item => item.CharacterId == _characterId && item.Id == 1).Count);
    }

    [TestMethod]
    public void FixtureVisibility_DoesNotScanOtherNpcs_ButOrdinaryNpcsStillDo()
    {
        var tile = _zone.GetTileFromPosition(Position);
        var nearby = new VisibilityCountingNpc(_zone) { Guid = 42, Visible = true };
        Assert.IsTrue(tile.Entities.TryAdd(nearby.Guid, nearby));
        try
        {
            var fixture = new DbHouseFixture { Id = 200, PositionX = 10, PositionZ = 10, Scale = 1 };
            Assert.IsTrue(_zone.TryAddNpc((zone, guid) => new HousingFixtureNpc(zone, guid, fixture,
                _resources.HousingFixtures[ChairDefinition], _resources.ClientItemDefinitions[ChairDefinition]), out var actor));
            actor.Visible = true;
            actor.ApplyFixture(fixture);
            Assert.AreEqual(0, nearby.VisibilityReads, "Furniture scanned a nearby NPC's visibility.");
            Assert.IsTrue(actor.VisibleNpcs.IsEmpty);

            Assert.IsTrue(_zone.TryCreateNpc(out var ordinary));
            ordinary.Visible = true;
            ordinary.UpdatePosition(Position, Quaternion.Identity);
            Assert.IsTrue(nearby.VisibilityReads > 0);
            Assert.IsTrue(ordinary.TracksVisibleNpcs);
            Assert.IsTrue(ordinary.VisibleNpcs.ContainsKey(nearby.Guid));
            var ticks = 0;
            var seconds = 0;
            ordinary.UpdateEveryTickAction = () => ticks++;
            ordinary.UpdateEverySecondAction = () => seconds++;
            ordinary.UpdateEveryTick();
            ordinary.UpdateEverySecond();
            Assert.AreEqual(1, ticks);
            Assert.AreEqual(1, seconds);
        }
        finally
        {
            tile.Entities.TryRemove(nearby.Guid, out _);
        }
    }

    [TestMethod]
    public void FixtureAndPlayerTileChanges_MaintainBothSidesOfVisibility()
    {
        _zone.Dispose();
        _zone = CreateZone(endTile: 8);
        _owner.Zone = _zone;
        Assert.IsTrue(_zone.TryAddPlayer(_owner));
        _owner.Visible = true;
        _owner.UpdatePosition(Position, Quaternion.Identity);
        var fixture = new DbHouseFixture { Id = 200, PositionX = 10, PositionZ = 10, Scale = 1 };
        Assert.IsTrue(_zone.TryAddNpc((zone, guid) => new HousingFixtureNpc(zone, guid, fixture,
            _resources.HousingFixtures[ChairDefinition], _resources.ClientItemDefinitions[ChairDefinition]), out var actor));
        actor.Visible = true;
        actor.ApplyFixture(fixture);
        Assert.IsTrue(_owner.VisibleNpcs.ContainsKey(actor.Guid));
        Assert.IsTrue(actor.VisiblePlayers.ContainsKey(_owner.Guid));

        fixture.PositionX = 610;
        actor.ApplyFixture(fixture);
        Assert.IsFalse(_owner.VisibleNpcs.ContainsKey(actor.Guid));
        Assert.IsFalse(actor.VisiblePlayers.ContainsKey(_owner.Guid));
        fixture.PositionX = 10;
        actor.ApplyFixture(fixture);
        Assert.IsTrue(_owner.VisibleNpcs.ContainsKey(actor.Guid));
        Assert.IsTrue(actor.VisiblePlayers.ContainsKey(_owner.Guid));

        _owner.UpdatePosition(new(610, 0, 10, 1), Quaternion.Identity);
        Assert.IsFalse(_owner.VisibleNpcs.ContainsKey(actor.Guid));
        Assert.IsFalse(actor.VisiblePlayers.ContainsKey(_owner.Guid));
        _owner.UpdatePosition(Position, Quaternion.Identity);
        Assert.IsTrue(_owner.VisibleNpcs.ContainsKey(actor.Guid));
        Assert.IsTrue(actor.VisiblePlayers.ContainsKey(_owner.Guid));

        actor.Dispose();
        Assert.IsFalse(_owner.VisibleNpcs.ContainsKey(actor.Guid));
        Assert.IsFalse(_zone.TryGetNpc(actor.Guid, out _));
    }

    [TestMethod]
    [TestCategory("Stress")]
    [DataRow(1000)]
    [DataRow(2000)]
    public void NearbyFixtureActors_DoNotRetainNpcObservers_AndDisposeWithoutDatabaseChanges(int count)
    {
        var before = Snapshot();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var elapsed = Stopwatch.StartNew();
        for (var index = 0; index < count; index++)
        {
            var fixture = new DbHouseFixture
            {
                Id = (ulong)(10000 + index), ItemId = 1, ItemDefinitionId = ChairDefinition,
                PositionX = Position.X, PositionY = Position.Y, PositionZ = Position.Z,
                RotationW = 1, Scale = 1
            };
            Assert.IsTrue(_zone.TryAddNpc((zone, guid) => new HousingFixtureNpc(zone, guid, fixture,
                _resources.HousingFixtures[ChairDefinition], _resources.ClientItemDefinitions[ChairDefinition]), out var actor));
            actor.Visible = true;
            actor.ApplyFixture(fixture);
        }
        elapsed.Stop();
        var creationMs = elapsed.Elapsed.TotalMilliseconds;
        var creationBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.AreEqual(count, _zone.Npcs.Count());
        Assert.IsTrue(_zone.Npcs.All(actor => actor.VisibleNpcs.IsEmpty));
        Assert.AreEqual(before, Snapshot());

        var actors = _zone.Npcs.Cast<IEntity>().ToArray();
        elapsed.Restart();
        for (var tick = 0; tick < 200; tick++)
        {
            foreach (var actor in actors)
                actor.UpdateEveryTick();
        }
        elapsed.Stop();
        var tickMs = elapsed.Elapsed.TotalMilliseconds;

        elapsed.Restart();
        _zone.Dispose();
        elapsed.Stop();
        Assert.IsFalse(_zone.Npcs.Any());
        Assert.AreEqual(before, Snapshot());
        TestContext.WriteLine($"{count} same-tile fixture actors: create/publish {creationMs:F2} ms; allocated {creationBytes:N0} bytes; 200 idle tick passes {tickMs:F2} ms; dispose {elapsed.Elapsed.TotalMilliseconds:F2} ms. No clients, running zone timers or database writes.");
    }

    private void RegisterViewers()
    {
        _thirdViewer = CreatePlayer(_characterId + 100);
        Assert.IsTrue(_zone.TryAddPlayer(_owner));
        Assert.IsTrue(_zone.TryAddPlayer(_visitor));
        Assert.IsTrue(_zone.TryAddPlayer(_thirdViewer));
        Assert.AreEqual(3, _zone.Players.Count());
        _owner.Items.Add(new ClientItem { Id = 1, Definition = ChairDefinition, Count = 0, Tint = 232 });
    }

    private Player RestrictAccess(Access access, bool enableEditMode = true)
    {
        if (enableEditMode && !_zone.InEditMode)
        {
            _zone.SetEditMode(_owner, true);
            Assert.IsTrue(_zone.InEditMode);
        }

        switch (access)
        {
            case Access.Visitor:
                Assert.IsFalse(_zone.IsOwner(_visitor));
                return _visitor;
            case Access.OwnerPreview:
                _owner.IsPreviewingHouse = true;
                Assert.IsFalse(_zone.IsOwner(_owner));
                break;
            case Access.OwnerWrongZone:
                _owner.Zone = _otherZone;
                Assert.IsTrue(_zone.IsOwner(_owner));
                break;
            case Access.ReadOnlyOwner:
                _zone.SetEditMode(_owner, false);
                Assert.IsFalse(_zone.InEditMode);
                break;
            case Access.DisposedZone:
                _zone.Dispose();
                Assert.IsFalse(_zone.InEditMode);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(access));
        }
        return _owner;
    }

    private static DbCharacter CreateCharacter(string name) => new()
    {
        FirstName = name, Head = "head", HeadId = 1, Hair = "hair", HairId = 1,
        SkinTone = "skin", SkinToneId = 1,
        User = new DbUser { Username = $"permissions-{name}", Password = "not-used" },
        Items = [new DbItem { Id = AvailableItemId, Definition = ChairDefinition, Count = 1, Tint = 231 }]
    };

    private HousingZone CreateZone(int endTile = 2, ulong? houseId = null) => new(new HousingZoneDefinition
    {
        Id = HouseId, Name = "PermissionTestHouse", TileSize = 100,
        StartLongitude = 0, EndLongitude = endTile, StartLatitude = 0, EndLatitude = endTile
    }, _services) { OwnerId = GuidHelper.GetHouseGuid(houseId ?? HouseId) };

    private Player CreatePlayer(ulong characterId) => new(_zone,
        new UdpConnection(_udp, new IPEndPoint(IPAddress.Loopback, 12345).Serialize(), connectCode: (int)characterId),
        _resources, _services.GetRequiredService<IZoneManager>())
    {
        Guid = GuidHelper.GetPlayerGuid(characterId),
        Items = [new ClientItem { Id = AvailableItemId, Definition = ChairDefinition, Count = 1, Tint = 231 }]
    };

    private static ClientHousingPacketPlaceFixture PlaceRequest() => new()
    {
        ItemId = AvailableItemId, Position = Position, Rotation = Quaternion.Identity, Scale = 1
    };

    private string Snapshot()
    {
        using var db = _factory.CreateDbContext();
        return JsonSerializer.Serialize(new
        {
            Items = db.Items.AsNoTracking().AsEnumerable().OrderBy(item => item.CharacterId).ThenBy(item => item.Id)
                .Select(item => new { item.CharacterId, item.Id, item.Definition, item.Count, item.Tint, item.Created }).ToArray(),
            Houses = db.Houses.AsNoTracking().AsEnumerable().OrderBy(house => house.CharacterId).ThenBy(house => house.Id)
                .Select(house => new
                {
                    house.CharacterId, house.Id, house.Name, house.IsLocked, house.IsFloraAllowed, house.PetAutospawn,
                    house.IsPublished, house.Description, house.Keywords, house.PublishedAt, house.Created
                }).ToArray(),
            Fixtures = db.HouseFixtures.AsNoTracking().OrderBy(fixture => (long)fixture.Id).Select(fixture => new
            {
                fixture.Id, fixture.PlacementId, fixture.CharacterId, fixture.HouseId, fixture.ItemId,
                fixture.ItemDefinitionId, fixture.Tint, fixture.PositionX, fixture.PositionY, fixture.PositionZ,
                fixture.RotationX, fixture.RotationY, fixture.RotationZ, fixture.RotationW, fixture.Scale
            }).ToArray()
        });
    }

    private sealed class Factory(DbContextOptions options) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => new SqliteDatabaseContext(options);
    }

    private sealed class VisibilityCountingNpc(IZone zone) : Npc(zone), IEntity
    {
        public int VisibilityReads { get; private set; }
        bool IEntity.Visible
        {
            get { VisibilityReads++; return Visible; }
            set => Visible = value;
        }
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public bool FailFixtureCounts { get; set; }
        public int FailedCounts { get; private set; }
        public Action? BeforeFailedCount { get; set; }
        public Action<DbCommand>? BeforeCommand { get; set; }

        public static bool IsFixtureCount(string command) =>
            command.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
            && command.Contains("HouseFixtures", StringComparison.OrdinalIgnoreCase);

        private void Record(DbCommand command)
        {
            Commands.Add(command.CommandText);
            BeforeCommand?.Invoke(command);
            if (!FailFixtureCounts || !IsFixtureCount(command.CommandText))
                return;
            FailedCounts++;
            BeforeFailedCount?.Invoke();
            throw new SqliteException("Injected post-commit fixture count failure.", 10);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result)
        {
            Record(command);
            return result;
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }
    }
}
