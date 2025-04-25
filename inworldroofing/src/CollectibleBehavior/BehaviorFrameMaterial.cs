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
                    Name = Lang.Get("toolmode-framematerial-placeonground"),
                    TexturePremultipliedAlpha = false
                },
                new(){
                    Code = AssetLocation.Create("makeframe", InWorldRoofingSystem.MODID),
                    Name = Lang.Get("toolmode-framematerial-placeframe"),
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
        if(byEntity == null || world == null) return;

        if(blockSel == null) return;

        if(byEntity.Controls.ShiftKey) {
            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if(player == null) return;

            int toolMode = GetToolMode(slot, player, blockSel);
            if(toolMode == 0) return;

            handling = EnumHandling.PreventSubsequent;
            handHandling = EnumHandHandling.PreventDefault;
            
            BlockRoofingStage selectedFrame = world.GetBlock(GetSelectedFrame(player, slot)) as BlockRoofingStage;

            switch(selectedFrame.OrientableBehavior) {
                case EnumFrameOrientableBehavior.None: {
                    if(!PlaceBlockNotOrientable(world, slot, player, blockSel, selectedFrame)) {
                        return;
                    }
                } break;
                case EnumFrameOrientableBehavior.NWOrientable: {
                    if(!PlaceBlockNWOrientable(world, slot, player, blockSel, selectedFrame)) {
                        return;
                    }
                } break;
                case EnumFrameOrientableBehavior.HOrientable: {
                    if(!PlaceBlockHOrientable(world, slot, player, blockSel, selectedFrame)) {
                        return;
                    }
                } break;
            }
        }
    }

    public bool PlaceBlockNotOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, BlockRoofingStage selectedFrame)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);

        RoofingStageIngredient cost = selectedFrame.StageCost;
        if(slot.Itemstack.StackSize < cost.Quantity) {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, "notenough", Lang.Get("ingameerror-thatchframematerial-notenoughsticks", slot.Itemstack.StackSize, cost.Quantity));
            return false;
        }

        string err = "";
        if(selectedFrame.TryPlaceBlock(world, player, new ItemStack(selectedFrame), placeSel, ref err)) {
            slot.TakeOut(cost.Quantity);
        }else{
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, err, Lang.Get("placefailure-" + err));
            return false;
        }

        if(world.Side == EnumAppSide.Server) {
            world.PlaySoundAt(
                selectedFrame.Sounds.Place, 
                placeSel.Position,
                -0.5);
        }
        return true;
    }

    public bool PlaceBlockHOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, Block selectedBlock)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);

        BlockFacing[] horVer = Block.SuggestedHVOrientation(player, blockSel);
        AssetLocation orientedCode = selectedBlock.CodeWithVariant("horizontalorientation", horVer[0].Code);
        Block orientedBlock = world.BlockAccessor.GetBlock(orientedCode);

        if(orientedBlock == null)
            throw new System.NullReferenceException($"Unable to find rotated frame with code {orientedCode}");

        if (orientedBlock is not BlockRoofingStage frameBlock)
            throw new System.NullReferenceException($"Unable to calculate cost for non-frame block {orientedCode}");

        RoofingStageIngredient cost = frameBlock.StageCost;
        if(slot.Itemstack.StackSize < cost.Quantity) {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, "notenough", Lang.Get("Not enough sticks! Have {0}, need {1}", slot.Itemstack.StackSize, cost.Quantity));
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

    public bool PlaceBlockNWOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, Block selectedBlock)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);

        BlockFacing[] horVer = Block.SuggestedHVOrientation(player, blockSel);
        string orientCode = "north";
        if(horVer[0].Index == 1 || horVer[0].Index == 3) orientCode = "east";
        AssetLocation orientedCode = selectedBlock.CodeWithVariant("horizontalorientation", orientCode);

        Block orientedBlock = world.BlockAccessor.GetBlock(orientedCode);

        if(orientedBlock == null)
            throw new System.NullReferenceException($"Unable to find rotated frame with code {orientedCode}");

        if (orientedBlock is not BlockRoofingStage frameBlock)
            throw new System.NullReferenceException($"Unable to calculate cost for non-frame block {orientedCode}");

        RoofingStageIngredient cost = frameBlock.StageCost;
        if(slot.Itemstack.StackSize < cost.Quantity) {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, "notenough", Lang.Get("Not enough sticks! Have {0}, need {1}", slot.Itemstack.StackSize, cost.Quantity));
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
        if(slot.Itemstack.Attributes["toolMode"] != null) {
            slot.Itemstack.Attributes.RemoveAttribute("toolMode");
            slot.Itemstack.Attributes.RemoveAttribute("inworldthatching.selectedframe");
        }
        return byPlayer.Entity.Attributes[PLAYER_FRAMEMATERIAL_KEY]?.ToString() == slot.Itemstack.Collectible.Code.ToString() ? 1 : 0;
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if(toolMode != 1) {
            SetSelectedFrame(byPlayer, slot, null);
            return;
        };
        if (byPlayer == null || byPlayer.Entity.World.Side != EnumAppSide.Client) return;

        BlockPos selPos = blockSelection.Position;
        OpenDialog(byPlayer as IClientPlayer, selPos, slot);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        ICoreClientAPI api = inSlot.Inventory.Api as ICoreClientAPI;
        WorldInteraction[] interactions = new WorldInteraction[] {
            new() {
                ActionLangCode = "heldhelp-framematerial-toolmode",
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
            AssetLocation selectedFrame = GetSelectedFrame(api.World.Player, inSlot);
            BlockRoofingStage blockFrame = api.World.GetBlock(selectedFrame) as BlockRoofingStage;
            if(blockFrame == null) return base.GetHeldInteractionHelp(inSlot, ref handling);;

            ItemStack frameStack = new ItemStack(blockFrame);
            RoofingStageIngredient cost = blockFrame.StageCost;
            cost.Resolve(api.World, "CollectibleBehaviorFrameMaterial.GetHeldInteractionHelp");

            return new WorldInteraction[] {
                new () {
                    ActionLangCode = Lang.Get("heldhelp-placeframe", blockFrame.GetHeldItemName(frameStack).ToLower()),
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new ItemStack[] { cost.ResolvedItemstack }
                }
            };
        }
        return base.GetHeldInteractionHelp(inSlot, ref handling);
    }

#region Client
    GuiDialog dialog;
    public void OpenDialog(IClientPlayer byPlayer, BlockPos pos, ItemSlot slot) {
        if(dialog != null && dialog.IsOpened()) return;
        InWorldRoofingSystem roofingSystem = InWorldRoofingSystem.Instance;
        IClientWorldAccessor world = byPlayer.Entity.World as IClientWorldAccessor;

        List<ItemStack> stacks = new();
        List<BlockRoofingStage> MatchingFrames = roofingSystem.FramesForStack(world, slot.Itemstack);
        List<BlockRoofingStage> AvailableFrames = new();
        foreach(var frameBlock in MatchingFrames) {
            if(frameBlock.RecipeDisplay == null) continue;
            Block displayBlock = world.GetBlock(frameBlock.RecipeDisplay);
            stacks.Add(new ItemStack(displayBlock));
            AvailableFrames.Add(frameBlock);
        }

        ICoreClientAPI capi = world.Api as ICoreClientAPI;
        dialog = new GuiDialogBlockEntityRecipeSelector(
            Lang.Get("Select roof type"),
            stacks.ToArray(),
            (selectedIndex) => {
                AssetLocation selectedFrame = AvailableFrames[selectedIndex].Code;
                SetSelectedFrame(byPlayer, slot, selectedFrame);
                roofingSystem.SendSelectMessage(byPlayer, slot, selectedFrame);
                slot.MarkDirty();
            },
            () => {
                SetSelectedFrame(byPlayer, slot, null);
                roofingSystem.SendSelectMessage(byPlayer, slot, null);
                slot.MarkDirty();
            },
            pos,
            capi
        );
        dialog.OnClosed += dialog.Dispose;
        dialog.TryOpen();
    }
#endregion

    public static AssetLocation GetSelectedFrame(IPlayer player, ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack;
        if(player.Entity.Attributes[PLAYER_FRAMEMATERIAL_KEY] == null) return null;
        if(stack.Collectible.Code != new AssetLocation(player.Entity.Attributes[PLAYER_FRAMEMATERIAL_KEY].ToString()))
            return null;
        return new AssetLocation(player.Entity.Attributes[PLAYER_SELECTEDFRAME_KEY].ToString());
    }

    public static readonly string PLAYER_FRAMEMATERIAL_KEY = "inworldroofing.currentFrameMaterial";
    public static readonly string PLAYER_SELECTEDFRAME_KEY = "inworldroofing.currentFrame";

    public static void SetSelectedFrame(IPlayer player, ItemSlot slot, AssetLocation value)
    {
        if(value == null) {
            player.Entity.Attributes.RemoveAttribute(PLAYER_FRAMEMATERIAL_KEY);
            player.Entity.Attributes.RemoveAttribute(PLAYER_SELECTEDFRAME_KEY);
            return;
        }
        player.Entity.Attributes.SetString(PLAYER_FRAMEMATERIAL_KEY, slot.Itemstack.Collectible.Code);
        player.Entity.Attributes.SetString(PLAYER_SELECTEDFRAME_KEY, value);
    }
}