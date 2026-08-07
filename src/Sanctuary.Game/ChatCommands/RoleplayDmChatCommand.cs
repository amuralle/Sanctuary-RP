using System;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.ChatCommands;

public sealed class RoleplayDmChatCommand : IChatCommand
{
    private readonly IChatCommandManager _chatCommandManager;
    private readonly IResourceManager _resourceManager;

    public string KeyWord => "dm";
    public string Usage => "model [modelId|reset] | invisible [on|off|toggle] | status";
    public string Description => "Manage admin-only roleplay presentation tools.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public RoleplayDmChatCommand(IChatCommandManager chatCommandManager, IResourceManager resourceManager)
    {
        _chatCommandManager = chatCommandManager;
        _resourceManager = resourceManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length == 0)
            return false;

        switch (args[0].ToLowerInvariant())
        {
            case "model":
                return SetModel(invoker, args[1..]);
            case "invisible":
                return SetVisibility(invoker, args[1..]);
            case "status":
                return ShowStatus(invoker, args[1..]);
            default:
                return false;
        }
    }

    private bool SetModel(Player invoker, string[] args)
    {
        if (args.Length != 1)
            return false;

        if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            invoker.ClearRoleplayTemporaryAppearance();
            _chatCommandManager.LogAction(this, invoker, "Reset Model", invoker.Name.FullName, null);
            ChatHelper.SendSystemMessage(invoker, "Restored your normal player model.");
            return true;
        }

        if (!int.TryParse(args[0], out var modelId) || !_resourceManager.Models.ContainsKey(modelId))
        {
            ChatHelper.SendSystemMessage(invoker,
                "That model ID is not in the client model catalog. Try !mini models [search].");
            return true;
        }

        invoker.SetRoleplayTemporaryAppearance(modelId);
        _chatCommandManager.LogAction(this, invoker, "Set Model", invoker.Name.FullName, $"model={modelId}");
        ChatHelper.SendSystemMessage(invoker, $"Using model {modelId}. Run !dm model reset to restore your character.");
        return true;
    }

    private bool SetVisibility(Player invoker, string[] args)
    {
        if (args.Length != 1)
            return false;

        bool visible;
        switch (args[0].ToLowerInvariant())
        {
            case "on":
                visible = false;
                break;
            case "off":
                visible = true;
                break;
            case "toggle":
                visible = invoker.RoleplayHidden;
                break;
            default:
                return false;
        }

        invoker.SetRoleplayVisibility(visible);
        _chatCommandManager.LogAction(
            this, invoker, visible ? "Show" : "Hide", invoker.Name.FullName, null);
        ChatHelper.SendSystemMessage(invoker, visible
            ? "You are visible to other players again."
            : "You are hidden from other players while rigging the scene.");
        return true;
    }

    private static bool ShowStatus(Player invoker, string[] args)
    {
        if (args.Length != 0)
            return false;

        var model = invoker.RoleplayTemporaryAppearance == 0
            ? "normal character"
            : $"model {invoker.RoleplayTemporaryAppearance}";
        ChatHelper.SendSystemMessage(invoker,
            $"DM presentation: {model}; {(invoker.RoleplayHidden ? "hidden" : "visible")} to other players.");
        return true;
    }
}
