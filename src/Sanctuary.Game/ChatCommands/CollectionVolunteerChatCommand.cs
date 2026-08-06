using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Core.CollectionVolunteers;
using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.ChatCommands;

public sealed class CollectionVolunteerChatCommand : IChatCommand
{
    private const int PageSize = 8;

    private readonly IChatCommandManager _chatCommandManager;
    private readonly CollectionVolunteerStore _store;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;

    public string KeyWord => "volunteer";
    public string Usage => "help | pools | place [pool] [note] | mine [page] | undo [#id|last]";
    public string Description => "Submit collection node locations for review.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Player;

    public CollectionVolunteerChatCommand(
        IChatCommandManager chatCommandManager,
        CollectionVolunteerStore store,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _chatCommandManager = chatCommandManager;
        _store = store;
        _dbContextFactory = dbContextFactory;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            SendHelp(invoker);
            return true;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "pools":
                ListPools(invoker);
                return true;
            case "place":
                Place(invoker, args[1..]);
                return true;
            case "mine":
                ListMine(invoker, args[1..]);
                return true;
            case "undo":
                Undo(invoker, args[1..]);
                return true;
            default:
                return false;
        }
    }

    private static void ListPools(Player invoker)
    {
        var pools = invoker.Zone.GetCollectionNodePoolStatuses()
            .OrderBy(pool => pool.Key)
            .Select(pool => pool.Key)
            .ToArray();

        ChatHelper.SendSystemMessage(invoker, pools.Length == 0
            ? "This zone has no collection pools available for volunteer placement."
            : $"Collection pools in this zone:\n{string.Join(", ", pools)}");
    }

    private void Place(Player invoker, string[] args)
    {
        if (args.Length < 1)
        {
            ChatHelper.SendSystemMessage(invoker, "Usage: !volunteer place [pool] [optional note]");
            return;
        }

        var poolKey = CollectionVolunteerStore.NormalizePool(args[0]);
        if (!invoker.Zone.GetCollectionNodePoolStatuses().Any(pool => pool.Key == poolKey))
        {
            ChatHelper.SendSystemMessage(invoker,
                "Unknown pool in this zone. Use !volunteer pools to see available pools.");
            return;
        }

        var characterId = GuidHelper.GetPlayerId(invoker.Guid);
        var userId = GetUserId(characterId);

        if (userId == 0)
        {
            ChatHelper.SendSystemMessage(invoker, "Your account could not be identified for this submission.");
            return;
        }

        var placement = new CollectionVolunteerPlacement
        {
            ZoneId = invoker.Zone.DefinitionId,
            Pool = poolKey,
            X = invoker.Position.X,
            Y = invoker.Position.Y,
            Z = invoker.Position.Z,
            Heading = MathF.Atan2(invoker.Rotation.X, invoker.Rotation.Z),
            Note = CollectionVolunteerStore.NormalizeNote(
                args.Length > 1 ? string.Join(' ', args[1..]) : null),
            SubmittedByUserId = userId,
            SubmittedByCharacterId = characterId,
            SubmittedByCharacterName = invoker.Name.FullName,
            Status = CollectionVolunteerPlacementStatus.Pending
        };

        var result = _store.TryAdd(placement, out var stored);
        if (result != CollectionVolunteerAddResult.Success || stored is null)
        {
            ChatHelper.SendSystemMessage(invoker, GetAddFailureMessage(result));
            return;
        }

        var heading = placement.Heading;
        if (!invoker.Zone.TryPlaceCollectionNodeSpawn(
            stored.Pool, invoker.Position, heading, out var spawn, out var activated))
        {
            _store.TryDelete(stored.Id, stored.SubmittedByUserId, actorIsAdmin: false);
            ChatHelper.SendSystemMessage(invoker,
                "The location was not saved because its live collection node could not be created.");
            return;
        }

        if (!_store.TryAttachHardPoint(
            stored.Id, stored.SubmittedByUserId, spawn.Id, out var materialized) || materialized is null)
        {
            invoker.Zone.TryRemoveCollectionNodeSpawn(spawn.Id, out _);
            _store.TryDelete(stored.Id, stored.SubmittedByUserId, actorIsAdmin: false);
            ChatHelper.SendSystemMessage(invoker,
                "The location was not saved because its live node could not be linked to the submission.");
            return;
        }

        stored = materialized;

        _chatCommandManager.LogAction(
            this,
            invoker,
            "Submit placement",
            $"{stored.Pool} #{stored.Id}",
            $"zone={stored.ZoneId}, position=({stored.X:0.00}, {stored.Y:0.00}, {stored.Z:0.00})");
        ChatHelper.SendSystemMessage(invoker,
            $"Submitted {stored.Pool} location #{stored.Id} and placed live hard point #{spawn.Id}; " +
            (activated ? "the node is active now." : "the node is saved but the pool is at capacity."));
    }

    private static string GetAddFailureMessage(CollectionVolunteerAddResult result)
    {
        return result switch
        {
            CollectionVolunteerAddResult.Duplicate =>
                "A matching location is already submitted within 1.5 units.",
            CollectionVolunteerAddResult.RateLimited =>
                "Please wait two seconds before submitting another location.",
            CollectionVolunteerAddResult.PendingLimitReached =>
                "Your pending placement queue is full. Please wait for a reviewer.",
            _ => "That collection location could not be submitted. Keep notes under 200 characters."
        };
    }

    private void ListMine(Player invoker, string[] args)
    {
        var page = 1;
        if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out page) || page < 1)))
        {
            ChatHelper.SendSystemMessage(invoker, "Usage: !volunteer mine [page]");
            return;
        }

        var userId = GetUserId(GuidHelper.GetPlayerId(invoker.Guid));
        if (userId == 0)
        {
            ChatHelper.SendSystemMessage(invoker, "Your account could not be identified.");
            return;
        }

        var placements = _store.GetAll()
            .Where(placement => placement.SubmittedByUserId == userId)
            .OrderByDescending(placement => placement.Id)
            .ToArray();
        var pageCount = Math.Max(1, (placements.Length + PageSize - 1) / PageSize);

        if (placements.Length == 0 || page > pageCount)
        {
            ChatHelper.SendSystemMessage(invoker, placements.Length == 0
                ? "You have not submitted any collection locations."
                : $"There is no submission page {page}; last page is {pageCount}.");
            return;
        }

        var entries = placements
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(placement =>
                $"#{placement.Id} {placement.Pool} {placement.Status.ToString().ToLowerInvariant()} " +
                $"({placement.X:0.0}, {placement.Y:0.0}, {placement.Z:0.0})")
            .ToArray();

        ChatHelper.SendSystemMessage(invoker,
            $"Your collection submissions {page}/{pageCount}:\n{string.Join('\n', entries)}");
    }

    private void Undo(Player invoker, string[] args)
    {
        if (args.Length != 1)
        {
            ChatHelper.SendSystemMessage(invoker, "Usage: !volunteer undo [#id|last]");
            return;
        }

        var userId = GetUserId(GuidHelper.GetPlayerId(invoker.Guid));
        if (userId == 0)
        {
            ChatHelper.SendSystemMessage(invoker, "Your account could not be identified.");
            return;
        }

        var ownPending = _store.GetAll()
            .Where(placement =>
                placement.SubmittedByUserId == userId &&
                placement.Status == CollectionVolunteerPlacementStatus.Pending)
            .OrderByDescending(placement => placement.Id)
            .ToArray();

        var id = args[0].Equals("last", StringComparison.OrdinalIgnoreCase)
            ? ownPending.FirstOrDefault()?.Id ?? 0
            : TryParseId(args[0], out var parsedId) ? parsedId : 0;
        var matchingPlacement = ownPending.FirstOrDefault(placement => placement.Id == id);

        if (matchingPlacement is null)
        {
            ChatHelper.SendSystemMessage(invoker, "No matching pending submission was found.");
            return;
        }

        if (matchingPlacement.HardPointId is int hardPointId)
        {
            var hardPointExists = invoker.Zone.GetCollectionNodeSpawnStatuses()
                .Any(spawn => spawn.Id == hardPointId);
            if (!_store.TryDetachHardPoint(
                id, matchingPlacement.SubmittedByUserId, hardPointId, out _))
            {
                ChatHelper.SendSystemMessage(invoker, "The submission's live hard point could not be detached.");
                return;
            }

            if (hardPointExists && !invoker.Zone.TryRemoveCollectionNodeSpawn(hardPointId, out _))
            {
                _store.TryAttachHardPoint(id, matchingPlacement.SubmittedByUserId, hardPointId, out _);
                ChatHelper.SendSystemMessage(invoker, "The live hard point could not be removed.");
                return;
            }
        }

        if (!_store.TryDelete(id, matchingPlacement.SubmittedByUserId, actorIsAdmin: false))
        {
            ChatHelper.SendSystemMessage(invoker, "The pending submission could not be removed.");
            return;
        }

        _chatCommandManager.LogAction(this, invoker, "Undo placement", $"#{id}", null);
        ChatHelper.SendSystemMessage(invoker, $"Removed pending collection submission #{id}.");
    }

    private static bool TryParseId(string argument, out int id)
    {
        return int.TryParse(argument.AsSpan(argument.StartsWith('#') ? 1 : 0), out id) && id > 0;
    }

    private ulong GetUserId(ulong characterId)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        return dbContext.Characters
            .Where(character => character.Id == characterId)
            .Select(character => character.UserId)
            .SingleOrDefault();
    }

    private static void SendHelp(Player invoker)
    {
        ChatHelper.SendSystemMessage(invoker,
            "Collection volunteer placement:\n" +
            "!volunteer pools\n" +
            "!volunteer place [pool] [optional note]\n" +
            "!volunteer mine [page]\n" +
            "!volunteer undo [#id|last]\n" +
            "Submissions create live collectible nodes immediately and remain queued for review.");
    }
}
