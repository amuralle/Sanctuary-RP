using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Scripting;
using Sanctuary.UdpLibrary;
using Sanctuary.Game.Pathfinding;

namespace Sanctuary.Game.Zones;

public interface IZone : IScriptZone
{
    int DefinitionId { get; }
    #region Events

    void OnStart();
    void OnClientIsReady(Player entity);
    void OnClientFinishedLoading(Player entity);

    #endregion

    #region Entities

    IEnumerable<Npc> Npcs { get; }
    IEnumerable<Player> Players { get; }

    float TickDeltaSeconds { get; }

    bool TryGetNpc(ulong guid, [MaybeNullWhen(false)] out Npc npc);
    bool TryGetPlayer(ulong guid, [MaybeNullWhen(false)] out Player player);
    bool TryGetEntity(ulong guid, [MaybeNullWhen(false)] out IEntity entity);

    bool TryAddMount(Mount mount);
    bool TryAddPlayer(Player player);

    bool TryCreateNpc(ulong? guid, [MaybeNullWhen(false)] out Npc npc);
    bool TryCreateNpc(ulong? guid, NpcDefinition definition, [MaybeNullWhen(false)] out Npc npc);
    IReadOnlyList<CollectionNodePoolStatus> GetCollectionNodePoolStatuses();
    IReadOnlyList<CollectionNodeSpawnStatus> GetCollectionNodeSpawnStatuses(string? poolKey = null);
    bool TryPlaceCollectionNodeSpawn(string poolKey, Vector4 position, float heading,
        [MaybeNullWhen(false)] out CollectionNodeSpawnDefinition spawn, out bool activated);
    bool TryConfigureCollectionNodePool(string poolKey, int maxActiveNodes, int respawnSeconds,
        out int activeCount, out int targetActiveCount);
    bool TryRemoveCollectionNodeSpawn(int id,
        [MaybeNullWhen(false)] out CollectionNodeSpawnDefinition removedSpawn);
    bool TryRemoveNearestCollectionNodeSpawn(Vector4 position, float radius,
        [MaybeNullWhen(false)] out CollectionNodeSpawnDefinition removedSpawn);
    void CompleteCollectionNode(CollectionNode node);
    IReadOnlyList<RoleplayMiniatureStatus> GetRoleplayMiniatureStatuses();
    RoleplayMiniatureCreateResult TryCreateRoleplayMiniature(Player owner, int modelId, string name, string group,
        out RoleplayMiniatureStatus? miniature);
    RoleplayMiniatureSpeakResult TrySpeakRoleplayMiniature(int id, ulong actorGuid, string message,
        [MaybeNullWhen(false)] out RoleplayMiniatureStatus miniature);
    RoleplayMiniatureMoveResult TryMoveRoleplayMiniature(int id, ulong actorGuid, Vector3 destination, bool direct,
        Quaternion arrivalRotation, [MaybeNullWhen(false)] out RoleplayMiniatureStatus miniature);
    bool TryStopRoleplayMiniature(int id, [MaybeNullWhen(false)] out RoleplayMiniatureStatus miniature);
    RoleplayMiniatureEmoteResult TryEmoteRoleplayMiniature(int id, ulong actorGuid, int animationId,
        [MaybeNullWhen(false)] out RoleplayMiniatureStatus miniature);
    bool TryRemoveRoleplayMiniature(int id, [MaybeNullWhen(false)] out RoleplayMiniatureStatus miniature);
    bool TryRemoveNearestRoleplayMiniature(Vector4 position, float radius,
        [MaybeNullWhen(false)] out RoleplayMiniatureStatus miniature);
    int RemoveAllRoleplayMiniatures();
    int RemoveRoleplayMiniaturesByOwner(ulong ownerGuid);
    int RemoveRoleplayMiniaturesByGroup(string group);
    int RemoveNearbyRoleplayMiniatures(Vector4 position, float radius);
    bool TryCreateMount(Player rider, MountDefinition definition, [MaybeNullWhen(false)] out Mount mount);
    bool TryCreatePlayer(ulong guid, UdpConnection connection, [MaybeNullWhen(false)] out Player player);

    bool TryRemoveNpc(ulong guid);
    bool TryRemovePlayer(ulong guid);



    #endregion

    #region Zone System

    ZoneTile GetTileFromPosition(Vector4 position);
    void UpdateEntityZoneTile(IEntity entity, ZoneTile from, ZoneTile to);

    #endregion

    Pathfinder<MapNode>? Pathfinder { get; }
}
