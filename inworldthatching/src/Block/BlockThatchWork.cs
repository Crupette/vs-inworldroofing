using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace InWorldThatching;

public class BlockThatchWork : Block
{

    public AssetLocation[] UpgradeMaterial { get; private set; }
    public AssetLocation NextStage { get; private set; }

    WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        NextStage = AssetLocation.Create(Attributes?["nextStage"].AsString(), InWorldThatchingSystem.MODID);

        var upgradeMaterialsRaw = Attributes?["upgradeMaterial"].AsArray();
        List<AssetLocation> upgradeMaterials = new();
        foreach(var material in upgradeMaterialsRaw) {
            upgradeMaterials.Add(AssetLocation.Create(material.AsString(), InWorldThatchingSystem.MODID));
        }
        UpgradeMaterial = upgradeMaterials.ToArray();

        if(api.Side != EnumAppSide.Client) return;

        interactions = ObjectCacheUtil.GetOrCreate(api, $"inworldthatching.thatchWork-{Code.Path.Split('-')[2]}-BlockInteractions", () => {
            List<ItemStack> materialStacks = new();
            foreach(var material in upgradeMaterials) {
                materialStacks.Add(new ItemStack(api.World.GetItem(material)));
            }

            return new WorldInteraction[] {
                new() {
                    ActionLangCode = "blockhelp-thatchframe-addmaterial",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = materialStacks.ToArray(),
                    GetMatchingStacks = (wi, bs, es) => materialStacks.ToArray()
                }
            };
        });

    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if(heldSlot == null || heldSlot.Empty) return false;

        if(!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) {
            heldSlot.MarkDirty();
            return false;
        }

        ItemStack heldStack = heldSlot.Itemstack;
        if(!UpgradeMaterial.Contains(heldStack.Collectible.Code)) return false;

        Block nextStageBlock = world.BlockAccessor.GetBlock(NextStage);
        if(nextStageBlock == null) 
            throw new System.NullReferenceException($"Unable to get block from code {NextStage}. You might have made a typo in the nextStage attribute!");

        heldSlot.TakeOut(1);
        world.BlockAccessor.SetBlock(nextStageBlock.Id, blockSel.Position);

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(
                AssetLocation.Create("sounds/block/plant"), 
                blockSel.Position,
                -0.5);
        }

        return true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}