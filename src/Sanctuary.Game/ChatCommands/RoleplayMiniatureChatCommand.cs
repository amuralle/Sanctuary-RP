using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.ChatCommands;

public sealed class RoleplayMiniatureChatCommand : IChatCommand
{
    private const int ListPageSize = 8;
    private const int ModelPageSize = 6;
    private const float DefaultRemovalRadius = 15f;
    private const float MaximumRemovalRadius = 100f;

    private readonly IChatCommandManager _chatCommandManager;
    private readonly IResourceManager _resourceManager;

    public string KeyWord => "mini";
    public string Usage => "help | spawn | models | list | info | move | stop | say | emote | emotes | groups | remove | clear";
    public string Description => "Manage shared roleplay miniatures.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Player;

    public RoleplayMiniatureChatCommand(IChatCommandManager chatCommandManager, IResourceManager resourceManager)
    {
        _chatCommandManager = chatCommandManager;
        _resourceManager = resourceManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length == 0)
        {
            SendHelp(invoker);
            return true;
        }

        var commandArgs = args[1..];

        switch (args[0].ToLowerInvariant())
        {
            case "help":
                SendHelp(invoker);
                break;
            case "spawn":
                Spawn(invoker, commandArgs);
                break;
            case "models":
                ListModels(invoker, commandArgs);
                break;
            case "list":
                ListMiniatures(invoker, commandArgs);
                break;
            case "info":
                ShowInfo(invoker, commandArgs);
                break;
            case "say":
                Say(invoker, commandArgs);
                break;
            case "move":
                Move(invoker, commandArgs);
                break;
            case "stop":
                Stop(invoker, commandArgs);
                break;
            case "emote":
                Emote(invoker, commandArgs);
                break;
            case "emotes":
                ListEmotes(invoker, commandArgs);
                break;
            case "groups":
                ListGroups(invoker);
                break;
            case "remove":
                Remove(invoker, commandArgs);
                break;
            case "clear":
                Clear(invoker, commandArgs);
                break;
            default:
                return false;
        }

        return true;
    }

    private void Spawn(Player invoker, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var modelId))
        {
            SendSystemMessage(invoker, "Usage: !mini spawn [modelId] [@group] [name]");
            return;
        }

        var nameStart = 1;
        var group = RoleplayMiniatureRules.DefaultGroup;

        if (args[1].StartsWith('@'))
        {
            if (args.Length < 3)
            {
                SendSystemMessage(invoker, "Usage: !mini spawn [modelId] [@group] [name]");
                return;
            }

            group = args[1];
            nameStart = 2;
        }

        var name = string.Join(' ', args[nameStart..]);
        var result = invoker.Zone.TryCreateRoleplayMiniature(
            invoker, modelId, name, group, out var miniature);

        if (result != RoleplayMiniatureCreateResult.Success || miniature is null)
        {
            SendSystemMessage(invoker, GetCreateFailureMessage(result));
            return;
        }

        LogAction(invoker, "Spawn", miniature);
        SendSystemMessage(invoker,
            $"Spawned #{miniature.Id} {miniature.Name} (model {miniature.ModelId}) in @{miniature.Group}.");
    }

    private static string GetCreateFailureMessage(RoleplayMiniatureCreateResult result)
    {
        return result switch
        {
            RoleplayMiniatureCreateResult.InvalidModel =>
                "That model ID is not in the client model catalog. Try !mini models [search].",
            RoleplayMiniatureCreateResult.InvalidName =>
                $"Names must be 1-{RoleplayMiniatureRules.MaximumNameLength} characters on one line.",
            RoleplayMiniatureCreateResult.InvalidGroup =>
                $"Groups must be 1-{RoleplayMiniatureRules.MaximumGroupLength} letters, numbers, - or _.",
            RoleplayMiniatureCreateResult.PlayerLimitReached =>
                $"You already have {RoleplayMiniatureRules.MaximumPerPlayer} miniatures in this zone.",
            RoleplayMiniatureCreateResult.ZoneLimitReached =>
                $"This zone already has {RoleplayMiniatureRules.MaximumPerZone} miniatures.",
            RoleplayMiniatureCreateResult.RateLimited =>
                "Please wait a moment before placing another miniature.",
            _ => "The miniature could not be spawned."
        };
    }

    private void ListModels(Player invoker, string[] args)
    {
        var page = 1;
        var queryArgs = args;

        if (args.Length > 1 && int.TryParse(args[^1], out var parsedPage))
        {
            page = parsedPage;
            queryArgs = args[..^1];
        }

        if (page < 1)
        {
            SendSystemMessage(invoker, "Model page must be a positive number.");
            return;
        }

        var query = string.Join(' ', queryArgs).Trim();
        IEnumerable<ModelDefinition> models = _resourceManager.Models.Values;

        if (query == "*")
        {
            query = string.Empty;
        }
        else if (query.Length > 0)
        {
            if (int.TryParse(query, out var exactModelId))
            {
                models = models.Where(model => model.Id == exactModelId);
            }
            else
            {
                models = models.Where(model =>
                    model.ModelFileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    model.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    model.Descriptor.Contains(query, StringComparison.OrdinalIgnoreCase));
            }
        }

        var results = models.OrderBy(model => model.Id).ToArray();
        var pageCount = Math.Max(1, (results.Length + ModelPageSize - 1) / ModelPageSize);

        if (page > pageCount || results.Length == 0)
        {
            SendSystemMessage(invoker, results.Length == 0
                ? $"No models matched \"{query}\"."
                : $"There is no model page {page}; last page is {pageCount}.");
            return;
        }

        var entries = results
            .Skip((page - 1) * ModelPageSize)
            .Take(ModelPageSize)
            .Select(model => $"{model.Id}: {Path.GetFileName(model.ModelFileName)}")
            .ToArray();

        SendSystemMessage(invoker,
            $"Models {page}/{pageCount}{(query.Length > 0 ? $" for \"{query}\"" : string.Empty)}:\n" +
            string.Join('\n', entries));
    }

    private void ListMiniatures(Player invoker, string[] args)
    {
        if (args.Length > 2)
        {
            SendSystemMessage(invoker, "Usage: !mini list [all|mine|@group] [page]");
            return;
        }

        var page = 1;
        var filter = "all";

        if (args.Length > 0 && int.TryParse(args[0], out var firstPage))
        {
            page = firstPage;
        }
        else if (args.Length > 0)
        {
            filter = args[0].ToLowerInvariant();

            if (args.Length == 2 && !int.TryParse(args[1], out page))
            {
                SendSystemMessage(invoker, "Usage: !mini list [all|mine|@group] [page]");
                return;
            }
        }

        if (page < 1 || (filter is not "all" and not "mine" && !filter.StartsWith('@')))
        {
            SendSystemMessage(invoker, "Usage: !mini list [all|mine|@group] [page]");
            return;
        }

        IEnumerable<RoleplayMiniatureStatus> miniatures = invoker.Zone.GetRoleplayMiniatureStatuses();

        if (filter == "mine")
            miniatures = miniatures.Where(miniature => miniature.OwnerGuid == invoker.Guid);
        else if (filter.StartsWith('@'))
            miniatures = miniatures.Where(miniature => miniature.Group == filter[1..]);

        var results = miniatures.ToArray();
        var pageCount = Math.Max(1, (results.Length + ListPageSize - 1) / ListPageSize);

        if (page > pageCount || results.Length == 0)
        {
            SendSystemMessage(invoker, results.Length == 0
                ? $"No miniatures matched {filter}."
                : $"There is no miniature page {page}; last page is {pageCount}.");
            return;
        }

        var entries = results
            .Skip((page - 1) * ListPageSize)
            .Take(ListPageSize)
            .Select(miniature =>
                $"#{miniature.Id} {miniature.Name} [{miniature.ModelId}] @{miniature.Group} by {miniature.OwnerName}")
            .ToArray();

        SendSystemMessage(invoker, $"Miniatures {page}/{pageCount} ({results.Length}):\n{string.Join('\n', entries)}");
    }

    private void ShowInfo(Player invoker, string[] args)
    {
        if (args.Length != 1)
        {
            SendSystemMessage(invoker, "Usage: !mini info [#id|nearest]");
            return;
        }

        var miniatures = invoker.Zone.GetRoleplayMiniatureStatuses();
        RoleplayMiniatureStatus? miniature;

        if (args[0].Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            miniature = miniatures
                .OrderBy(candidate => DistanceSquared(candidate.Position, invoker.Position))
                .FirstOrDefault();
        }
        else if (TryParseId(args[0], out var id))
        {
            miniature = miniatures.FirstOrDefault(candidate => candidate.Id == id);
        }
        else
        {
            SendSystemMessage(invoker, "Usage: !mini info [#id|nearest]");
            return;
        }

        if (miniature is null)
        {
            SendSystemMessage(invoker, "No matching miniature exists in this zone.");
            return;
        }

        var distance = MathF.Sqrt(DistanceSquared(miniature.Position, invoker.Position));
        SendSystemMessage(invoker,
            $"#{miniature.Id} {miniature.Name} | model {miniature.ModelId} | @{miniature.Group} | " +
            $"placed by {miniature.OwnerName} | {distance:0.#}m away");
    }

    private void ListGroups(Player invoker)
    {
        var groups = invoker.Zone.GetRoleplayMiniatureStatuses()
            .GroupBy(miniature => miniature.Group)
            .OrderBy(group => group.Key)
            .Select(group => $"@{group.Key}: {group.Count()}")
            .ToArray();

        SendSystemMessage(invoker, groups.Length == 0
            ? "There are no miniature groups in this zone."
            : $"Miniature groups:\n{string.Join('\n', groups)}");
    }

    private void Say(Player invoker, string[] args)
    {
        if (args.Length < 2)
        {
            SendSystemMessage(invoker, "Usage: !mini say [#id|nearest] [message]");
            return;
        }

        var statuses = invoker.Zone.GetRoleplayMiniatureStatuses();
        int id;

        if (args[0].Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            var nearest = statuses
                .OrderBy(candidate => DistanceSquared(candidate.Position, invoker.Position))
                .FirstOrDefault();

            if (nearest is null)
            {
                SendSystemMessage(invoker, "There are no miniatures in this zone.");
                return;
            }

            id = nearest.Id;
        }
        else if (!TryParseId(args[0], out id))
        {
            SendSystemMessage(invoker, "Usage: !mini say [#id|nearest] [message]");
            return;
        }

        var message = string.Join(' ', args[1..]);
        var result = invoker.Zone.TrySpeakRoleplayMiniature(
            id, invoker.Guid, message, out var miniature);

        switch (result)
        {
            case RoleplayMiniatureSpeakResult.Success:
                _chatCommandManager.LogAction(
                    this, invoker, "Say", $"#{miniature.Id} {miniature.Name}", $"message=\"{message}\"");
                break;
            case RoleplayMiniatureSpeakResult.NotFound:
                SendSystemMessage(invoker, $"No miniature #{id} exists in this zone.");
                break;
            case RoleplayMiniatureSpeakResult.InvalidMessage:
                SendSystemMessage(invoker,
                    $"Speech must be 1-{RoleplayMiniatureRules.MaximumSpeechLength} characters on one line.");
                break;
            case RoleplayMiniatureSpeakResult.RateLimited:
                SendSystemMessage(invoker, "Please wait a moment before speaking through another miniature.");
                break;
        }
    }

    private void Move(Player invoker, string[] args)
    {
        if (args.Length < 2 || !TryResolveMiniatureId(invoker, args[0], out var id))
        {
            SendSystemMessage(invoker,
                "Usage: !mini move [#id|nearest] [here|x y z] [direct]");
            return;
        }

        var direct = args[^1].Equals("direct", StringComparison.OrdinalIgnoreCase);
        Vector3 destination;

        if (args[1].Equals("here", StringComparison.OrdinalIgnoreCase) && args.Length == (direct ? 3 : 2))
        {
            destination = new Vector3(invoker.Position.X, invoker.Position.Y, invoker.Position.Z);
        }
        else if (args.Length == (direct ? 5 : 4) &&
            float.TryParse(args[1], out var x) &&
            float.TryParse(args[2], out var y) &&
            float.TryParse(args[3], out var z))
        {
            destination = new Vector3(x, y, z);
        }
        else
        {
            SendSystemMessage(invoker,
                "Usage: !mini move [#id|nearest] [here|x y z] [direct]");
            return;
        }

        var result = invoker.Zone.TryMoveRoleplayMiniature(
            id, invoker.Guid, destination, direct, out var miniature);

        switch (result)
        {
            case RoleplayMiniatureMoveResult.Success:
                _chatCommandManager.LogAction(this, invoker, "Move", $"#{miniature.Id} {miniature.Name}",
                    $"destination=({destination.X:0.##},{destination.Y:0.##},{destination.Z:0.##}), direct={direct}");
                SendSystemMessage(invoker,
                    $"Moving #{miniature.Id} {miniature.Name}{(direct ? " directly" : string.Empty)}.");
                break;
            case RoleplayMiniatureMoveResult.NotFound:
                SendSystemMessage(invoker, $"No miniature #{id} exists in this zone.");
                break;
            case RoleplayMiniatureMoveResult.TooFar:
                SendSystemMessage(invoker,
                    $"A miniature can move at most {RoleplayMiniatureRules.MaximumMovementDistance:0}m per command.");
                break;
            case RoleplayMiniatureMoveResult.RateLimited:
                SendSystemMessage(invoker, "Please wait a moment before moving another miniature.");
                break;
        }
    }

    private void Stop(Player invoker, string[] args)
    {
        if (args.Length != 1 || !TryResolveMiniatureId(invoker, args[0], out var id))
        {
            SendSystemMessage(invoker, "Usage: !mini stop [#id|nearest]");
            return;
        }

        if (!invoker.Zone.TryStopRoleplayMiniature(id, out var miniature))
        {
            SendSystemMessage(invoker, $"No miniature #{id} exists in this zone.");
            return;
        }

        LogAction(invoker, "Stop", miniature);
        SendSystemMessage(invoker, $"Stopped #{miniature.Id} {miniature.Name}.");
    }

    private void Emote(Player invoker, string[] args)
    {
        if (args.Length != 2 ||
            !TryResolveMiniatureId(invoker, args[0], out var id) ||
            !RoleplayMiniatureRules.Emotes.TryGetValue(args[1], out var animationId))
        {
            SendSystemMessage(invoker, "Usage: !mini emote [#id|nearest] [emote]");
            return;
        }

        var result = invoker.Zone.TryEmoteRoleplayMiniature(
            id, invoker.Guid, animationId, out var miniature);

        switch (result)
        {
            case RoleplayMiniatureEmoteResult.Success:
                _chatCommandManager.LogAction(
                    this, invoker, "Emote", $"#{miniature.Id} {miniature.Name}", $"emote={args[1]}");
                break;
            case RoleplayMiniatureEmoteResult.NotFound:
                SendSystemMessage(invoker, $"No miniature #{id} exists in this zone.");
                break;
            case RoleplayMiniatureEmoteResult.RateLimited:
                SendSystemMessage(invoker, "Please wait a moment before animating another miniature.");
                break;
        }
    }

    private void ListEmotes(Player invoker, string[] args)
    {
        if (args.Length != 0)
        {
            SendSystemMessage(invoker, "Usage: !mini emotes");
            return;
        }

        SendSystemMessage(invoker,
            $"Miniature emotes:\n{string.Join(", ", RoleplayMiniatureRules.Emotes.Keys.Order())}");
    }

    private void Remove(Player invoker, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            SendSystemMessage(invoker, "Usage: !mini remove [#id|nearest] [radius]");
            return;
        }

        RoleplayMiniatureStatus? miniature;

        if (args[0].Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseRadius(args, 1, out var radius))
            {
                SendSystemMessage(invoker, $"Radius must be between 0 and {MaximumRemovalRadius:0}.");
                return;
            }

            if (!invoker.Zone.TryRemoveNearestRoleplayMiniature(
                invoker.Position, radius, out miniature))
            {
                SendSystemMessage(invoker, $"No miniature found within {radius:0.#}m.");
                return;
            }
        }
        else
        {
            if (args.Length != 1 || !TryParseId(args[0], out var id))
            {
                SendSystemMessage(invoker, "Usage: !mini remove [#id|nearest] [radius]");
                return;
            }

            if (!invoker.Zone.TryRemoveRoleplayMiniature(id, out miniature))
            {
                SendSystemMessage(invoker, $"No miniature #{id} exists in this zone.");
                return;
            }
        }

        LogAction(invoker, "Remove", miniature);
        SendSystemMessage(invoker, $"Removed #{miniature.Id} {miniature.Name}.");
    }

    private void Clear(Player invoker, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            SendSystemMessage(invoker, "Usage: !mini clear [all|mine|@group|nearby] [radius]");
            return;
        }

        var scope = args[0].ToLowerInvariant();
        int removed;

        if (scope == "all" && args.Length == 1)
        {
            removed = invoker.Zone.RemoveAllRoleplayMiniatures();
        }
        else if (scope == "mine" && args.Length == 1)
        {
            removed = invoker.Zone.RemoveRoleplayMiniaturesByOwner(invoker.Guid);
        }
        else if (scope.StartsWith('@') && args.Length == 1 &&
            RoleplayMiniatureRules.TryNormalizeGroup(scope, out var group))
        {
            removed = invoker.Zone.RemoveRoleplayMiniaturesByGroup(group);
        }
        else if (scope == "nearby" && TryParseRadius(args, 1, out var radius))
        {
            removed = invoker.Zone.RemoveNearbyRoleplayMiniatures(invoker.Position, radius);
        }
        else
        {
            SendSystemMessage(invoker, "Usage: !mini clear [all|mine|@group|nearby] [radius]");
            return;
        }

        _chatCommandManager.LogAction(this, invoker, "Clear", scope, $"removed={removed}");
        SendSystemMessage(invoker, $"Removed {removed} miniature(s) from {scope}.");
    }

    private static bool TryParseId(string argument, out int id)
    {
        return int.TryParse(argument.AsSpan(argument.StartsWith('#') ? 1 : 0), out id) && id > 0;
    }

    private static bool TryResolveMiniatureId(Player invoker, string argument, out int id)
    {
        if (argument.Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            var nearest = invoker.Zone.GetRoleplayMiniatureStatuses()
                .OrderBy(candidate => DistanceSquared(candidate.Position, invoker.Position))
                .FirstOrDefault();

            id = nearest?.Id ?? 0;
            return nearest is not null;
        }

        return TryParseId(argument, out id);
    }

    private static bool TryParseRadius(string[] args, int index, out float radius)
    {
        radius = DefaultRemovalRadius;

        if (args.Length > index && !float.TryParse(args[index], out radius))
            return false;

        return radius is > 0 and <= MaximumRemovalRadius;
    }

    private static float DistanceSquared(Vector4 left, Vector4 right)
    {
        return Vector3.DistanceSquared(
            new Vector3(left.X, left.Y, left.Z),
            new Vector3(right.X, right.Y, right.Z));
    }

    private void SendHelp(Player invoker)
    {
        SendSystemMessage(invoker,
            "Tabletop miniatures:\n" +
            "!mini spawn [modelId] [@group] [name]\n" +
            "!mini models [search|*] [page]\n" +
            "!mini list [all|mine|@group] [page]\n" +
            "!mini info [#id|nearest]\n" +
            "!mini move [#id|nearest] [here|x y z] [direct]\n" +
            "!mini stop [#id|nearest]\n" +
            "!mini say [#id|nearest] [message]\n" +
            "!mini emote [#id|nearest] [emote]\n" +
            "!mini emotes\n" +
            "!mini groups\n" +
            "!mini remove [#id|nearest] [radius]\n" +
            "!mini clear [all|mine|@group|nearby] [radius]");
    }

    private void SendSystemMessage(Player invoker, string message)
    {
        ChatHelper.SendSystemMessage(invoker, message);
    }

    private void LogAction(
        Player invoker,
        string action,
        RoleplayMiniatureStatus miniature)
    {
        _chatCommandManager.LogAction(
            this,
            invoker,
            action,
            $"#{miniature.Id} {miniature.Name}",
            $"model={miniature.ModelId}, group={miniature.Group}, owner={miniature.OwnerGuid}");
    }
}
