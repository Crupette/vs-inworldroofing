using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing;

[ProtoContract]
public class RoofingFrameSelectMessage 
{
    [ProtoMember(1)]
    public int slotId;
    [ProtoMember(2)]
    public int selection;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum EnumOrientableBehavior
{
    None,
    NWOrientable,
    HOrientable,
}

public class InWorldRoofingSystem : ModSystem
{
    public const string MODID = "inworldroofing";
    public const string FRAME_SELECT_CHANNEL_NAME = MODID + ".thatchframeselect";
    public Harmony harmony;

    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass("InWorldRoofing.CollectibleBehaviorFrameMaterial", 
            typeof(CollectibleBehaviorFrameMaterial));
        api.RegisterBlockBehaviorClass("InWorldRoofing.BlockBehaviorRoofingStage", 
            typeof(BlockBehaviorRoofingStage));

        api.Network.RegisterChannel(FRAME_SELECT_CHANNEL_NAME)
                   .RegisterMessageType(typeof(RoofingFrameSelectMessage));

        if(!Harmony.HasAnyPatches(MODID)) {
            harmony = new Harmony(MODID);
            harmony.PatchAll();
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(MODID);
    }

    #region Server
    public ICoreServerAPI sapi;
    public IServerNetworkChannel serverThatchFrameChannel;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        serverThatchFrameChannel = 
            api.Network.GetChannel(FRAME_SELECT_CHANNEL_NAME)
                       .SetMessageHandler<RoofingFrameSelectMessage>((fromPlayer, message) => 
        {
            ItemSlot slot = fromPlayer.InventoryManager.GetHotbarInventory()[message.slotId];
            CollectibleBehaviorFrameMaterial.SetSelectedRecipe(fromPlayer, slot, message.selection);
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
        RoofingFrameSelectMessage message = new() {
            slotId = player.InventoryManager.GetHotbarInventory().GetSlotId(slot),
            selection = selection
        };
        clientThatchFrameChannel.SendPacket(message);
    }
    #endregion
}
