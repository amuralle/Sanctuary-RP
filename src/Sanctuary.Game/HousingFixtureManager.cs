using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game;

public sealed class HousingFixtureManager(IResourceManager resources, IDbContextFactory<DatabaseContext> factory,
    ILogger<HousingFixtureManager> logger)
{
    public const int MaxFixtureCount = 9999;

    public List<DbHouseFixture> GetFixtures(ulong characterId, ulong houseId)
    {
        using var db = factory.CreateDbContext();
        return db.HouseFixtures.AsNoTracking().Where(fixture => fixture.CharacterId == characterId && fixture.HouseId == houseId)
            .OrderBy(fixture => (long)fixture.Id).ToList();
    }

    public int GetFixtureCount(ulong characterId, ulong houseId)
    {
        using var db = factory.CreateDbContext();
        return db.HouseFixtures.Count(fixture => fixture.CharacterId == characterId && fixture.HouseId == houseId);
    }

    public bool IsValidTransform(int houseId, Vector4 position, Quaternion rotation, float scale)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) && float.IsFinite(position.W)
            && float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z) && float.IsFinite(rotation.W)
            // These four floats contain client heading/pitch/roll; zero is a valid rotation.
            && float.IsFinite(scale) && scale is >= 0.25f and <= 4f
            && HousingHelper.TryGetHouseDefinition(resources, houseId, out var house)
            && house.BuildAreas.Any(area => position.X >= area.Min.X && position.X <= area.Max.X
                && position.Y >= area.Min.Y && position.Y <= area.Max.Y && position.Z >= area.Min.Z && position.Z <= area.Max.Z);
    }

    public bool TryPlace(ulong characterId, ulong houseId, int itemId, Vector4 position, Quaternion rotation, float scale,
        out DbHouseFixture fixture, out int remaining, Guid? placementId = null)
    {
        fixture = null!;
        remaining = 0;
        var operationId = placementId ?? Guid.NewGuid();

        try
        {
            var result = Execute(db =>
            {
                using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
                var failure = (Fixture: (DbHouseFixture?)null, Count: 0);
                var house = db.Houses.AsNoTracking().SingleOrDefault(row => row.Id == houseId && row.CharacterId == characterId);
                if (house is null || house.Key == HousingHelper.PreviewHouseKey
                    || !IsValidTransform(house.ZoneId, position, rotation, scale))
                    return failure;
                // A retried commit must return the original placement, not consume another unit.
                var existing = db.HouseFixtures.AsNoTracking().SingleOrDefault(row => row.PlacementId == operationId);
                if (existing is not null)
                {
                    return existing.CharacterId == characterId && existing.HouseId == houseId && existing.ItemId == itemId
                        ? (Fixture: (DbHouseFixture?)existing, Count: db.Items.Where(row => row.CharacterId == characterId && row.Id == itemId).Select(row => row.Count).Single())
                        : failure;
                }
                var item = db.Items.AsNoTracking().SingleOrDefault(item => item.CharacterId == characterId && item.Id == itemId);
                if (item is null || !resources.HousingFixtures.ContainsKey(item.Definition))
                    return failure;

                // Reserve the unit in the database, not from a possibly stale player inventory snapshot.
                if (db.Items.Where(row => row.CharacterId == characterId && row.Id == itemId && row.Count > 0)
                        .ExecuteUpdate(update => update.SetProperty(row => row.Count, row => row.Count - 1)) != 1)
                    return failure;
                if (db.HouseFixtures.Count(row => row.CharacterId == characterId && row.HouseId == houseId) >= MaxFixtureCount)
                    return failure;
                // Houses and fixtures share the native housing GUID sequence.
                var identity = new DbHousingGuid();
                db.HousingGuids.Add(identity);
                db.SaveChanges();
                // Instance snapshots key fixtures by their 32-bit local ID, not their prefixed GUID.
                if (identity.Id > uint.MaxValue)
                    return failure;

                var placed = new DbHouseFixture
                {
                    Id = identity.Id,
                    PlacementId = operationId,
                    HouseId = houseId,
                    CharacterId = characterId,
                    ItemId = itemId,
                    ItemDefinitionId = item.Definition,
                    Tint = item.Tint
                };
                SetTransform(placed, position, rotation, scale);
                db.HouseFixtures.Add(placed);
                db.SaveChanges();
                var count = db.Items.Where(row => row.CharacterId == characterId && row.Id == itemId).Select(row => row.Count).Single();
                transaction.Commit();
                return (Fixture: (DbHouseFixture?)placed, Count: count);
            });
            fixture = result.Fixture!;
            remaining = result.Count;
            return fixture is not null;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(ex, "Could not place item {item} in house {house} for character {character}.", itemId, houseId, characterId);
            return false;
        }
    }

    public bool TryMove(ulong characterId, ulong houseId, ulong fixtureGuid, Vector4 position, Quaternion rotation, float scale,
        out DbHouseFixture fixture)
    {
        fixture = null!;
        if (!GuidHelper.IsFixtureGuid(fixtureGuid))
            return false;
        var fixtureId = GuidHelper.GetFixtureId(fixtureGuid);

        try
        {
            using var db = factory.CreateDbContext();
            var placed = db.HouseFixtures.Include(row => row.House)
                .SingleOrDefault(row => row.Id == fixtureId && row.CharacterId == characterId && row.HouseId == houseId);
            if (placed is null || placed.House.CharacterId != characterId || placed.House.Key == HousingHelper.PreviewHouseKey
                || !IsValidTransform(placed.House.ZoneId, position, rotation, scale))
                return false;
            SetTransform(placed, position, rotation, scale);
            db.SaveChanges();
            fixture = placed;
            return true;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(ex, "Could not move fixture {fixture} in house {house}.", fixtureGuid, houseId);
            return false;
        }
    }

    public bool TryPickup(ulong characterId, ulong houseId, ulong fixtureGuid, out DbHouseFixture fixture, out int remaining)
    {
        fixture = null!;
        remaining = 0;
        if (!GuidHelper.IsFixtureGuid(fixtureGuid))
            return false;
        var fixtureId = GuidHelper.GetFixtureId(fixtureGuid);

        DbHouseFixture? previousAttempt = null;
        try
        {
            var result = Execute(db =>
            {
                using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
                var placed = db.HouseFixtures.SingleOrDefault(row => row.Id == fixtureId && row.CharacterId == characterId
                    && row.HouseId == houseId && row.House.CharacterId == characterId
                    && row.House.Key != HousingHelper.PreviewHouseKey);
                var failure = (Fixture: (DbHouseFixture?)null, Count: 0);
                if (placed is null)
                {
                    // A commit may have succeeded even if its acknowledgement triggered a retry.
                    if (previousAttempt is not null)
                    {
                        var recoveredCount = db.Items.Where(row => row.CharacterId == characterId && row.Id == previousAttempt.ItemId)
                            .Select(row => (int?)row.Count).SingleOrDefault();
                        if (recoveredCount.HasValue)
                            return (Fixture: previousAttempt, Count: recoveredCount.Value);
                    }
                    return failure;
                }
                if (db.Items.Where(row => row.CharacterId == characterId && row.Id == placed.ItemId && row.Count < int.MaxValue)
                        .ExecuteUpdate(update => update.SetProperty(row => row.Count, row => row.Count + 1)) != 1)
                    return failure;
                db.HouseFixtures.Remove(placed);
                db.SaveChanges();
                var count = db.Items.Where(row => row.CharacterId == characterId && row.Id == placed.ItemId).Select(row => row.Count).Single();
                previousAttempt = placed;
                transaction.Commit();
                return (Fixture: (DbHouseFixture?)placed, Count: count);
            });
            fixture = result.Fixture!;
            remaining = result.Count;
            return fixture is not null;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(ex, "Could not pick up fixture {fixture} in house {house}.", fixtureGuid, houseId);
            return false;
        }
    }

    private T Execute<T>(Func<DatabaseContext, T> operation)
    {
        using var context = factory.CreateDbContext();
        return context.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var attempt = factory.CreateDbContext();
            return operation(attempt);
        });
    }

    public static FixtureInstance CreateClientInstance(DbHouseFixture fixture, ulong houseGuid, ulong npcGuid) => new()
    {
        Guid = GuidHelper.GetFixtureGuid(fixture.Id),
        HouseGuid = houseGuid,
        FixtureDefinitionId = fixture.ItemDefinitionId,
        NpcGuid = npcGuid,
        Position = new Vector4(fixture.PositionX, fixture.PositionY, fixture.PositionZ, 1),
        Rotation = new Quaternion(fixture.RotationX, fixture.RotationY, fixture.RotationZ, fixture.RotationW),
        Tilt = GetSurfaceNormal(fixture),
        Scale = fixture.Scale,
        TintId = fixture.Tint
    };

    private static Quaternion GetSurfaceNormal(DbHouseFixture fixture)
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(fixture.RotationX, fixture.RotationY, fixture.RotationZ);
        return new Quaternion(Vector3.Transform(Vector3.UnitY, rotation), 0);
    }

    private static void SetTransform(DbHouseFixture fixture, Vector4 position, Quaternion rotation, float scale)
    {
        fixture.PositionX = position.X;
        fixture.PositionY = position.Y;
        fixture.PositionZ = position.Z;
        fixture.RotationX = rotation.X;
        fixture.RotationY = rotation.Y;
        fixture.RotationZ = rotation.Z;
        fixture.RotationW = rotation.W;
        fixture.Scale = scale;
    }
}
