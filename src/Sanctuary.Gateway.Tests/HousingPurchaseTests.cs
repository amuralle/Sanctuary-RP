using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;
using Sanctuary.Gateway.Handlers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.GameCommerce;
using Sanctuary.Scripting;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.Gateway.Tests;

[TestClass]
[DoNotParallelize]
public sealed class HousingPurchaseTests
{
    private const int DefinitionId = 36110;
    private string _path = null!;
    private Factory _factory = null!;
    private ResourceManager _resources = null!;
    private ServiceProvider _services = null!;
    private HousingFixtureManager _fixtures = null!;
    private HousingZone _zone = null!;
    private GatewayServer _server = null!;
    private LoginClient _login = null!;
    private GatewayConnection _connection = null!;
    private Player _player = null!;
    private ulong _characterId;
    private ClientItemDefinition Item => _resources.ClientItemDefinitions[DefinitionId];
    private AppStoreBundleDefinition Offer => _resources.Stores[1].Bundles[10];

    [TestInitialize]
    public void Setup()
    {
        Directory.CreateDirectory(ResourceManager.BaseDirectory);
        _path = Path.Combine(Path.GetTempPath(), $"housing-shop-{Guid.NewGuid():N}.db");
        _factory = new Factory(new DbContextOptionsBuilder().UseSqlite($"Data Source={_path};Pooling=False").Options);
        using (var db = _factory.CreateDbContext())
        {
            db.Database.Migrate();
            var character = new DbCharacter
            {
                FirstName = "Buyer", Head = "head", HeadId = 1, Hair = "hair", HairId = 1,
                SkinTone = "skin", SkinToneId = 1, Coins = 10000, StationCash = 10000,
                User = new DbUser { Username = "housing-buyer", Password = "unused" }
            };
            db.Characters.Add(character);
            db.SaveChanges();
            _characterId = character.Id;
            db.HousingGuids.Add(new DbHousingGuid { Id = 100 });
            db.Houses.Add(new DbHouse { Id = 100, Key = 100, ZoneId = 100, CharacterId = character.Id });
            db.SaveChanges();
        }
        _resources = new ResourceManager(NullLogger<ResourceManager>.Instance);
        _resources.ClientItemDefinitions.Add(DefinitionId, new ClientItemDefinition
        {
            Id = DefinitionId, Type = 29, Cost = 100, MemberDiscount = 10, ResellValue = 20,
            MaxStackSize = -1, Icon = new IconData { Id = 1, TintId = 231 }
        });
        _resources.CoinStoreItems.Add(DefinitionId, new ItemDefinitionMetaData { Id = DefinitionId });
        _resources.HousingFixtures.Add(DefinitionId, new HousingFixtureDefinition { ItemDefinitionId = DefinitionId, ModelId = 1825 });
        _resources.Houses.Add(1, new HouseDefinition
        {
            Id = 1, ZoneId = 100,
            BuildAreas = [new BoundingBox { Min = new(0, -10, 0, 0), Max = new(100, 100, 100, 0) }]
        });
        _resources.Stores.Add(1, new StoreDefinition { Id = 1 });
        _resources.Stores[1].Bundles.Add(10, new AppStoreBundleDefinition
        {
            Id = 10, StoreId = 1, Price = 200, MemberDiscount = 180,
            Entries = [new MarketingBundleDefinition.Entry { MarketingItemId = DefinitionId, Quantity = 1 }]
        });
        _fixtures = new HousingFixtureManager(_resources, _factory, NullLogger<HousingFixtureManager>.Instance);
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            .AddSingleton<IResourceManager>(_resources)
            .AddSingleton<IDbContextFactory<DatabaseContext>>(_factory)
            .AddSingleton<IScriptManager>(new ScriptManager(NullLoggerFactory.Instance))
            .AddSingleton<IZoneManager, ZoneManager>()
            .AddSingleton<IRewardManager, RewardManager>()
            .AddSingleton<IQuestManager, QuestManager>()
            .AddSingleton(_fixtures).BuildServiceProvider();
        _zone = new HousingZone(new HousingZoneDefinition
        {
            Id = 100, Name = "PurchaseTest", TileSize = 100,
            StartLongitude = 0, EndLongitude = 2, StartLatitude = 0, EndLatitude = 2
        }, _services) { OwnerId = GuidHelper.GetHouseGuid(100) };
        _server = new GatewayServer(NullLogger<GatewayServer>.Instance, _resources,
            new UdpParams(ManagerRole.ExternalServer) { BindIpAddress = "127.0.0.1", Port = 0 }, _services);
        _login = new LoginClient(NullLogger<LoginClient>.Instance, new UdpParams(ManagerRole.ExternalClient), _services);
        _connection = new GatewayConnection(NullLogger<GatewayConnection>.Instance, Options.Create(new GatewayServerOptions
        {
            Environment = "live", ClientVersion = "test", ServerAddress = "127.0.0.1",
            LoginGatewayAddress = "127.0.0.1", LoginGatewayChallenge = "test", Port = 0, UseCompression = false
        }), _services.GetRequiredService<IZoneManager>(), _login, _server, _resources, _services, _factory,
            new IPEndPoint(IPAddress.Loopback, 12345).Serialize(), 1);
        _player = new Player(_zone, _connection, _resources, _services.GetRequiredService<IZoneManager>())
        {
            Guid = GuidHelper.GetPlayerGuid(_characterId), Coins = 10000, StationCash = 10000
        };
        // Exercise real packet handlers without running their login/socket service lifecycle.
        typeof(GatewayConnection).GetProperty(nameof(GatewayConnection.Player))!.SetValue(_connection, _player);
        CoinStoreSellToClientRequestPacketHandler.ConfigureServices(_services);
        CoinStoreBuyFromClientRequestPacketHandler.ConfigureServices(_services);
        PacketInGamePurchasePlaceOrderPacketHandler.ConfigureServices(_services);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _player?.Dispose();
        _zone?.Dispose();
        _server?.Dispose();
        _login?.Dispose();
        _services?.Dispose();
        _resources?.Dispose();
        if (_path is not null)
            File.Delete(_path);
    }

    private void BuyCoins(int quantity, int tint = 0, int definitionId = DefinitionId)
    {
        using var writer = new PacketWriter();
        new BaseCoinStorePacket(4).Write(writer);
        writer.Write(0UL);
        writer.Write(0);
        new ItemRecord { Definition = definitionId, Tint = tint }.Serialize(writer);
        writer.Write(quantity);
        Assert.IsTrue(CoinStoreSellToClientRequestPacketHandler.HandlePacket(_connection, writer.Buffer));
    }

    private void SellCoins(int itemId, int quantity)
    {
        using var writer = new PacketWriter();
        new BaseCoinStorePacket(5).Write(writer);
        writer.Write(0UL);
        writer.Write(itemId);
        writer.Write(quantity);
        Assert.IsTrue(CoinStoreBuyFromClientRequestPacketHandler.HandlePacket(_connection, writer.Buffer));
    }

    private void BuyMarketplace(int quantity)
    {
        using var writer = new PacketWriter();
        new PacketBaseInGamePurchase(3).Write(writer);
        writer.Write(""); writer.Write(""); writer.Write(1);
        writer.Write("en_US"); writer.Write(""); writer.Write(""); writer.Write("");
        writer.Write(1);
        writer.Write(10); writer.Write(1); writer.Write(quantity); writer.Write("0"); writer.Write("");
        Assert.IsTrue(PacketInGamePurchasePlaceOrderPacketHandler.HandlePacket(_connection, writer.Buffer));
    }

    private string Snapshot()
    {
        using var db = _factory.CreateDbContext();
        return JsonSerializer.Serialize(new
        {
            Characters = db.Characters.Select(c => new { c.Coins, c.StationCash }).ToArray(),
            Items = db.Items.AsNoTracking().ToArray(), Fixtures = db.HouseFixtures.AsNoTracking().ToArray()
        });
    }

    [TestMethod]
    public void CoinPurchase_InEmptyInventory_ChargesAndCanBePlacedAndReturned()
    {
        BuyCoins(2);
        using (var db = _factory.CreateDbContext())
        {
            Assert.AreEqual(9800, db.Characters.Single().Coins);
            Assert.AreEqual(2, db.Items.Single().Count);
            Assert.AreEqual(231, db.Items.Single().Tint);
        }
        var itemId = _player.Items.Single().Id;
        _zone.SetEditMode(_player, true);
        _zone.RequestFixture(_player, new ClientHousingPacketPlaceFixtureRequest { ItemId = itemId });
        _zone.PlaceFixture(_player, new ClientHousingPacketPlaceFixture { ItemId = itemId, Position = new(10, 0, 10, 1), Scale = 1 });
        using var verify = _factory.CreateDbContext();
        var fixture = verify.HouseFixtures.Single();
        Assert.AreEqual(itemId, fixture.ItemId);
        _zone.PickupFixture(_player, GuidHelper.GetFixtureGuid(fixture.Id));
        Assert.AreEqual(2, _player.Items.Single().Count);
    }

    [TestMethod]
    public void BuyMoreThenSellSpare_PreservesTheStackBackingPlacedFurniture()
    {
        BuyCoins(1);
        var itemId = _player.Items.Single().Id;
        Assert.IsTrue(_fixtures.TryPlace(_characterId, 100, itemId, new(10, 0, 10, 1), default, 1, out var fixture, out _));
        _player.Items.Single().Count = 0;
        BuyCoins(1);
        Assert.AreEqual(itemId, _player.Items.Single().Id);
        SellCoins(itemId, 1);
        using (var db = _factory.CreateDbContext())
        {
            Assert.AreEqual(0, db.Items.Single().Count);
            Assert.AreEqual(1, db.HouseFixtures.Count());
            Assert.AreEqual(9820, db.Characters.Single().Coins);
        }
        Assert.IsTrue(_fixtures.TryPickup(_characterId, 100, GuidHelper.GetFixtureGuid(fixture.Id), out _, out var count));
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void MarketplacePurchase_UsesFurnitureTypeAndDefaultTint()
    {
        BuyMarketplace(2);
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(9600, db.Characters.Single().StationCash);
        Assert.AreEqual(2, db.Items.Single().Count);
        Assert.AreEqual(231, db.Items.Single().Tint);
        Assert.AreEqual(2, _player.Items.Single().Count);
    }

    [TestMethod]
    [DataRow(218)] // Building block.
    [DataRow(2705)] // Newly listed, fixed-color alias.
    [DataRow(2712)] // Wall decoration.
    [DataRow(2930)] // Launch pad, placed as a static decoration.
    [DataRow(3016)] // High-price treasure chest.
    [DataRow(15972)] // Farming-category decoration.
    [DataRow(36105)] // Bed.
    [DataRow(36110)] // Bookcase.
    [DataRow(36137)] // Table.
    [DataRow(36142)] // Lamp.
    public void RealCatalogFurniture_CanBePurchasedPlacedAndPickedUp(int definitionId)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Resources"));
        _resources.ClientItemDefinitions.Clear();
        _resources.CoinStoreItems.Clear();
        _resources.HousingFixtures.Clear();
        Assert.IsTrue(_resources.ClientItemDefinitions.Load(Path.Combine(path, "ClientItemDefinitions.json")));
        Assert.IsTrue(_resources.CoinStoreItems.Load(Path.Combine(path, "CoinStoreItems.json")));
        Assert.IsTrue(_resources.HousingFixtures.Load(Path.Combine(path, "HousingFixtures.json")));
        Assert.IsTrue(_resources.Models.Load(Path.Combine(path, "Models.txt")));
        var definition = _resources.ClientItemDefinitions[definitionId];
        var startingCoins = definition.Cost + 1000;
        using (var db = _factory.CreateDbContext())
        {
            db.Characters.Single().Coins = startingCoins;
            db.SaveChanges();
        }
        _player.Coins = startingCoins;

        BuyCoins(1, definition.Icon.TintId, definitionId);
        var item = _player.Items.Single();
        Assert.AreEqual(definitionId, item.Definition);
        Assert.AreEqual(definition.Icon.TintId, item.Tint);
        Assert.AreEqual(1000, _player.Coins);
        _zone.SetEditMode(_player, true);
        _zone.RequestFixture(_player, new ClientHousingPacketPlaceFixtureRequest { ItemId = item.Id });
        _zone.PlaceFixture(_player, new ClientHousingPacketPlaceFixture
        {
            ItemId = item.Id, Position = new(10, 0, 10, 1), Scale = 1
        });
        ulong fixtureId;
        using (var db = _factory.CreateDbContext())
        {
            var fixture = db.HouseFixtures.Single();
            fixtureId = fixture.Id;
            Assert.AreEqual(definitionId, fixture.ItemDefinitionId);
            Assert.AreEqual(item.Tint, fixture.Tint);
            Assert.AreEqual(0, db.Items.Single().Count);
        }
        _zone.PickupFixture(_player, GuidHelper.GetFixtureGuid(fixtureId));
        using var verify = _factory.CreateDbContext();
        Assert.AreEqual(0, verify.HouseFixtures.Count());
        Assert.AreEqual(1, verify.Items.Single().Count);
        Assert.AreEqual(1000, verify.Characters.Single().Coins);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void UnpricedFurniture_CannotBeBoughtEvenWithAnEnabledOffer(int price)
    {
        Item.Cost = price;
        var before = Snapshot();
        BuyCoins(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void FurnitureWithoutPlacementSupport_CannotBeBought()
    {
        _resources.HousingFixtures.Clear();
        var before = Snapshot();
        BuyCoins(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void InvalidCoinQuantities_DoNotChangeMoneyOrStock(int quantity)
    {
        var before = Snapshot();
        BuyCoins(quantity);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void InvalidMarketplaceQuantities_DoNotChangeMoneyOrStock(int quantity)
    {
        var before = Snapshot();
        BuyMarketplace(quantity);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void MemberPrices_AreAppliedToBothCurrencies()
    {
        _player.MembershipStatus = 2;
        BuyCoins(1);
        BuyMarketplace(1);
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(9910, db.Characters.Single().Coins);
        Assert.AreEqual(9820, db.Characters.Single().StationCash);
        Assert.AreEqual(2, db.Items.Single().Count);
    }

    [TestMethod]
    public void StaleClientBalances_CannotSpendMoneyMissingFromTheDatabase()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Characters.Single().Coins = 0;
            db.Characters.Single().StationCash = 0;
            db.SaveChanges();
        }
        var before = Snapshot();
        BuyCoins(1);
        BuyMarketplace(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void StackLimits_RejectFurtherPurchasesWithoutChargingEitherCurrency()
    {
        Item.MaxStackSize = 1;
        BuyCoins(1);
        var before = Snapshot();
        BuyCoins(1);
        BuyMarketplace(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void DisabledOrHiddenFurnitureOffers_CannotBeBought(bool disabled, bool hidden)
    {
        _resources.CoinStoreItems[DefinitionId].BuyDisabled = disabled;
        _resources.CoinStoreItems[DefinitionId].Hidden = hidden;
        var before = Snapshot();
        BuyCoins(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void SellingAnUnplacedStack_CompletelyRemovesIt()
    {
        BuyCoins(2);
        SellCoins(_player.Items.Single().Id, 2);
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(0, db.Items.Count());
        Assert.AreEqual(9840, db.Characters.Single().Coins);
        Assert.AreEqual(0, _player.Items.Count);
    }

    [TestMethod]
    public void ResaleProceedsOverflow_IsRejectedWithoutDeletingStock()
    {
        BuyCoins(2);
        Item.ResellValue = int.MaxValue;
        var before = Snapshot();
        SellCoins(_player.Items.Single().Id, 2);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void UnlistedFurniture_CannotBePurchasedThroughAForgedCoinRequest()
    {
        _resources.CoinStoreItems.Clear();
        var before = Snapshot();
        BuyCoins(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void InsufficientFunds_DoNotGrantFurniture()
    {
        _player.Coins = 0;
        _player.StationCash = 0;
        var before = Snapshot();
        BuyCoins(1);
        BuyMarketplace(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void SaleFlagsAndInvalidSaleQuantities_LeaveStockUntouched()
    {
        BuyCoins(2);
        var itemId = _player.Items.Single().Id;
        var before = Snapshot();
        SellCoins(itemId, 0);
        SellCoins(itemId, -1);
        Item.NoSale = true;
        SellCoins(itemId, 1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void MarketplaceBundleQuantityOverflow_IsRejectedBeforeAnyGrant()
    {
        Offer.Entries.Single().Quantity = int.MaxValue;
        var before = Snapshot();
        BuyMarketplace(2);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void FullIntegerStock_RejectsAdditionalPurchasesWithoutOverflow()
    {
        BuyCoins(1);
        using (var db = _factory.CreateDbContext())
        {
            db.Items.Single().Count = int.MaxValue;
            db.SaveChanges();
        }
        _player.Items.Single().Count = int.MaxValue;
        var before = Snapshot();
        BuyCoins(1);
        BuyMarketplace(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void TintedCoinPurchase_KeepsDistinctStacksAndTheirAppearance()
    {
        _resources.CoinStoreItems[DefinitionId].TintGroupId = 1;
        BuyCoins(1, tint: 232);
        BuyCoins(1, tint: 233);
        using var db = _factory.CreateDbContext();
        Assert.AreEqual(2, db.Items.Count());
        Assert.AreEqual(1, db.Items.Single(item => item.Tint == 232).Count);
        Assert.AreEqual(1, db.Items.Single(item => item.Tint == 233).Count);
        Assert.AreEqual(2, _player.Items.Count);
    }

    [TestMethod]
    public void ZeroQuantityBundle_DoesNotChargeForAnEmptyGrant()
    {
        Offer.Entries.Single().Quantity = 0;
        var before = Snapshot();
        BuyMarketplace(1);
        Assert.AreEqual(before, Snapshot());
    }

    [TestMethod]
    public void MarketplacePurchaseLimit_IsEnforced()
    {
        Offer.MaxPerOrder = 1;
        var before = Snapshot();
        BuyMarketplace(2);
        Assert.AreEqual(before, Snapshot());
    }

    private sealed class Factory(DbContextOptions options) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => new SqliteDatabaseContext(options);
    }
}
