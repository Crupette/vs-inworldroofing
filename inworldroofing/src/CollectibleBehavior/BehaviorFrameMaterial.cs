using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing;

public class CollectibleBehaviorFrameMaterial : CollectibleBehavior
{
    public SkillItem[] ToolModes;

    public CollectibleBehaviorFrameMaterial(CollectibleObject collObj) : base(collObj)
    {
        
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if(api is not ICoreClientAPI capi) return;
        ToolModes = ObjectCacheUtil.GetOrCreate(api, "InWorldRoofing.FrameMaterialToolModes", () => {
            List<SkillItem> modes = new() {
                new(){
                    Code = AssetLocation.Create("default", InWorldRoofingSystem.MODID),
                    Name = Lang.Get("toolmode-inworldroofing-placeonground"),
                    TexturePremultipliedAlpha = false
                },
                new(){
                    Code = AssetLocation.Create("makeframe", InWorldRoofingSystem.MODID),
                    Name = Lang.Get("toolmode-inworldroofing-placeframe"),
                    TexturePremultipliedAlpha = false
                }
            };

            modes[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(
                AssetLocation.Create("textures/icons/cross.svg", InWorldRoofingSystem.MODID),
                48, 48, 5, ColorUtil.WhiteArgb));
            modes[1].WithIcon(capi, capi.Gui.LoadSvgWithPadding(
                AssetLocation.Create("textures/icons/frame.svg", InWorldRoofingSystem.MODID),
                48, 48, 5, ColorUtil.WhiteArgb));

            return modes.ToArray();
        });
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        IWorldAccessor world = byEntity?.World;
        if(byEntity == null || world == null || slot == null || slot.Empty) return;
        if(!firstEvent) return;

        if(byEntity.Controls.ShiftKey) {
            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if(player == null) return;

            int toolMode = GetToolMode(slot, player, blockSel);
            if(toolMode == 0) return;

            handling = EnumHandling.PreventSubsequent;
            handHandling = EnumHandHandling.PreventDefault;
            
            RoofingRecipe recipe = GetSelectedRecipe(player, slot);
            if(recipe == null) return;

            switch(recipe.OrientableBehavior) {
                case EnumOrientableBehavior.None: {
                    if(!PlaceBlockNotOrientable(world, slot, player, blockSel, recipe.FrameStage)) {
                        return;
                    }
                } break;
                case EnumOrientableBehavior.NWOrientable: {
                    if(!PlaceBlockNWOrientable(world, slot, player, blockSel, recipe.FrameStage)) {
                        return;
                    }
                } break;
                case EnumOrientableBehavior.HOrientable: {
                    if(!PlaceBlockHOrientable(world, slot, player, blockSel, recipe.FrameStage)) {
                        return;
                    }
                } break;
            }
        }
    }

    public bool PlaceBlockNotOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, RoofingRecipeStage recipe)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);

        CraftingRecipeIngredient ingredient = recipe.GetMatchingIngredient(slot.Itemstack);
        if(slot.Itemstack.StackSize < ingredient.Quantity) {
            (world.Api as ICoreClientAPI)?
                .TriggerIngameError(this, "notenough", 
                    Lang.Get("ingameerror-inworldroofing-nomaterial", 
                    slot.Itemstack.GetName().ToLower(), 
                    slot.Itemstack.StackSize, ingredient.Quantity));
            return false;
        }

        Block frameBlock = recipe.ResolvedResult;

        string err = "";
        if(frameBlock.TryPlaceBlock(world, player, new ItemStack(frameBlock), placeSel, ref err)) {
            slot.TakeOut(ingredient.Quantity);
        }else{
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, err, Lang.Get("placefailure-" + err));
            return false;
        }

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(
                frameBlock.Sounds.Place, 
                placeSel.Position,
                -0.5);
        }
        return true;
    }

    public bool PlaceBlockHOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, RoofingRecipeStage recipe)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);
        Block frameBlock = recipe.ResolvedResult;

        BlockFacing[] horVer = Block.SuggestedHVOrientation(player, blockSel);
        AssetLocation orientedCode = frameBlock.CodeWithVariant("orientation", horVer[0].Code);
        Block orientedBlock = world.BlockAccessor.GetBlock(orientedCode);

        if(orientedBlock == null)
            throw new System.NullReferenceException($"Unable to find rotated frame with code {orientedCode}");

        CraftingRecipeIngredient cost = recipe.GetMatchingIngredient(slot.Itemstack);
        if(slot.Itemstack.StackSize < cost.Quantity) {
            (world.Api as ICoreClientAPI)?
                .TriggerIngameError(this, "notenough", 
                    Lang.Get("ingameerror-inworldroofing-nomaterial", 
                    slot.Itemstack.GetName().ToLower(), 
                    slot.Itemstack.StackSize, cost.Quantity));
            return false;
        }

        string err = "";
        if(orientedBlock.TryPlaceBlock(world, player, new ItemStack(orientedBlock), placeSel, ref err)) {
            slot.TakeOut(cost.Quantity);
        }else{
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, err, Lang.Get("placefailure-" + err));
            return false;
        }

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(
                orientedBlock.Sounds.Place, 
                placeSel.Position,
                -0.5);
        }
        return true;
    }

    public bool PlaceBlockNWOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, RoofingRecipeStage recipe)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);
        Block frameBlock = recipe.ResolvedResult;

        BlockFacing[] horVer = Block.SuggestedHVOrientation(player, blockSel);
        string orientCode = "ns";
        if(horVer[0].Index == 1 || horVer[0].Index == 3) orientCode = "we";
        AssetLocation orientedCode = frameBlock.CodeWithVariant("orientation", orientCode);

        Block orientedBlock = world.BlockAccessor.GetBlock(orientedCode);

        if(orientedBlock == null)
            throw new System.NullReferenceException($"Unable to find rotated frame with code {orientedCode}");

        CraftingRecipeIngredient cost = recipe.GetMatchingIngredient(slot.Itemstack);
        if(slot.Itemstack.StackSize < cost.Quantity) {
            (world.Api as ICoreClientAPI)?
                .TriggerIngameError(this, "notenough", 
                    Lang.Get("ingameerror-inworldroofing-nomaterial", 
                    slot.Itemstack.GetName().ToLower(), 
                    slot.Itemstack.StackSize, cost.Quantity));
            return false;
        }

        string err = "";
        if(orientedBlock.TryPlaceBlock(world, player, new ItemStack(orientedBlock), placeSel, ref err)) {
            slot.TakeOut(cost.Quantity);
        }else{
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, err, Lang.Get("placefailure-" + err));
            return false;
        }

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(
                frameBlock.Sounds.Place,
                placeSel.Position,
                -0.5);
        }
        return true;
    }

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if(blockSel == null) return null;
        return ToolModes;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if(slot == null || byPlayer == null || blockSelection == null) return 0;
        if(slot.Itemstack?.Attributes?["toolMode"] != null) {
            slot.Itemstack.Attributes.RemoveAttribute("toolMode");
            slot.Itemstack.Attributes.RemoveAttribute("inworldthatching.selectedframe");
        }
        return byPlayer.Entity?.Attributes[PLAYER_FRAMEMATERIAL_KEY]?.ToString() == slot.Itemstack?.Collectible.Code.ToString() ? 1 : 0;
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if(slot == null || byPlayer == null || blockSelection == null) return;
        if(toolMode != 1) {
            SetSelectedRecipe(byPlayer, slot, -1);
            return;
        };
        if (byPlayer == null || byPlayer.Entity.World.Side != EnumAppSide.Client) return;

        BlockPos selPos = blockSelection.Position;
        OpenDialog(byPlayer as IClientPlayer, selPos, slot);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        if(inSlot.Inventory.Api is not ICoreClientAPI api) return base.GetHeldInteractionHelp(inSlot, ref handling);
        IPlayer player = api.World.Player;

        WorldInteraction[] interactions = new WorldInteraction[] {
            new() {
                ActionLangCode = "heldhelp-inworldroofing-toolmode",
                HotKeyCode = "toolmodeselect",
                MouseButton = EnumMouseButton.None
            }
        };
        int toolMode = GetToolMode(inSlot, api.World.Player, null);
        if(toolMode == 0) {
            return interactions.Append(base.GetHeldInteractionHelp(inSlot, ref handling));
        }
        if(toolMode == 1) {
            handling = EnumHandling.PreventDefault;
            RoofingRecipe selectedRecipe = GetSelectedRecipe(player, inSlot);
            Block blockFrame = selectedRecipe.ResolvedName;
            if(blockFrame == null) return base.GetHeldInteractionHelp(inSlot, ref handling);

            ItemStack frameStack = new ItemStack(blockFrame);
            RoofingRecipeStage frameStage = selectedRecipe.FrameStage;
            if(frameStage == null) return base.GetHeldInteractionHelp(inSlot, ref handling);

            return new WorldInteraction[] {
                new () {
                    ActionLangCode = Lang.Get("heldhelp-inworldroofing-placeframe", blockFrame.GetHeldItemName(frameStack).ToLower()),
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = frameStage.IngredientStacks
                }
            };
        }
        return base.GetHeldInteractionHelp(inSlot, ref handling);
    }

#region Client
    GuiDialog dialog;
    public void OpenDialog(IClientPlayer byPlayer, BlockPos pos, ItemSlot slot) {
        if(dialog != null && dialog.IsOpened()) return;
        IClientWorldAccessor world = byPlayer.Entity.World as IClientWorldAccessor;
        ICoreAPI api = world.Api;

        List<ItemStack> stacks = new();
        RoofingRecipe[] matchingRecipes = 
            ApiAdditions.RoofingRecipesForFrameOrientations(api, slot.Itemstack, "east", "ns", "none");

        List<RoofingRecipe> displayRecipes = new();
        List<Block> displayBlocks = new();
        foreach(var recipe in matchingRecipes) {
            Block displayBlock = world.GetBlock(recipe.Name);
            if(displayBlocks.Contains(displayBlock)) continue;
            displayRecipes.Add(recipe);
            displayBlocks.Add(displayBlock);
            stacks.Add(new ItemStack(displayBlock));
        }

        ICoreClientAPI capi = world.Api as ICoreClientAPI;
        dialog = new GuiDialogBlockEntityRecipeSelector(
            Lang.Get("Select roof type"),
            stacks.ToArray(),
            (selectedIndex) => {
                int selectedRecipe = displayRecipes[selectedIndex].RecipeId;
                SetSelectedRecipe(byPlayer, slot, selectedRecipe);
                ApiAdditions.RoofingSystem(api).SendSelectMessage(byPlayer, slot, selectedRecipe);
                slot.MarkDirty();
            },
            () => {
                SetSelectedRecipe(byPlayer, slot, -1);
                ApiAdditions.RoofingSystem(api).SendSelectMessage(byPlayer, slot, -1);
                slot.MarkDirty();
            },
            pos,
            capi
        );
        dialog.OnClosed += dialog.Dispose;
        dialog.TryOpen();
    }
#endregion

    public static RoofingRecipe GetSelectedRecipe(IPlayer player, ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack;
        if(player.Entity.Attributes[PLAYER_FRAMEMATERIAL_KEY] == null) return null;
        ICoreAPI api = player?.Entity.Api;
        if(stack.Collectible.Code != new AssetLocation(player.Entity.Attributes[PLAYER_FRAMEMATERIAL_KEY].ToString()))
            return null;
        
        int recipeId = player?.Entity.Attributes?[PLAYER_SELECTEDRECIPE_KEY].GetValue() as int? ?? -1;
        return ApiAdditions.RoofingRecipes(api).FirstOrDefault(r => r.RecipeId == recipeId);
    }

    public static readonly string PLAYER_FRAMEMATERIAL_KEY = "inworldroofing.currentFrameMaterial";
    public static readonly string PLAYER_SELECTEDRECIPE_KEY = "inworldroofing.currentRecipe";

    public static void SetSelectedRecipe(IPlayer player, ItemSlot slot, int value)
    {
        if(value < 0) {
            player.Entity.Attributes.RemoveAttribute(PLAYER_FRAMEMATERIAL_KEY);
            player.Entity.Attributes.RemoveAttribute(PLAYER_SELECTEDRECIPE_KEY);
            return;
        }
        player.Entity.Attributes.SetString(PLAYER_FRAMEMATERIAL_KEY, slot.Itemstack.Collectible.Code);
        player.Entity.Attributes.SetInt(PLAYER_SELECTEDRECIPE_KEY, value);
    }
}