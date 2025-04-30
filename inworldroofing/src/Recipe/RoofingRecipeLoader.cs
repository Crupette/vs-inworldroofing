using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing;

public class RoofingRecipeLoader : ModSystem
{
    ICoreServerAPI api;

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

        LoadRoofingRecipes();
    }

    public void LoadRoofingRecipes()
    {
        int quantityRegistered = 0;
        int quantityIgnored = 0;

        Dictionary<AssetLocation, JToken> files = api.Assets.GetMany<JToken>(api.Server.Logger, "recipes/roofing");
        foreach(var file in files) {
            if(file.Value is JObject) {
                JObject recipe = file.Value as JObject;
                LoadRoofingRecipe(file.Key, recipe.ToObject<RoofingRecipe>(file.Key.Domain), ref quantityRegistered, ref quantityIgnored);
            }else
            if(file.Value is JArray) {
                JArray recipeArray = file.Value as JArray;
                foreach(var recipe in recipeArray) {
                    LoadRoofingRecipe(file.Key, recipe.ToObject<RoofingRecipe>(file.Key.Domain), ref quantityRegistered, ref quantityIgnored);
                }
            }
        }

        api.World.Logger.Debug($"{quantityRegistered} roofing recipes loaded, {quantityIgnored} ignored.");
    }

    public void LoadRoofingRecipe(AssetLocation path, RoofingRecipe recipe, ref int quantityRegistered, ref int quantityIgnored)
    {
        if(!(recipe?.Enabled ?? false)) return;
        recipe.OrientationCode = "none";

        Dictionary<string, string[]> nameToCodeMappings = recipe.GetNameToCodeMapping(api.World);
        if(nameToCodeMappings.Count > 0) {
            List<RoofingRecipe> subRecipes = new() {recipe.Clone()};
            //The normal method used by vanilla doesn't work here, because each orientation needs all of the other
            //variants. Therefore, we need to properly nest things.
            foreach(var mappingPair in nameToCodeMappings) {
                //List to replace subRecipes when this loop is through.
                List<RoofingRecipe> newRecipes = new();
                string variantName = mappingPair.Key;
                string[] variants = mappingPair.Value;

                //Fill {variantName} with 'variant' in recipes to replace subRecipe
                foreach(var subRecipe in subRecipes) {
                    foreach(var variant in variants) {
                        RoofingRecipe newRecipe;
                        newRecipes.Add(newRecipe = subRecipe.Clone());

                        newRecipe.Name = newRecipe.Name.CopyWithPath(newRecipe.Name.Path.Replace("{" + variantName + "}", variant));
                        //If the name is "orientation", replace the OrientationCode value in subRecipe
                        if(variantName == "orientation") {
                            newRecipe.OrientationCode = variant;
                        }

                        //Replace all wildcards and variant selectors with this variant in each stage.
                        foreach(var stage in newRecipe.Stages) {
                            stage.Result = stage.Result.CopyWithPath(stage.Result.Path.Replace("{" + variantName + "}", variant));
                            foreach(var ingredient in stage.Ingredient) {
                                ingredient.FillPlaceHolder(variantName, variant);
                                if(ingredient.Name == variantName) {
                                    ingredient.Code = ingredient.Code.CopyWithPath(ingredient.Code.Path.Replace("*", variant));
                                }
                            }
                        }
                    }
                }
                subRecipes = newRecipes;
            }
            foreach(var subRecipe in subRecipes) {
                if(!subRecipe.Resolve(api.World, "roofing recipe " + path)) {
                    quantityIgnored++;
                    continue;
                }

                ApiAdditions.RegisterRoofingRecipe(api, subRecipe);
                quantityRegistered++;
            }
        }else{
            if(!recipe.Resolve(api.World, "roofing recipe " + path)) {
                quantityIgnored++;
                return;
            }
            ApiAdditions.RegisterRoofingRecipe(api, recipe);
            quantityRegistered++;
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        long elapsedMiliseconds = api.World.ElapsedMilliseconds;
        int recipesDisabled = 0;
        Dictionary<AssetLocation, (int, List<RoofingRecipe>)> stageRecipes = new();
        foreach(var recipe in ApiAdditions.RoofingRecipes(api)) {
            for(int i = 0; i < recipe.Stages.Length; i++) {
                RoofingRecipeStage stage = recipe.Stages[i];
                Block stageBlock = stage.ResolvedResult;

                //Collect stageblocks for later behavior manipulations.
                if(!stageRecipes.ContainsKey(stageBlock.Code)) {
                    stageRecipes[stageBlock.Code] = (i + 1, new() { recipe });
                }else stageRecipes[stageBlock.Code].Item2.Add(recipe);

                //Remove OrientableBehavior from blocks with stage.
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

                //Add FrameMaterial behavior to ingredients part of FrameStage
                if(stage.IsFrame) {
                    foreach(var ingred in stage.IngredientStacks) {
                        if(ingred.Collectible.HasBehavior<CollectibleBehaviorFrameMaterial>()) continue;
                        CollectibleBehaviorFrameMaterial frameBehavior = new(ingred.Collectible);
                        frameBehavior.Initialize(new JsonObject(new JObject()));

                        ingred.Collectible.CollectibleBehaviors = new CollectibleBehavior[] { frameBehavior }.Append(ingred.Collectible.CollectibleBehaviors);
                    }
                }
            }

            if(!recipe.ReplaceDrops) continue;

            //Replace drops for stage block with sum of previous stages
            List<BlockDropItemStack> blockDropStacks = new();
            foreach(var stage in recipe.Stages) {
                BlockDropItemStack stageDrop;
                if((stageDrop = blockDropStacks.Find(drop => {
                    return drop.Code == stage.Ingredient[0].Code;
                })) == null) {
                    stageDrop = new BlockDropItemStack(stage.Ingredient[0].ResolvedItemstack);
                    stageDrop.Quantity.avg = stage.Ingredient[0].Quantity;
                    blockDropStacks.Add(stageDrop);
                }else{
                    stageDrop.Quantity.avg += stage.Ingredient[0].Quantity;
                }
            }
            recipe.Stages[^1].ResolvedResult.Drops = blockDropStacks.ToArray();
        }

        //Add BehaviorRoofingStage to all blocks associated with a RoofingRecipeStage, except for the final block.
        //The behavior needs data from the recipe stage after it, so the index is shifted 1 up.
        foreach(var stagePair in stageRecipes) {
            Block stageBlock = api.World.GetBlock(stagePair.Key);
            int stage = stagePair.Value.Item1;
            List<RoofingRecipe> recipes = stagePair.Value.Item2;

            if(stage < recipes[0].Stages.Length) {
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

            //If the recipe says to replace the grid recipe, disable and remove it from World.GridRecipes.
            if(recipes[0].ReplaceGridRecipe) {
                List<GridRecipe> toRemove = new();
                foreach(var gridRecipe in api.World.GridRecipes) {
                    if(gridRecipe.Output.Code == stageBlock.Code) {
                        //api.Logger.Debug($"Disabled recipe for {gridRecipe.Output.Code}");
                        recipesDisabled++;
                        toRemove.Add(gridRecipe);
                        gridRecipe.Enabled = false;
                    }
                }
                toRemove.Foreach(gridRecipe => api.World.GridRecipes.Remove(gridRecipe));
            }
        }

        api.World.Logger.Debug($"{recipesDisabled} grid recipes disabled by roofing recipes in {api.World.ElapsedMilliseconds - elapsedMiliseconds} ms.");
    }

}