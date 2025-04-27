using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing;

public class RoofingRecipeLoader : ModSystem
{
    ICoreServerAPI api;
    InWorldRoofingSystem RoofingSystem;

    public override double ExecuteOrder()
    {
        return 1;
    }

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Server;
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if(api is not ICoreServerAPI sapi) return;
        this.api = sapi;

        RoofingSystem = sapi.ModLoader.GetModSystem<InWorldRoofingSystem>();

        LoadRoofingRecipes();
    }

    public void LoadRoofingRecipes()
    {
        Dictionary<AssetLocation, JToken> files = api.Assets.GetMany<JToken>(api.Server.Logger, "recipes/roofing");
        foreach(var file in files) {
            if(file.Value is JObject) {
                JObject recipe = file.Value as JObject;
                LoadRoofingRecipe(file.Key, recipe.ToObject<RoofingRecipe>(file.Key.Domain));
            }else
            if(file.Value is JArray) {
                JArray recipeArray = file.Value as JArray;
                foreach(var recipe in recipeArray) {
                    LoadRoofingRecipe(file.Key, recipe.ToObject<RoofingRecipe>(file.Key.Domain));
                }
            }
        }
    }

    public void LoadRoofingRecipe(AssetLocation path, RoofingRecipe recipe)
    {
        if(!(recipe?.Enabled ?? false)) return;
        recipe.OrientationCode = "none";

        Dictionary<string, string[]> nameToCodeMappings = recipe.GetNameToCodeMapping(api.World);
        if(nameToCodeMappings.Count > 0) {
            List<RoofingRecipe> subRecipes = new();

            int combinations = 0;
            bool first = true; //For n^n recipe variants
            foreach(var mappingPair in nameToCodeMappings) {
                if(first) combinations = mappingPair.Value.Length;
                else combinations *= mappingPair.Value.Length;
                first = false;
            }

            first = true;
            //For each mapping, replace the {variant} string with a proper name
            foreach(var mappingPair in nameToCodeMappings) {
                string variantCode = mappingPair.Key;
                string[] variants = mappingPair.Value;

                for(int i = 0; i < combinations; i++) {
                    string variant = variants[i % variants.Length];
                    RoofingRecipe subRecipe;
                    if(first) subRecipes.Add(subRecipe = recipe.Clone());
                    else subRecipe = subRecipes[i];

                    //Replace the final block code with the variant code.
                    subRecipe.Name = subRecipe.Name.CopyWithPath(subRecipe.Name.Path.Replace("{" + variantCode + "}", variant));
                    //If the name is "orientation", replace the OrientationCode value in subRecipe
                    if(variantCode == "orientation") {
                        subRecipe.OrientationCode = variant;
                    }

                    //Replace all wildcards and named variants with variant code.
                    foreach(var stage in subRecipe.Stages) {
                        stage.Result = stage.Result.CopyWithPath(stage.Result.Path.Replace("{" + variantCode + "}", variant));
                        foreach(var ingredient in stage.Ingredient) {
                            if(ingredient.Name == variantCode) {
                                ingredient.Code = ingredient.Code.CopyWithPath(ingredient.Code.Path.Replace("*", variant));
                            }
                        }
                    }
                }
                first = false;
            }
            foreach(var subRecipe in subRecipes) {
                if(!subRecipe.Resolve(api.World, "roofing recipe " + path)) {
                    continue;
                }
                subRecipe.RecipeId = RoofingSystem.RoofingRecipeRegistry.Recipes.Count;
                RoofingSystem.RoofingRecipeRegistry.Recipes.Add(subRecipe);
            }
        }else{
            if(!recipe.Resolve(api.World, "roofing recipe " + path)) {
                return;
            }
            recipe.RecipeId = RoofingSystem.RoofingRecipeRegistry.Recipes.Count;
            RoofingSystem.RoofingRecipeRegistry.Recipes.Add(recipe);
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        Dictionary<AssetLocation, (int, List<RoofingRecipe>)> stageRecipes = new();
        foreach(var recipe in RoofingSystem.RoofingRecipeRegistry.Recipes) {
            for(int i = 0; i < recipe.Stages.Length; i++) {
                RoofingRecipeStage stage = recipe.Stages[i];
                Block stageBlock = stage.ResolvedResult;

                if(i != recipe.Stages.Length - 1) {
                    if(!stageRecipes.ContainsKey(stageBlock.Code)) {
                        stageRecipes[stageBlock.Code] = (i + 1, new() { recipe });
                    }else stageRecipes[stageBlock.Code].Item2.Add(recipe);
                }

                if(stageBlock.HasBlockBehavior<BlockBehaviorHorizontalOrientable>()) {
                    BlockBehaviorHorizontalOrientable orientable = stageBlock.GetBehavior<BlockBehaviorHorizontalOrientable>();
                    stageBlock.BlockBehaviors = stageBlock.BlockBehaviors.Remove(orientable);
                    stageBlock.CollectibleBehaviors = stageBlock.CollectibleBehaviors.Remove(orientable);
                }

                if(stageBlock.HasBlockBehavior<BlockBehaviorNWOrientable>()) {
                    BlockBehaviorNWOrientable orientable = stageBlock.GetBehavior<BlockBehaviorNWOrientable>();
                    stageBlock.BlockBehaviors = stageBlock.BlockBehaviors.Remove(orientable);
                    stageBlock.CollectibleBehaviors = stageBlock.CollectibleBehaviors.Remove(orientable);
                }
            }

            if(!recipe.ReplaceDrops) continue;

            List<BlockDropItemStack> blockDropStacks = new();
            foreach(var stage in recipe.Stages) {
                blockDropStacks.Add(new BlockDropItemStack(stage.Ingredient[0].ResolvedItemstack));
                blockDropStacks.Last().Quantity.avg = stage.Ingredient[0].Quantity;
            }
            recipe.Stages[^1].ResolvedResult.Drops = blockDropStacks.ToArray();
        }

        foreach(var stagePair in stageRecipes) {
            Block stageBlock = api.World.GetBlock(stagePair.Key);
            int stage = stagePair.Value.Item1;
            List<RoofingRecipe> recipes = stagePair.Value.Item2;

            int[] recipeIds = new int[recipes.Count];
            for(int i = 0; i < recipeIds.Length; i++) recipeIds[i] = recipes[i].RecipeId;

            BlockBehaviorRoofingStage stageBehavior = new(stageBlock);
            JsonObject properties = new(new JObject());
            properties.Token["stage"] = stage;
            properties.Token["recipes"] = new JArray(recipeIds);

            stageBehavior.Initialize(properties);

            stageBlock.BlockBehaviors = stageBlock.BlockBehaviors.Append(stageBehavior);
            stageBlock.CollectibleBehaviors = stageBlock.CollectibleBehaviors.Append(stageBehavior);
        }
    }

}