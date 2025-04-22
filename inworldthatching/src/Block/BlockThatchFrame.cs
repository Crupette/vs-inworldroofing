using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace InWorldThatching;

public class BlockThatchFrame : Block
{

    public CraftingRecipeIngredient Cost { get; private set; }
    public Dictionary<AssetLocation, AssetLocation> NextStage { get; private set; }
    public AssetLocation RecipeDisplay { get; private set; }
    public EnumFrameOrientableBehavior OrientableBehavior {get; private set; }

    WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Cost = Attributes?["cost"].AsObject<CraftingRecipeIngredient>();
        RecipeDisplay = AssetLocation.Create(Attributes?["recipeDisplay"]?.AsString(), InWorldThatchingSystem.MODID);
        
        var orientableBehavior = Attributes?["orientableBehavior"]?.AsString();
        switch(orientableBehavior) {
            case "horientable": {
                OrientableBehavior = EnumFrameOrientableBehavior.HORIENTABLE;
            } break;
            case "nworientable": {
                OrientableBehavior = EnumFrameOrientableBehavior.NWORIENTABLE;
            } break;
            case "none": {
                OrientableBehavior = EnumFrameOrientableBehavior.NONE;
            } break;
            default:
                throw new NullReferenceException($"Missing or invalid orientableBehavior attribute for frame block {Code}");
        }

        var rawNextStage = Attributes?["nextStage"].AsObject<Dictionary<string, string>>();
        NextStage = new();
        foreach(var stage in rawNextStage) {
            NextStage.Add(
                AssetLocation.Create(stage.Key, InWorldThatchingSystem.MODID),
                AssetLocation.Create(stage.Value, InWorldThatchingSystem.MODID));
        }

        if(api.Side != EnumAppSide.Client) return;

        interactions = ObjectCacheUtil.GetOrCreate(api, "inworldthatching.thatchFrameBlockInteractions", () => {
            List<ItemStack> materialStacks = new();
            foreach(var stage in NextStage) {
                materialStacks.Add(new(api.World.GetItem(stage.Key)));
            }

            return new WorldInteraction[] {
                new()
                {
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
        if(!NextStage.TryGetValue(heldStack.Collectible.Code, out var nextStageCode)) return false;

        Block nextStageBlock = world.BlockAccessor.GetBlock(nextStageCode);
        if(nextStageBlock == null) 
            throw new System.NullReferenceException($"Unable to get block from code {nextStageCode}. You might have made a typo in the nextStage attribute!");

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