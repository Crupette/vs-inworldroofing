using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace InWorldRoofing;

public class RoofingRecipeRegistry : ModSystem
{
    RecipeRegistryGeneric<RoofingRecipe> Registry;

    public List<RoofingRecipe> Recipes => Registry.Recipes;

    public override double ExecuteOrder()
    {
        return 1;
    }

    public override void Start(ICoreAPI api)
    {
        Registry = api.RegisterRecipeRegistry<RecipeRegistryGeneric<RoofingRecipe>>(InWorldRoofingSystem.MODID + ".roofingrecipe");
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        Registry.FreeRAMServer();
    }

    public RoofingRecipe[] RecipesForFrameOrientations(ItemStack stack, params string[] orientation)
    {
        List<RoofingRecipe> recipes = new();
        foreach(var recipe in Recipes) {
            if(!orientation.Contains(recipe.OrientationCode)) continue;
            RoofingRecipeStage frameStage = recipe.FrameStage;
            if(frameStage == null) continue;

            foreach(var ingredVar in frameStage.Ingredient) {
                if(ingredVar.SatisfiesAsIngredient(stack, false)) {
                    recipes.Add(recipe);
                    break;
                }
            }
        }
        return recipes.ToArray();
    }

    public RoofingRecipeStage[] RecipesForStack(ItemStack stack) {
        List<RoofingRecipeStage> stages = new();
        foreach(var recipe in Recipes) {
            foreach(var stage in recipe.Stages) {
                ItemStack resolvedStack = new(stage.ResolvedResult);
                if(stage.ResolvedResult.Satisfies(resolvedStack, stack)) {
                    stages.Add(stage);
                }
            }
        }
        return stages.ToArray();
    }
}