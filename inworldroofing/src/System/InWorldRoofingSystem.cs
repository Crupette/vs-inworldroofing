using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace InWorldRoofing;

[ProtoContract]
public class RoofingFrameSelectMessage 
{
    [ProtoMember(1)]
    public int slotId;
    [ProtoMember(2)]
    public AssetLocation selection;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum EnumFrameOrientableBehavior
{
    None,
    NWOrientable,
    HOrientable,
}


public class InWorldRoofingSystem : ModSystem
{
    public const string MODID = "inworldroofing";
    public const string FRAME_SELECT_CHANNEL_NAME = MODID + ".thatchframeselect";

    public List<(RoofingStageIngredient, List<BlockRoofingStage>)> RoofingFrames;
    public static InWorldRoofingSystem Instance { get; private set; }

    public override void Start(ICoreAPI api)
    {
        Instance = this;
        api.RegisterBlockClass("InWorldRoofing.BlockRoofingStage", typeof(BlockRoofingStage));
        api.RegisterCollectibleBehaviorClass("InWorldRoofing.CollectibleBehaviorFrameMaterial", typeof(CollectibleBehaviorFrameMaterial));

        api.Network.RegisterChannel(FRAME_SELECT_CHANNEL_NAME)
                   .RegisterMessageType(typeof(RoofingFrameSelectMessage));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        RoofingFrames = new();
        foreach(var block in api.World.Blocks) {
            if(block is BlockRoofingStage stageBlock) {
                if(stageBlock.StageCost == null) continue;

                //Check for existing cost list.
                bool found = false;
                foreach(var framePair in RoofingFrames) {
                    RoofingStageIngredient frameIngredient = framePair.Item1;
                    if(frameIngredient.CollectibleEquals(stageBlock.StageCost)) {
                        framePair.Item2.Add(stageBlock);
                        found = true;
                        break;
                    }
                }
                if(!found) {
                    RoofingFrames.Add(new(stageBlock.StageCost, new() {stageBlock}));
                }
            }
        }
    }

    public List<BlockRoofingStage> FramesForStack(IWorldAccessor world, ItemStack stack) {
        foreach(var framePair in RoofingFrames) {
            RoofingStageIngredient ingredient = framePair.Item1;
            if(!ingredient.Resolve(world, "InWorldRoofingSystem.FramesForStack"))
                throw new System.Exception();
            if(ingredient.SatisfiesAsIngredient(stack, false)) return framePair.Item2;
        }
        return null;
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
            CollectibleBehaviorFrameMaterial.SetSelectedFrame(fromPlayer, slot, message.selection);
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

    public void SendSelectMessage(IClientPlayer player, ItemSlot slot, AssetLocation selection)
    {
        RoofingFrameSelectMessage message = new() {
            slotId = player.InventoryManager.GetHotbarInventory().GetSlotId(slot),
            selection = selection
        };
        clientThatchFrameChannel.SendPacket(message);
    }
    #endregion
}
