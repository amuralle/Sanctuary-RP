using Sanctuary.Game.Zones;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Game.Entities;

public sealed class RoleplayMiniature : Npc
{
    public int InstanceId { get; }
    public ulong OwnerGuid { get; }
    public string OwnerName { get; }
    public string Group { get; }

    public RoleplayMiniature(IZone zone, int instanceId, ulong ownerGuid, string ownerName, string group)
        : base(zone)
    {
        InstanceId = instanceId;
        OwnerGuid = ownerGuid;
        OwnerName = ownerName;
        Group = group;
    }

    public override void OnInteract(Player player)
    {
        player.SendTunneled(new PacketChat
        {
            Channel = ChatChannel.System,
            Message = $"Miniature #{InstanceId}: {Name} | model {ModelId} | @{Group} | placed by {OwnerName}"
        });
    }

    internal void DisposeFromScene()
    {
        EntityHelper.RemovePlayerGracefully(this, animate: true, duration: 500);
    }

    internal void Speak(string message)
    {
        var packet = new PacketChat
        {
            Channel = ChatChannel.WorldSay,
            FromGuid = Guid,
            FromName = new NameData { FirstName = Name ?? $"Miniature #{InstanceId}" },
            Message = message
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(packet);
    }

    internal void Emote(int animationId)
    {
        var packet = new PlayerUpdatePacketSetAnimation
        {
            Guid = Guid,
            AnimationId = animationId
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(packet);
    }
}
