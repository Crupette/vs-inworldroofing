using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing.Patches;

[HarmonyPatch(
    typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
public class CollectibleBehaviorHandbookTextAndExtraInfo_addCreatedByInfo_Patch
{
    const int TinyPadding = 2;
    const int TinyIndent = 2;

    static void AddSubHeading(
        CollectibleBehaviorHandbookTextAndExtraInfo instance, 
        List<RichTextComponentBase> components,
        ICoreClientAPI capi,
        ActionConsumable<string> openDetailPageFor,
        string subheading,
        string detailpage)
    {
        AccessTools.Method(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "AddSubHeading")
            .Invoke(instance, new object[] {components, capi, openDetailPageFor, subheading, detailpage});
    }

    static void AddHeading(
        CollectibleBehaviorHandbookTextAndExtraInfo instance, 
        List<RichTextComponentBase> components,
        ICoreClientAPI capi,
        string heading,
        ref bool haveText)
    {
        AccessTools.Method(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "AddHeading")
            .Invoke(instance, new object[] {components, capi, heading, haveText});
    }

    static void Postfix(
        CollectibleBehaviorHandbookTextAndExtraInfo __instance, 
        ref bool __result,
        ICoreClientAPI capi,
        ItemStack[] allStacks,
        ActionConsumable<string> openDetailPageFor,
        ItemStack stack,
        List<RichTextComponentBase> components,
        float marginTop,
        bool haveText)
    {
        List<RoofingRecipeStage> recipes = new();
        ApiAdditions.RoofingRecipes(capi).Foreach(recipe => {
            foreach(var stage in recipe.Stages){ 
                if(new ItemStack(stage.ResolvedResult).Equals(capi.World, stack, GlobalConstants.IgnoredStackAttributes)) {
                    recipes.Add(stage);
                }
            }
        });

        if(!recipes.Any()) return;

        if(!components.Where(component => {
            if(component is not ClearFloatTextComponent textComponent) return false;
            if(textComponent.DisplayText == "Created by") return true;
            return false;
        }).Any()) {
            //Does not have the "Created by" header, so we add it ourselves.
            AddHeading(__instance, components, capi, "Created by", ref haveText);
        }

        AddSubHeading(__instance, components, capi, openDetailPageFor, "Roofing (in world)", null);

        foreach(var rec in recipes) {
            int firstIndent = TinyIndent;
            for(int i = 0; i <= rec.StageIndex; i++) {
                RoofingRecipeStage preStage = rec.Recipe.Stages[i];
                if(i > 0) {
                    RichTextComponent plus = new RichTextComponent(capi, " + ", CairoFont.WhiteMediumText());
                    plus.VerticalAlign = EnumVerticalAlign.Middle;
                    components.Add(plus);
                }

                var stacks = preStage.IngredientStacks;
                var slideshow = new SlideshowItemstackTextComponent(capi, stacks, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)));
                slideshow.ShowStackSize = true;
                slideshow.PaddingLeft = firstIndent;
                components.Add(slideshow);
                firstIndent = 0;

                if(i == 0) {
                    RichTextComponent hotkey = new HotkeyComponent(capi, "toolmodeselect", CairoFont.WhiteSmallishText());
                    hotkey.VerticalAlign = EnumVerticalAlign.Middle;
                    components.Add(hotkey);
                }
            }

            var equal = new RichTextComponent(capi, " = ", CairoFont.WhiteMediumText());
            equal.VerticalAlign = EnumVerticalAlign.Middle;
            components.Add(equal);
            
            var output = new ItemstackTextComponent(capi, new ItemStack(rec.ResolvedResult), 40, 10, EnumFloat.Inline);
            output.ShowStacksize = true;
            components.Add(output);
            components.Add(new ClearFloatTextComponent(capi, 10));
        }
    }
}