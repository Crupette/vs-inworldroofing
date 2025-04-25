using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace InWorldRoofing;

public class BlockRoofingStage : Block
{
    public readonly string COST_KEY = "cost";
    public readonly string NEXTSTAGECOST_KEY = "nextStageCost";
    public readonly string RECIPEDISPLAY_KEY = "recipeDisplay";
    public readonly string ORIENTABLEBEHAVIOR_KEY = "orientableBehavior";

    public RoofingStageIngredient StageCost { get; private set; }
    public Dictionary<string, List<RoofingStageIngredient>> NextStageCost { get; private set; }
    public AssetLocation RecipeDisplay { get; private set; }
    public EnumFrameOrientableBehavior? OrientableBehavior {get; private set; }

    WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        
        if(Enum.TryParse<EnumFrameOrientableBehavior>(Attributes?[ORIENTABLEBEHAVIOR_KEY].ToString(), true, out var orientableBehavior)) {
            OrientableBehavior = orientableBehavior;
        }

        StageCost = Attributes?[COST_KEY]?.AsObject<RoofingStageIngredient>();
        if(Attributes?[NEXTSTAGECOST_KEY].Exists ?? false) {
            NextStageCost = new();
            foreach(var pair in Attributes[NEXTSTAGECOST_KEY].AsObject<Dictionary<string, List<JToken>>>()) {
                AssetLocation key = FillPlaceholders(AssetLocation.Create(pair.Key, InWorldRoofingSystem.MODID));
                List<JToken> values = pair.Value;

                foreach(JToken value in values) {
                    LoadStageRecipe(api.World, key, value);
                }
            }
        }

        if(Attributes?[RECIPEDISPLAY_KEY].Exists ?? false)
            RecipeDisplay = AssetLocation.Create(Attributes?[RECIPEDISPLAY_KEY]?.AsString(), InWorldRoofingSystem.MODID);

        if(api.Side != EnumAppSide.Client) return;

        interactions = ObjectCacheUtil.GetOrCreate(api, $"inworldroofing.roofingStage-{Code.Path}-BlockInteractions", () => {
            List<ItemStack> materialStacks = new();
            foreach(var stageCostList in NextStageCost.Values) {
                foreach(var stageCost in stageCostList) {
                    materialStacks.Add(stageCost.ResolvedItemstack);
                }
            }

            return new WorldInteraction[] {
                new() {
                    ActionLangCode = "blockhelp-roofingStage-addmaterial",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = materialStacks.ToArray(),
                    GetMatchingStacks = (wi, bs, es) => materialStacks.ToArray()
                }
            };
        });

    }

    void LoadStageRecipe(IWorldAccessor world, AssetLocation stageCode, JToken costObject)
    {
        List<string> nameMappings = new();
        AssetLocation costCode = AssetLocation.Create(costObject["code"].ToString(), InWorldRoofingSystem.MODID);
        RoofingStageIngredient ingredient = costObject.ToObject<RoofingStageIngredient>();
        ingredient.Code = costCode;

        //Realize mappings.
        if(ingredient.Name == null || ingredient.Name.Length == 0) {
            AddStageRecipe(stageCode, ingredient);
            return;
        }

        int wildcardIndex = costCode.Path.IndexOf("*");
        int wildcardLen = costCode.Path.Length - wildcardIndex - 1;
        if(wildcardIndex == -1) {
            AddStageRecipe(stageCode, ingredient);
            return;
        }

        if(ingredient.Type == EnumItemClass.Block) {
            foreach(Block block in world.Blocks) {
                if(!block.IsMissing && WildcardUtil.Match(costCode, block.Code, ingredient.AllowedVariants)) {
                    string code = block.Code.Path.Substring(wildcardIndex);
                    string blockVariant = code.Substring(0, code.Length - wildcardLen).DeDuplicate();
                    nameMappings.Add(blockVariant);
                }
            }
        }else
        if(ingredient.Type == EnumItemClass.Item) {
            foreach(Item item in world.Items) {
                if(item.Code != null && !item.IsMissing && WildcardUtil.Match(costCode, item.Code, ingredient.AllowedVariants)) {
                    string code = item.Code.Path.Substring(wildcardIndex);
                    string itemVariant = code.Substring(0, code.Length - wildcardLen).DeDuplicate();
                    nameMappings.Add(itemVariant);
                }
            }
        }

        foreach(var mapping in nameMappings) {
            AssetLocation newStageCode = stageCode.Clone();
            newStageCode.Path = stageCode.Path.Replace("{" + ingredient.Name + "}", mapping);
            RoofingStageIngredient newIngredient = ingredient.Clone();
            newIngredient.Code.Path = ingredient.Code.Path.Replace("*", mapping);
            AddStageRecipe(newStageCode, newIngredient);
        }
    }

    void AddStageRecipe(AssetLocation forStage, RoofingStageIngredient ingredient)
    {
        //api.World.Logger.Debug($"New stage recipe in {Code} for {forStage} | {ingredient.Code}x{ingredient.Quantity}");
        ingredient.Resolve(api.World, $"{Code}.AddStageRecipe");
        if(!NextStageCost.TryGetValue(forStage.ToString(), out var ingredientsList)) {
                ingredientsList = new();
                NextStageCost[forStage] = ingredientsList;
            }
        ingredientsList.Add(ingredient);
    }

    public AssetLocation FillPlaceholders(AssetLocation asset)
    {
        AssetLocation result = asset.Clone();
        foreach(var variant in Variant) {
            result.Path = result.Path.Replace("{" + variant.Key + "}", variant.Value);
        }
        return result;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if(heldSlot == null || heldSlot.Empty) return false;

        if(!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "claimed", Lang.Get("placefailure-claimed"));
            heldSlot.MarkDirty();
            return false;
        }

        ItemStack heldStack = heldSlot.Itemstack;
        RoofingStageIngredient cost = null;
        Block nextStageBlock = null;

        foreach(var ingredientPair in NextStageCost) {
            List<RoofingStageIngredient> ingredients = ingredientPair.Value;
            foreach(var ingredient in ingredients) {
                if(!ingredient.Resolve(world, $"{Code}"))
                    throw new System.Exception($"Failed to resolve CraftingRecipe itemstack for {Code} : {ingredient.Code}");
            
                if(ingredient.SatisfiesAsIngredient(heldStack, false)) {
                    cost = ingredient;
                    AssetLocation nextStageCode = ingredientPair.Key;
                    nextStageBlock = world.BlockAccessor.GetBlock(nextStageCode);
                    if(nextStageBlock == null)
                        throw new System.Exception($"Failed to resolve block with code {nextStageCode}");
                    break;
                }
            }
        }

        if(cost == null) return false;

        if(!cost.SatisfiesAsIngredient(heldStack)) {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "nomaterial", 
                Lang.Get("ingameerror-inworldroofing-nomaterial", heldStack.GetName().ToLower(), heldStack.StackSize, cost.ResolvedItemstack.StackSize));
            return false;
        }
        heldSlot.TakeOut(cost.ResolvedItemstack.StackSize);

        world.BlockAccessor.SetBlock(nextStageBlock.Id, blockSel.Position);

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(
                nextStageBlock.Sounds.Place,
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