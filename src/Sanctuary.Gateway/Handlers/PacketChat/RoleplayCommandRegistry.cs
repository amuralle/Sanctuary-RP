using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Gateway.Helpers;

namespace Sanctuary.Gateway.Handlers;

public static class RoleplayCommandRegistry
{
    private const int ListPageSize = 8;
    private const int ModelPageSize = 6;
    private const float DefaultRemovalRadius = 15f;
    private const float MaximumRemovalRadius = 100f;

    private static IResourceManager _resourceManager = null!;
    private static ILogger _logger = null!;

    public static void Initialize(IResourceManager resourceManager, ILogger logger)
    {
        _resourceManager = resourceManager;
        _logger = logger;
    }

    public static bool IsCommand(string message)
    {
        return message.Equals("!mini", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("!mini ", StringComparison.OrdinalIgnoreCase);
    }

    public static void HandleCommand(GatewayConnection connection, string message)
    {
        var tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            SendHelp(connection);
            return;
        }

        var args = tokens[2..];

        switch (tokens[1].ToLowerInvariant())
        {
            case "help":
                SendHelp(connection);
                break;
            case "spawn":
                Spawn(connection, args);
                break;
            case "models":
                ListModels(connection, args);
                break;
            case "list":
                ListMiniatures(connection, args);
                break;
            case "info":
                ShowInfo(connection, args);
                break;
            case "groups":
                ListGroups(connection);
                break;
            case "remove":
                Remove(connection, args);
                break;
            case "clear":
                Clear(connection, args);
                break;
            default:
                SendSystemMessage(connection, "Unknown miniature command. Type !mini help.");
                break;
        }
    }

    private static void Spawn(GatewayConnection connection, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var modelId))
        {
            SendSystemMessage(connection, "Usage: !mini spawn [modelId] [@group] [name]");
            return;
        }

        var nameStart = 1;
        var group = RoleplayMiniatureRules.DefaultGroup;

        if (args[1].StartsWith('@'))
        {
            if (args.Length < 3)
            {
                SendSystemMessage(connection, "Usage: !mini spawn [modelId] [@group] [name]");
                return;
            }

            group = args[1];
            nameStart = 2;
        }

        var name = string.Join(' ', args[nameStart..]);
        var result = connection.Player.Zone.TryCreateRoleplayMiniature(
            connection.Player, modelId, name, group, out var miniature);

        if (result != RoleplayMiniatureCreateResult.Success || miniature is null)
        {
            SendSystemMessage(connection, GetCreateFailureMessage(result));
            return;
        }

        LogAction(connection, "Spawn", miniature);
        SendSystemMessage(connection,
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

    private static void ListModels(GatewayConnection connection, string[] args)
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
            SendSystemMessage(connection, "Model page must be a positive number.");
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
            SendSystemMessage(connection, results.Length == 0
                ? $"No models matched \"{query}\"."
                : $"There is no model page {page}; last page is {pageCount}.");
            return;
        }

        var entries = results
            .Skip((page - 1) * ModelPageSize)
            .Take(ModelPageSize)
            .Select(model => $"{model.Id}: {Path.GetFileName(model.ModelFileName)}")
            .ToArray();

        SendSystemMessage(connection,
            $"Models {page}/{pageCount}{(query.Length > 0 ? $" for \"{query}\"" : string.Empty)}:\n" +
            string.Join('\n', entries));
    }

    private static void ListMiniatures(GatewayConnection connection, string[] args)
    {
        if (args.Length > 2)
        {
            SendSystemMessage(connection, "Usage: !mini list [all|mine|@group] [page]");
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
                SendSystemMessage(connection, "Usage: !mini list [all|mine|@group] [page]");
                return;
            }
        }

        if (page < 1 || (filter is not "all" and not "mine" && !filter.StartsWith('@')))
        {
            SendSystemMessage(connection, "Usage: !mini list [all|mine|@group] [page]");
            return;
        }

        IEnumerable<RoleplayMiniatureStatus> miniatures = connection.Player.Zone.GetRoleplayMiniatureStatuses();

        if (filter == "mine")
            miniatures = miniatures.Where(miniature => miniature.OwnerGuid == connection.Player.Guid);
        else if (filter.StartsWith('@'))
            miniatures = miniatures.Where(miniature => miniature.Group == filter[1..]);

        var results = miniatures.ToArray();
        var pageCount = Math.Max(1, (results.Length + ListPageSize - 1) / ListPageSize);

        if (page > pageCount || results.Length == 0)
        {
            SendSystemMessage(connection, results.Length == 0
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

        SendSystemMessage(connection, $"Miniatures {page}/{pageCount} ({results.Length}):\n{string.Join('\n', entries)}");
    }

    private static void ShowInfo(GatewayConnection connection, string[] args)
    {
        if (args.Length != 1)
        {
            SendSystemMessage(connection, "Usage: !mini info [#id|nearest]");
            return;
        }

        var miniatures = connection.Player.Zone.GetRoleplayMiniatureStatuses();
        RoleplayMiniatureStatus? miniature;

        if (args[0].Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            miniature = miniatures
                .OrderBy(candidate => DistanceSquared(candidate.Position, connection.Player.Position))
                .FirstOrDefault();
        }
        else if (TryParseId(args[0], out var id))
        {
            miniature = miniatures.FirstOrDefault(candidate => candidate.Id == id);
        }
        else
        {
            SendSystemMessage(connection, "Usage: !mini info [#id|nearest]");
            return;
        }

        if (miniature is null)
        {
            SendSystemMessage(connection, "No matching miniature exists in this zone.");
            return;
        }

        var distance = MathF.Sqrt(DistanceSquared(miniature.Position, connection.Player.Position));
        SendSystemMessage(connection,
            $"#{miniature.Id} {miniature.Name} | model {miniature.ModelId} | @{miniature.Group} | " +
            $"placed by {miniature.OwnerName} | {distance:0.#}m away");
    }

    private static void ListGroups(GatewayConnection connection)
    {
        var groups = connection.Player.Zone.GetRoleplayMiniatureStatuses()
            .GroupBy(miniature => miniature.Group)
            .OrderBy(group => group.Key)
            .Select(group => $"@{group.Key}: {group.Count()}")
            .ToArray();

        SendSystemMessage(connection, groups.Length == 0
            ? "There are no miniature groups in this zone."
            : $"Miniature groups:\n{string.Join('\n', groups)}");
    }

    private static void Remove(GatewayConnection connection, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            SendSystemMessage(connection, "Usage: !mini remove [#id|nearest] [radius]");
            return;
        }

        RoleplayMiniatureStatus? miniature;

        if (args[0].Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseRadius(args, 1, out var radius))
            {
                SendSystemMessage(connection, $"Radius must be between 0 and {MaximumRemovalRadius:0}.");
                return;
            }

            if (!connection.Player.Zone.TryRemoveNearestRoleplayMiniature(
                connection.Player.Position, radius, out miniature))
            {
                SendSystemMessage(connection, $"No miniature found within {radius:0.#}m.");
                return;
            }
        }
        else
        {
            if (args.Length != 1 || !TryParseId(args[0], out var id))
            {
                SendSystemMessage(connection, "Usage: !mini remove [#id|nearest] [radius]");
                return;
            }

            if (!connection.Player.Zone.TryRemoveRoleplayMiniature(id, out miniature))
            {
                SendSystemMessage(connection, $"No miniature #{id} exists in this zone.");
                return;
            }
        }

        LogAction(connection, "Remove", miniature);
        SendSystemMessage(connection, $"Removed #{miniature.Id} {miniature.Name}.");
    }

    private static void Clear(GatewayConnection connection, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            SendSystemMessage(connection, "Usage: !mini clear [all|mine|@group|nearby] [radius]");
            return;
        }

        var scope = args[0].ToLowerInvariant();
        int removed;

        if (scope == "all" && args.Length == 1)
        {
            removed = connection.Player.Zone.RemoveAllRoleplayMiniatures();
        }
        else if (scope == "mine" && args.Length == 1)
        {
            removed = connection.Player.Zone.RemoveRoleplayMiniaturesByOwner(connection.Player.Guid);
        }
        else if (scope.StartsWith('@') && args.Length == 1 &&
            RoleplayMiniatureRules.TryNormalizeGroup(scope, out var group))
        {
            removed = connection.Player.Zone.RemoveRoleplayMiniaturesByGroup(group);
        }
        else if (scope == "nearby" && TryParseRadius(args, 1, out var radius))
        {
            removed = connection.Player.Zone.RemoveNearbyRoleplayMiniatures(connection.Player.Position, radius);
        }
        else
        {
            SendSystemMessage(connection, "Usage: !mini clear [all|mine|@group|nearby] [radius]");
            return;
        }

        _logger.LogInformation(
            "Miniature Clear|Actor: \"{ActorName}\" ({ActorGuid}), Scope: {Scope}, Removed: {Removed}",
            connection.Player.Name.FullName,
            connection.Player.Guid,
            scope,
            removed);
        SendSystemMessage(connection, $"Removed {removed} miniature(s) from {scope}.");
    }

    private static bool TryParseId(string argument, out int id)
    {
        return int.TryParse(argument.AsSpan(argument.StartsWith('#') ? 1 : 0), out id) && id > 0;
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

    private static void SendHelp(GatewayConnection connection)
    {
        SendSystemMessage(connection,
            "Tabletop miniatures:\n" +
            "!mini spawn [modelId] [@group] [name]\n" +
            "!mini models [search|*] [page]\n" +
            "!mini list [all|mine|@group] [page]\n" +
            "!mini info [#id|nearest]\n" +
            "!mini groups\n" +
            "!mini remove [#id|nearest] [radius]\n" +
            "!mini clear [all|mine|@group|nearby] [radius]");
    }

    private static void SendSystemMessage(GatewayConnection connection, string message)
    {
        ChatHelper.SendSystemMessage(connection, message);
    }

    private static void LogAction(
        GatewayConnection connection,
        string action,
        RoleplayMiniatureStatus miniature)
    {
        _logger.LogInformation(
            "Miniature {Action}|Actor: \"{ActorName}\" ({ActorGuid}), Miniature: #{MiniatureId} \"{Name}\" " +
            "(Model {ModelId}, Group {Group}, Owner {OwnerGuid})",
            action,
            connection.Player.Name.FullName,
            connection.Player.Guid,
            miniature.Id,
            miniature.Name,
            miniature.ModelId,
            miniature.Group,
            miniature.OwnerGuid);
    }
}
