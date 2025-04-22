using System.Reflection.Metadata;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace InWorldThatching;

[ProtoContract]
public class ThatchFrameSelectMessage 
{
    [ProtoMember(1)]
    public int slotId;
    [ProtoMember(2)]
    public int selection;
}

public enum EnumFrameOrientableBehavior
{
    NONE,
    NWORIENTABLE,
    HORIENTABLE,
}

public class InWorldThatchingSystem : ModSystem
{
    public const string MODID = "inworldthatching";
    public const string FRAME_SELECT_CHANNEL_NAME = MODID + ".thatchframeselect";

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("InWorldThatching.BlockThatchFrame", typeof(BlockThatchFrame));
        api.RegisterBlockClass("InWorldThatching.BlockThatchWork", typeof(BlockThatchWork));
        api.RegisterCollectibleBehaviorClass("InWorldThatching.CollectibleBehaviorFrameMaterial", typeof(CollectibleBehaviorFrameMaterial));

        api.Network.RegisterChannel(FRAME_SELECT_CHANNEL_NAME)
                   .RegisterMessageType(typeof(ThatchFrameSelectMessage));
    }
    #region Server
    public ICoreServerAPI sapi;
    public IServerNetworkChannel serverThatchFrameChannel;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        serverThatchFrameChannel = 
            api.Network.GetChannel(FRAME_SELECT_CHANNEL_NAME)
                       .SetMessageHandler<ThatchFrameSelectMessage>((fromPlayer, message) => 
        {
            ItemSlot slot = fromPlayer.InventoryManager.GetHotbarInventory()[message.slotId];
            CollectibleBehaviorFrameMaterial.SetSelectedFrame(slot, message.selection);
            slot.MarkDirty();
        });
                        
    }
    #endregion

    #region Client

    IClientNetworkChannel clientThatchFrameChannel;

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientThatchFrameChannel =
            api.Network.GetChannel(FRAME_SELECT_CHANNEL_NAME);
    }

    public void SendSelectMessage(IClientPlayer player, ItemSlot slot, int selection)
    {
        ThatchFrameSelectMessage message = new() {
            slotId = player.InventoryManager.GetHotbarInventory().GetSlotId(slot),
            selection = selection
        };
        clientThatchFrameChannel.SendPacket(message);
    }
    #endregion
}
