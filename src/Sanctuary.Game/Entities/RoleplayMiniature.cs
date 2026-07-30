using Sanctuary.Game.Zones;
using Sanctuary.Packet;
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
        DisposeGracefully(
            animate: true,
            delay: 0,
            effectDelay: 0,
            compositeEffectId: 0,
            duration: 500);
    }
}
