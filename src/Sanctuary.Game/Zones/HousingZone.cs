using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class HousingZone : BaseZone
{
    private const int HousingZoneHeadSize = 10;

    private const float HousePetSpeed = 6f;
    private const int HousePetUpdateIntervalMs = 500;
    private const float HousePetMinLegDistance = 5f;
    private const float HousePetMaxLegDistance = 20f;
    private const int HousePetMinPauseMs = 1000;
    private const int HousePetMaxPauseMs = 3000;
    private const float HousePetSpawnRadius = 1.5f;
    private readonly Dictionary<int, Npc> _housePets = [];

    private bool _isHouseLoaded;
    private ulong? _ownerGuid;
    private int _houseKey;
    private bool _disposed;
    private readonly object _fixtureLock = new();
    private readonly HousingFixtureManager _fixtures;
    private readonly Dictionary<ulong, HousingFixtureNpc> _fixtureActors = [];
    private readonly Dictionary<ulong, (int ItemId, long ExpiresAt, Guid PlacementId)> _pendingFixtures = [];

    public string HouseName { get; private set; } = string.Empty;

    public bool IsLocked { get; private set; }
    public bool IsFloraAllowed { get; private set; } = true;
    public bool PetAutospawn { get; private set; }

    public bool InEditMode { get; private set; }

    public HousingZone(HousingZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        _fixtures = serviceProvider.GetRequiredService<HousingFixtureManager>();
    }

    public override void OnClientIsReady(Player player)
    {
        lock (_fixtureLock)
        {
            if (_disposed)
                return;
            if (IsOwner(player))
            {
                InEditMode = false;
                _pendingFixtures.Remove(player.Guid);
            }
            LoadHouse();

            base.OnClientIsReady(player);

            SendShopData(player);
            SendHousingZoneData(player);
            SendFixtureItems(player);

            if (PetAutospawn && IsOwner(player))
                SpawnHousePets(player);
        }
    }

    public ulong? OwnerGuid
    {
        get
        {
            LoadHouse();
            return _ownerGuid;
        }
    }

    public bool IsOwner(Player player) => OwnerGuid == player.Guid && !player.IsPreviewingHouse;

    public void SetEditMode(Player player, bool inEditMode)
    {
        lock (_fixtureLock)
        {
            if (_disposed || !IsOwner(player) || _houseKey == HousingHelper.PreviewHouseKey || player.Zone != this)
                return;
            InEditMode = inEditMode;
            _pendingFixtures.Remove(player.Guid);
            SendHouseInfo(player);
            SendFixtureItems(player);
        }
    }

    public void RequestFixture(Player player, ClientHousingPacketPlaceFixtureRequest request)
    {
        lock (_fixtureLock)
        {
            Logger.LogInformation("Housing fixture requested by {player}: inventory item {item}, {unknown2}, {unknown3}, {unknown4}.",
                player.Guid, request.ItemId, request.Unknown2, request.Unknown3, request.Unknown4);
            var item = player.Items.FirstOrDefault(item => item.Id == request.ItemId && item.Count > 0);
            if (!CanEditFixtures(player) || item is null || !_resourceManager.HousingFixtures.TryGetValue(item.Definition, out var definition))
            {
                DenyFixture(player);
                return;
            }

            _pendingFixtures[player.Guid] = (item.Id, Environment.TickCount64 + 600_000, Guid.NewGuid());
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(item.Definition, out var itemDefinition))
            {
                _pendingFixtures.Remove(player.Guid);
                DenyFixture(player);
                return;
            }
            player.SendTunneled(new HousingPacketFixtureAsset
            {
                ModelId = definition.ModelId,
                ItemId = item.Id,
                Definition = definition.CreateClientDefinition(),
                Tint = item.Tint,
                Unknown3 = itemDefinition.TextureAlias,
                Unknown4 = itemDefinition.TintAlias
            });
        }
    }

    public void PlaceFixture(Player player, ClientHousingPacketPlaceFixture request)
    {
        lock (_fixtureLock)
        {
            if (!CanEditFixtures(player) || !_pendingFixtures.TryGetValue(player.Guid, out var pending)
                || pending.ExpiresAt < Environment.TickCount64 || pending.ItemId != request.ItemId
                || !_fixtures.TryPlace(GuidHelper.GetPlayerId(player.Guid), HouseId, pending.ItemId,
                    request.Position, request.Rotation, request.Scale, out var fixture, out var remaining, pending.PlacementId))
            {
                Logger.LogWarning("Housing placement rejected for {player}: inventory item {item}, position {position}, rotation {rotation}, scale {scale}.",
                    player.Guid, request.ItemId, request.Position, request.Rotation, request.Scale);
                DenyFixture(player);
                return;
            }
            _pendingFixtures.Remove(player.Guid);
            Logger.LogInformation("Placed housing fixture {fixture} in house {house} for {player}.", fixture.Id, DefinitionId, player.Guid);
            UpdateFixtureInventory(player, fixture.ItemId, remaining);
            BroadcastFixture(fixture);
            BroadcastHouseInfo();
            SendFixtureItems(player);
        }
    }

    public void SaveFixture(Player player, ClientHousingPacketSaveFixture request)
    {
        lock (_fixtureLock)
        {
            // Customization is server-owned until tint/customization editing is implemented.
            if (!CanEditFixtures(player) || !_fixtures.TryMove(GuidHelper.GetPlayerId(player.Guid), HouseId, request.FixtureGuid,
                    request.Position, request.Rotation, request.Scale, out var fixture))
            {
                Logger.LogWarning("Housing save rejected for {player}: fixture {fixture}, can edit {canEdit}, position {position}, rotation {rotation}, scale {scale}.",
                    player.Guid, request.FixtureGuid, CanEditFixtures(player), request.Position, request.Rotation, request.Scale);
                DenyFixture(player);
                return;
            }
            Logger.LogInformation("Saved housing fixture {fixture} in house {house}: position {position}, rotation {rotation}, scale {scale}.",
                fixture.Id, DefinitionId, request.Position, request.Rotation, request.Scale);
            BroadcastFixture(fixture);
        }
    }

    public void PickupFixture(Player player, ulong fixtureGuid)
    {
        lock (_fixtureLock)
        {
            if (!CanEditFixtures(player) || !_fixtures.TryPickup(GuidHelper.GetPlayerId(player.Guid), HouseId, fixtureGuid,
                    out var fixture, out var remaining))
            {
                Logger.LogWarning("Housing pickup rejected for {player}: fixture {fixture}, can edit {canEdit}.",
                    player.Guid, fixtureGuid, CanEditFixtures(player));
                DenyFixture(player);
                return;
            }
            Logger.LogInformation("Picked up housing fixture {fixture} in house {house} for {player}.", fixture.Id, DefinitionId, player.Guid);
            // The database commit succeeded; remove the actor before fallible follow-up queries.
            if (_fixtureActors.Remove(fixture.Id, out var actor))
                actor.Dispose();
            UpdateFixtureInventory(player, fixture.ItemId, remaining);
            foreach (var viewer in Players)
                viewer.SendTunneled(new HousingPacketFixtureRemove { FixtureGuid = fixtureGuid });
            BroadcastHouseInfo();
            SendFixtureItems(player);
        }
    }

    private bool CanEditFixtures(Player player) => !_disposed && player.Zone == this && IsOwner(player)
        && _houseKey != HousingHelper.PreviewHouseKey && InEditMode;

    private ulong HouseId => OwnerId is { } houseGuid ? GuidHelper.GetHouseId(houseGuid) : 0;

    private static void DenyFixture(Player player) => player.SendTunneled(new HousingPacketFixturePlacementDenied
    {
        Message = "That decoration cannot be placed or changed here."
    });

    private static void UpdateFixtureInventory(Player player, int itemId, int count)
    {
        var item = player.Items.FirstOrDefault(item => item.Id == itemId);
        if (item is not null)
            item.Count = count;
        player.SendTunneled(new ClientUpdatePacketItemUpdate { ItemGuid = itemId, Count = count });
    }

    private FixtureInstance CreateFixtureInstance(DbHouseFixture fixture)
    {
        var instance = HousingFixtureManager.CreateClientInstance(fixture, OwnerId!.Value,
            _fixtureActors[fixture.Id].Guid);
        if (_resourceManager.ClientItemDefinitions.TryGetValue(fixture.ItemDefinitionId, out var item))
        {
            instance.Customization = new CustomizationDetail
            {
                Type = 1, TextureAlias = item.TextureAlias, TintAlias = item.TintAlias, TintId = fixture.Tint
            };
        }
        return instance;
    }

    private void BroadcastFixture(DbHouseFixture fixture)
    {
        if (!_resourceManager.HousingFixtures.TryGetValue(fixture.ItemDefinitionId, out var definition)
            || !TryGetFixtureActor(fixture, out var actor))
            return;
        foreach (var viewer in Players)
        {
            viewer.SendTunneled(new HousingPacketFixtureUpdate
            {
                Instance = CreateFixtureInstance(fixture),
                Info = new FixtureInstanceInfo { FixtureGuid = (ulong)fixture.Id, ItemDefinitionId = fixture.ItemDefinitionId },
                Definition = definition.CreateClientDefinition()
            });
        }
        PublishFixtureActor(actor, fixture);
    }

    private bool TryGetFixtureActor(DbHouseFixture fixture, [MaybeNullWhen(false)] out HousingFixtureNpc actor)
    {
        if (_fixtureActors.TryGetValue(fixture.Id, out actor))
            return true;
        if (!_resourceManager.HousingFixtures.TryGetValue(fixture.ItemDefinitionId, out var definition)
            || !_resourceManager.ClientItemDefinitions.TryGetValue(fixture.ItemDefinitionId, out var item)
            || !TryAddNpc((zone, guid) => new HousingFixtureNpc(zone, guid, fixture, definition, item), out actor))
        {
            actor = null;
            Logger.LogError("Could not create world actor for saved fixture {fixture} in house {house}.", fixture.Id, DefinitionId);
            return false;
        }
        _fixtureActors.Add(fixture.Id, actor);
        return true;
    }

    private static void PublishFixtureActor(HousingFixtureNpc actor, DbHouseFixture fixture)
    {
        // Send the fixture-to-NPC association before making the world actor visible.
        actor.Visible = true;
        actor.ApplyFixture(fixture);
        foreach (var viewer in actor.VisiblePlayers.Values)
        {
            viewer.SendTunneled(actor.GetFixturePositionPacket());
            viewer.SendTunneled(new PlayerUpdatePacketUpdateScale { Guid = actor.Guid, Scale = actor.Scale });
        }
    }

    private void SendFixtureItems(Player player)
    {
        player.SendTunneled(new HousingPacketFixtureItemList
        {
            Infos = IsOwner(player) ? player.Items.Where(item => item.Count > 0 && _resourceManager.HousingFixtures.ContainsKey(item.Definition))
                .Select(item => new FixtureInstanceInfo { FixtureGuid = (ulong)item.Id, ItemDefinitionId = item.Definition }).ToList() : [],
            Definitions = _resourceManager.HousingFixtures.Values.Select(definition => definition.CreateClientDefinition()).ToList()
        });
    }

    public void SetHouseName(string houseName) => UpdateHouse(dbHouse => dbHouse.Name = houseName);

    public void SetLocked(bool isLocked) => UpdateHouse(dbHouse => dbHouse.IsLocked = isLocked);

    public void SetFloraAllowed(bool isFloraAllowed)
    {
        UpdateHouse(dbHouse => dbHouse.IsFloraAllowed = isFloraAllowed);

        foreach (var player in Players)
            SendZoneData(player);
    }

    public void SetPetAutospawn(Player player, bool petAutospawn)
    {
        UpdateHouse(dbHouse => dbHouse.PetAutospawn = petAutospawn);

        if (PetAutospawn)
            SpawnHousePets(player);
        else
            RemoveHousePets();
    }

    public void SendHouseInfo(Player player)
    {
        lock (_fixtureLock)
        {
            if (!_disposed && TryCreateHouseInfo(out var packet))
                player.SendTunneled(packet);
        }
    }

    private void BroadcastHouseInfo()
    {
        if (!TryCreateHouseInfo(out var packet))
            return;
        foreach (var player in Players)
            player.SendTunneled(packet);
    }

    private bool TryCreateHouseInfo([MaybeNullWhen(false)] out HousingPacketUpdateHouseInfo packet)
    {
        try
        {
            packet = new HousingPacketUpdateHouseInfo
            {
                InEditMode = InEditMode,
                // Moves do not change capacity; only query the count when house info is needed.
                CurFixtureCount = OwnerGuid is { } owner ? _fixtures.GetFixtureCount(GuidHelper.GetPlayerId(owner), HouseId) : 0,
                IsLocked = IsLocked,
                IsFloraAllowed = IsFloraAllowed,
                PetAutospawn = PetAutospawn
            };
            return true;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or RetryLimitExceededException)
        {
            Logger.LogError(ex, "Could not read fixture count for house {house}; skipping its info update.", DefinitionId);
            packet = null;
            return false;
        }
    }

    private void SpawnHousePets(Player owner)
    {
        if (!TryGetHousePetArea(out var buildArea))
            return;

        foreach (var petClientInfo in owner.Pets)
        {
            if (petClientInfo.Id == owner.ActivePetId || _housePets.ContainsKey(petClientInfo.Id)
                || !PetHelper.TryGetPetModel(_resourceManager, owner, petClientInfo.Id, out _, out var clientItemDefinition, out var modelDefinition)
                || !TryCreateNpc(out var npc))
            {
                continue;
            }

            PetHelper.ApplyPetAppearance(_resourceManager, owner, npc, petClientInfo, clientItemDefinition, modelDefinition);

            var heading = Random.Shared.NextSingle() * MathF.Tau;

            npc.Visible = true;
            npc.UpdatePosition(GetHousePetSpawnPosition(npc, buildArea, heading).ToVector4(), QuaternionExtensions.FromHeading(heading));
            npc.MovementController = CreateHousePetMovement(npc, buildArea);

            _housePets[petClientInfo.Id] = npc;
        }
    }

    public bool TryTakeHousePet(int petId, [MaybeNullWhen(false)] out Npc npc)
    {
        if (!_housePets.Remove(petId, out npc))
            return false;

        npc.MovementController = null;
        return true;
    }

    public bool TryReturnHousePet(Player player, int petId, Npc npc)
    {
        if (!PetAutospawn || !IsOwner(player) || _housePets.ContainsKey(petId) || !TryGetHousePetArea(out var buildArea))
            return false;

        npc.PositionUpdateFrequency = Npc.DefaultPositionUpdateFrequency;
        npc.NavMeshFilter = NavMeshFilter;

        var position = npc.Position.ToVector3();

        if (TryGetHousePetGround(npc, buildArea, position, out var ground) && ground.DistanceSquaredXZ(position) > HousePetSpawnRadius * HousePetSpawnRadius)
            npc.UpdatePosition(ground.ToVector4(), npc.Rotation);

        npc.MovementController = CreateHousePetMovement(npc, buildArea);
        _housePets[petId] = npc;
        return true;
    }

    private Vector3 GetHousePetSpawnPosition(Npc npc, BoundingBox buildArea, float heading)
    {
        var spawnPosition = SpawnPosition.ToVector3();

        if (TryGetHousePetGround(npc, buildArea, spawnPosition, out var ground) && npc.TryGetRandomWalkablePoint(ground, HousePetSpawnRadius, out var position))
            return position;

        return spawnPosition + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * HousePetSpawnRadius;
    }

    private static bool TryGetHousePetGround(Npc npc, BoundingBox buildArea, Vector3 position, out Vector3 ground)
    {
        var buildAreaMin = buildArea.Min.ToVector3();
        var buildAreaMax = buildArea.Max.ToVector3();

        return npc.TryGetNearestWalkablePoint(Vector3.Clamp(position, buildAreaMin, buildAreaMax), buildAreaMax - buildAreaMin, out ground);
    }

    private bool TryGetHousePetArea([MaybeNullWhen(false)] out BoundingBox buildArea)
    {
        buildArea = HousingHelper.TryGetHouseDefinition(_resourceManager, DefinitionId, out var houseDefinition) && !houseDefinition.NoHousePets
            ? houseDefinition.BuildAreas.FirstOrDefault()
            : null;

        return buildArea is not null;
    }

    private static NpcMovementController CreateHousePetMovement(Npc npc, BoundingBox buildArea) => new(npc, new NpcMovementDefinition
    {
        Type = NpcMovementType.Wander,
        Speed = HousePetSpeed,
        UpdateIntervalMs = HousePetUpdateIntervalMs,
        Radius = Vector3.Distance(buildArea.Min.ToVector3(), buildArea.Max.ToVector3()),
        MinLegDistance = HousePetMinLegDistance,
        MaxLegDistance = HousePetMaxLegDistance,
        MinPauseMs = HousePetMinPauseMs,
        MaxPauseMs = HousePetMaxPauseMs,
        Area =
        [
            [buildArea.Min.X, buildArea.Min.Y, buildArea.Min.Z],
            [buildArea.Max.X, buildArea.Min.Y, buildArea.Min.Z],
            [buildArea.Max.X, buildArea.Min.Y, buildArea.Max.Z],
            [buildArea.Min.X, buildArea.Min.Y, buildArea.Max.Z]
        ]
    });

    private void RemoveHousePets()
    {
        foreach (var npc in _housePets.Values)
            EntityHelper.RemovePlayerGracefully(npc);

        _housePets.Clear();
    }

    private void LoadHouse()
    {
        lock (_fixtureLock)
        {
            if (_disposed || _isHouseLoaded || OwnerId is not { } houseGuid)
                return;
            if (!HousingHelper.TryGetHouse(_dbContextFactory, houseGuid, out var dbHouse))
                return;

            _ownerGuid = GuidHelper.GetPlayerGuid(dbHouse.CharacterId);
            _houseKey = dbHouse.Key;
            ApplyHouse(dbHouse);
            _isHouseLoaded = true;
        }
    }

    private void UpdateHouse(Action<DbHouse> update)
    {
        LoadHouse();

        if (OwnerId is not { } houseGuid || _houseKey == HousingHelper.PreviewHouseKey)
            return;

        using var dbContext = _dbContextFactory.CreateDbContext();

        if (!HousingHelper.TryGetHouse(dbContext, houseGuid, out var dbHouse))
            return;

        update(dbHouse);
        dbContext.SaveChanges();

        ApplyHouse(dbHouse);

        foreach (var player in Players.Where(IsOwner))
        {
            SendHouseInfo(player);
            player.SendTunneled(HousingHelper.GetInstanceList(_resourceManager, _dbContextFactory, player));
        }
    }

    private void ApplyHouse(DbHouse dbHouse)
    {
        HouseName = dbHouse.Name;
        IsLocked = dbHouse.IsLocked;
        IsFloraAllowed = dbHouse.IsFloraAllowed;
        PetAutospawn = dbHouse.PetAutospawn;
    }

    private void SendHousingZoneData(Player player)
    {
        SendZoneData(player);
        SendInstanceData(player);
    }

    private void SendZoneData(Player player)
    {
        if (!HousingHelper.TryGetHouseDefinition(_resourceManager, DefinitionId, out var houseDefinition) || OwnerId is not { } houseGuid || OwnerGuid is not { } ownerGuid
            || !HousingHelper.TryGetHouse(_dbContextFactory, houseGuid, out var dbHouse))
            return;

        player.SendTunneled(new HousingPacketZoneData
        {
            IsPreview = player.IsPreviewingHouse,
            HeadSize = HousingZoneHeadSize,
            InstanceInfo = HousingHelper.GetInstanceInfo(dbHouse, ownerGuid, GetOwnerName(ownerGuid), houseDefinition, player.Guid)
        });
    }

    private void SendInstanceData(Player player)
    {
        if (!HousingHelper.TryGetHouseDefinition(_resourceManager, DefinitionId, out var houseDefinition) || OwnerId is not { } houseGuid || OwnerGuid is not { } ownerGuid)
            return;

        var instanceData = HousingHelper.GetInstanceData(houseGuid, ownerGuid, GetOwnerName(ownerGuid), houseDefinition);

        instanceData.Name = HouseName;
        instanceData.IsLocked = IsLocked;
        instanceData.IsFloraAllowed = IsFloraAllowed;
        instanceData.Preview = player.IsPreviewingHouse;

        lock (_fixtureLock)
        {
            if (_disposed)
                return;
            var fixtures = _fixtures.GetFixtures(GuidHelper.GetPlayerId(ownerGuid), HouseId)
                .Where(fixture => TryGetFixtureActor(fixture, out _)).ToList();
            instanceData.Fixtures = fixtures.ToDictionary(fixture => checked((uint)fixture.Id), CreateFixtureInstance);
            instanceData.CurFixtureCount = fixtures.Count;
            player.SendTunneled(new HousingPacketInstanceData { InstanceData = instanceData });
            foreach (var fixture in fixtures)
            {
                var actor = _fixtureActors[fixture.Id];
                if (!actor.Visible)
                    PublishFixtureActor(actor, fixture);
            }
        }
    }

    private string GetOwnerName(ulong ownerGuid) => TryGetPlayer(ownerGuid, out var owner) ? owner.Name.FullName : string.Empty;

    public override void Dispose()
    {
        lock (_fixtureLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            InEditMode = false;
            _pendingFixtures.Clear();
        }
        // Do not hold the editor lock while the base waits for zone ticks to finish.
        try
        {
            base.Dispose();
        }
        finally
        {
            lock (_fixtureLock)
                _fixtureActors.Clear();
        }
    }
}
