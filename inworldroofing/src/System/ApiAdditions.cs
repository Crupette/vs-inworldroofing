using System.Collections.Generic;
using Vintagestory.API.Common;

namespace InWorldRoofing;

public class ApiAdditions
{
    public static InWorldRoofingSystem RoofingSystem(ICoreAPI api) {
        return api.ModLoader.GetModSystem<InWorldRoofingSystem>();
    }

    public static RoofingRecipeRegistry RoofingRecipeRegistry(ICoreAPI api) {
        return api.ModLoader.GetModSystem<RoofingRecipeRegistry>();
    }

    public static List<RoofingRecipe> RoofingRecipes(ICoreAPI api) {
        return RoofingRecipeRegistry(api).Recipes;
    }

    public static void RegisterRoofingRecipe(ICoreAPI api, RoofingRecipe recipe) {
        var registry = RoofingRecipeRegistry(api);
        recipe.RecipeId = registry.Recipes.Count;
        registry.Recipes.Add(recipe);
    }

    public static RoofingRecipe[] RoofingRecipesForFrameOrientations(ICoreAPI api, ItemStack stack, params string[] orientations)
    {
        return RoofingRecipeRegistry(api).RecipesForFrameOrientations(stack, orientations);
    }

    public static RoofingRecipeStage[] RoofingRecipesForStack(ICoreAPI api, ItemStack stack) {
        return RoofingRecipeRegistry(api).RecipesForStack(stack);
    }
}