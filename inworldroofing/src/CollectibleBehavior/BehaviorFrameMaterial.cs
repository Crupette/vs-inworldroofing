using System;
using System.Collections.Generic;
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
    public AssetLocation[] FrameCodes;
    public InWorldRoofingSystem roofingSystem;

    public const string SELECTED_FRAME_KEY = "inworldroofing.selectedframe";

    public CollectibleBehaviorFrameMaterial(CollectibleObject collObj) : base(collObj)
    {
        
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        roofingSystem = api.ModLoader.GetModSystem<InWorldRoofingSystem>();

        FrameCodes = ObjectCacheUtil.GetOrCreate(api, "inworldroofing.FrameCodes", () => {
            return new[] {
                AssetLocation.Create("thatchframe-straight-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-bottom-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-cornerinner-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-cornerouter-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-tip-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-ridge-north", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-halfleft-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-halfright-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-ridgeend-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-ridgehalfleft-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-ridgehalfright-east", InWorldRoofingSystem.MODID),
                AssetLocation.Create("thatchframe-top-east", InWorldRoofingSystem.MODID),
            };
        });

        if(api is not ICoreClientAPI capi) return;
        ToolModes = ObjectCacheUtil.GetOrCreate(api, "InWorldRoofing.ThatchFrameMaterialToolModes", () => {
            List<SkillItem> modes = new() {
                new(){
                    Code = AssetLocation.Create("default", InWorldRoofingSystem.MODID),
                    Name = Lang.Get("toolmode-thatchframematerial-placeonground"),
                    TexturePremultipliedAlpha = false
                },
                new(){
                    Code = AssetLocation.Create("makeframe", InWorldRoofingSystem.MODID),
                    Name = Lang.Get("toolmode-thatchframematerial-placeframe"),
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

            int toolMode = GetToolMode(slot, (byEntity as EntityPlayer).Player, blockSel);
            if(toolMode == 0) return;

            handling = EnumHandling.PreventSubsequent;
            handHandling = EnumHandHandling.PreventDefault;
            
            int selectedId = GetSelectedFrame(slot);

            Block selectedBlock = world.BlockAccessor.GetBlock(FrameCodes[selectedId]);
            if(selectedBlock == null)
                throw new System.NullReferenceException($"Unable to find frame block with code {FrameCodes[selectedId]}");

            if(selectedBlock is not BlockThatchFrame selectedFrame)
                throw new System.NullReferenceException($"Cannot create frame block from non-frame type block {FrameCodes[selectedId]}");

            switch(selectedFrame.OrientableBehavior) {
                case EnumFrameOrientableBehavior.NONE: {
                    if(!PlaceBlockNotOrientable(world, slot, player, blockSel, selectedFrame)) {
                        return;
                    }
                } break;
                case EnumFrameOrientableBehavior.NWORIENTABLE: {
                    if(!PlaceBlockNWOrientable(world, slot, player, blockSel, selectedBlock)) {
                        return;
                    }
                } break;
                case EnumFrameOrientableBehavior.HORIENTABLE: {
                    if(!PlaceBlockHOrientable(world, slot, player, blockSel, selectedBlock)) {
                        return;
                    }
                } break;
            }
        }
    }

    public bool PlaceBlockNotOrientable(IWorldAccessor world, ItemSlot slot, IPlayer player, BlockSelection blockSel, BlockThatchFrame selectedFrame)
    {
        BlockSelection placeSel = blockSel.AddPosCopy(blockSel.Face.Normali);

        CraftingRecipeIngredient cost = selectedFrame.Cost;
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
                AssetLocation.Create("sounds/block/loosestick"), 
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

        if (orientedBlock is not BlockThatchFrame frameBlock)
            throw new System.NullReferenceException($"Unable to calculate cost for non-frame block {orientedCode}");

        CraftingRecipeIngredient cost = frameBlock.Cost;
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
                AssetLocation.Create("sounds/block/loosestick"), 
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

        if (orientedBlock is not BlockThatchFrame frameBlock)
            throw new System.NullReferenceException($"Unable to calculate cost for non-frame block {orientedCode}");

        CraftingRecipeIngredient cost = frameBlock.Cost;
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
                AssetLocation.Create("sounds/block/loosestick"), 
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
        return slot.Itemstack.Attributes.GetInt("toolMode");
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
        if(toolMode != 1) {
            if(toolMode == 0) {
                slot.Itemstack.Attributes.RemoveAttribute("toolMode");
            }
            slot.Itemstack.Attributes.RemoveAttribute(SELECTED_FRAME_KEY);
            return;
        }
        if (byPlayer == null || byPlayer.Entity.World.Side != EnumAppSide.Client) return;

        BlockPos selPos = blockSelection.Position;
        OpenDialog(byPlayer as IClientPlayer, selPos, slot);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        ICoreAPI api = inSlot.Inventory.Api;
        WorldInteraction[] interactions = new WorldInteraction[] {
            new() {
                ActionLangCode = "heldhelp-thatchframetoolmode",
                HotKeyCode = "toolmodeselect",
                MouseButton = EnumMouseButton.None
            }
        };
        int toolMode = GetToolMode(inSlot, null, null);
        if(toolMode == 0) {
            return interactions.Append(base.GetHeldInteractionHelp(inSlot, ref handling));
        }
        if(toolMode == 1) {
            handling = EnumHandling.PreventDefault;
            int selectedId = GetSelectedFrame(inSlot);
            if (api.World.GetBlock(FrameCodes[selectedId]) is not BlockThatchFrame blockFrame)
                throw new NullReferenceException($"Failed to retrieve frame block from code {FrameCodes[selectedId]}");

            ItemStack frameStack = new ItemStack(blockFrame);
            CraftingRecipeIngredient cost = blockFrame.Cost;
            cost.Resolve(api.World, "CollectibleBehaviorFrameMaterial.GetHeldInteractionHelp");

            return new WorldInteraction[] {
                new () {
                    ActionLangCode = Lang.Get("heldhelp-placethatchframe", blockFrame.GetHeldItemName(frameStack).ToLower()),
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new ItemStack[] { cost.ResolvedItemstack }
                }
            };
        }
        return base.GetHeldInteractionHelp(inSlot, ref handling);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if(GetToolMode(inSlot, null, null) != 0) {
            dsc.Append(Lang.Get("heldinfo-thatchframematerial-clearattributes"));
        }
    }

#region Client
    GuiDialog dialog;
    public void OpenDialog(IClientPlayer byPlayer, BlockPos pos, ItemSlot slot) {
        if(dialog != null && dialog.IsOpened()) return;
        IClientWorldAccessor world = byPlayer.Entity.World as IClientWorldAccessor;

        List<ItemStack> stacks = new();
        foreach(var code in FrameCodes) {
            BlockThatchFrame frameBlock = world.GetBlock(code) as BlockThatchFrame;
            Block displayBlock = world.GetBlock(frameBlock.RecipeDisplay);
            stacks.Add(new ItemStack(displayBlock));
        }

        ICoreClientAPI capi = world.Api as ICoreClientAPI;
        dialog = new GuiDialogBlockEntityRecipeSelector(
            Lang.Get("Select roof type"),
            stacks.ToArray(),
            (selectedIndex) => {
                SetSelectedFrame(slot, selectedIndex);
                roofingSystem.SendSelectMessage(byPlayer, slot, selectedIndex);
                slot.MarkDirty();
            },
            () => {
                SetToolMode(slot, null, null, 0);
                slot.MarkDirty();
            },
            pos,
            capi
        );
        dialog.OnClosed += dialog.Dispose;
        dialog.TryOpen();
    }
#endregion

    public static int GetSelectedFrame(ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack;
        return stack.Attributes.GetInt(SELECTED_FRAME_KEY, 0);
    }

    public static void SetSelectedFrame(ItemSlot slot, int value)
    {
        ItemStack stack = slot.Itemstack;
        stack.Attributes.SetInt(SELECTED_FRAME_KEY, value);
        slot.Itemstack.Attributes.SetInt("toolMode", 1);
    }
}