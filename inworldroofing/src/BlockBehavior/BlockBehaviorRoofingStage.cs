using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing;

public class BlockBehaviorRoofingStage : BlockBehavior
{
    public int[] RecipeIds;

    public int StageId;

    public BlockBehaviorRoofingStage(Block block) : base(block)
    {
        
    }

    public override void Initialize(JsonObject properties)
    {
        RecipeIds = properties["recipes"].AsArray<int>();
        StageId = properties["stage"].AsInt();

        base.Initialize(properties);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        ItemSlot heldSlot = byPlayer?.InventoryManager.ActiveHotbarSlot;
        if(heldSlot == null || heldSlot.Empty) return false;

        ICoreAPI api = world?.Api;
        if(world == null || api == null) return false;

        if(!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "claimed", Lang.Get("placefailure-claimed"));
            heldSlot.MarkDirty();
            return false;
        }

        ItemStack heldStack = heldSlot.Itemstack;
        RoofingRecipeStage stage = BestRecipeForStack(api, heldStack);
        if(stage == null) return false;

        CraftingRecipeIngredient ingredient = stage.GetMatchingIngredient(heldStack);

        handling = EnumHandling.PreventSubsequent;
        if(ingredient.Quantity > heldStack.StackSize) {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "notenough",
                Lang.Get("placefailure-inworldroofing-notenough", 
                    heldStack.GetName().ToLower(), heldStack.StackSize, ingredient.Quantity));
            return false;
        }

        heldSlot.TakeOut(ingredient.Quantity);
        world.BlockAccessor.SetBlock(stage.ResolvedResult.Id, blockSel.Position);

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(stage.ResolvedResult.Sounds.Place, blockSel.Position, 0.5);
        }

        return true;
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref float dropChanceMultiplier, ref EnumHandling handling)
    {
        List<ItemStack> drops = new();
        ICoreAPI api = world.Api;
        RoofingRecipe firstRecipe = ApiAdditions.RoofingRecipes(api)[RecipeIds[0]];
        for(int i = 0; i < StageId; i++) {
            RoofingRecipeStage stage = firstRecipe.Stages[i];
            CraftingRecipeIngredient firstIngredient = stage.Ingredient[0];

            drops.Add(firstIngredient.ResolvedItemstack);
        }
        handling = EnumHandling.PreventDefault;
        return drops.ToArray();
    }

    public RoofingRecipeStage BestRecipeForStack(ICoreAPI api, ItemStack stack) {
        
        foreach(var id in RecipeIds) {
            RoofingRecipe recipe = ApiAdditions.RoofingRecipes(api)[id];
            RoofingRecipeStage stage = recipe.Stages[StageId];
            if(stage.MatchesIngredient(stack)) return stage;
        }
        return null;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
    {
        WorldInteraction[] baseInteractions = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);
        ICoreAPI api = world.Api;
        return baseInteractions.Append(ObjectCacheUtil.GetOrCreate<WorldInteraction[]>(world.Api, $"InWorldRoofing.RoofingStage-{block.Code}-Help", () => {
            RoofingRecipeStage[] stages = GetStages(api);
            List<ItemStack> stacks = new();

            foreach(var stage in stages) {
                stage.IngredientStacks.Foreach(stack => stacks.Add(stack));
            }

            return new WorldInteraction[] {
                new() {
                    ActionLangCode = "blockhelp-roofingstage-addmaterial",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = stacks.ToArray(),
                    GetMatchingStacks = (wi, bs, es) => stacks.ToArray()
                }
            };
        }));
    }

    public RoofingRecipe[] GetRecipes(ICoreAPI api) {
        List<RoofingRecipe> recipes = new();
        ApiAdditions.RoofingRecipes(api).ForEach((recipe) => {
            if(RecipeIds.Contains(recipe.RecipeId)) recipes.Add(recipe);
        });
        return recipes.ToArray();
    }

    public RoofingRecipeStage[] GetStages(ICoreAPI api) {
        List<RoofingRecipeStage> stages = new();
        GetRecipes(api).Foreach(stage => stages.Add(stage.Stages[StageId]));
        return stages.ToArray();
    }
}