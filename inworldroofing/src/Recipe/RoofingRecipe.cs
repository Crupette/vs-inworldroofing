using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace InWorldRoofing;

[JsonObject]
public class RoofingRecipeStage : IByteSerializable
{
    [JsonProperty]
    public bool IsFrame;
    
    [JsonProperty]
    public CraftingRecipeIngredient[] Ingredient;
    
    [JsonProperty]
    public AssetLocation Result;
    public Block ResolvedResult;

    public ItemStack[] IngredientStacks {
        get {
            List<ItemStack> stacks = new();
            Ingredient.Foreach(ingred => stacks.Add(ingred.ResolvedItemstack));
            return stacks.ToArray();
        }
    }
    public RoofingRecipe Recipe;
    public int StageIndex;

    public bool Resolve(RoofingRecipe recipe, IWorldAccessor resolver, string sourceForError) {
        Recipe = recipe;
        StageIndex = recipe.Stages.IndexOf(this);
        ResolvedResult = resolver.GetBlock(Result);
        if(ResolvedResult == null) {
            resolver.Logger.Error("Stage {0} for roofing recipe '{1}': Result {2} cannot be resolved.", recipe.Stages.IndexOf(this), recipe.Name, Result);
            return false;
        }
        foreach(var ingred in Ingredient) {
            if(!ingred.Resolve(resolver, "Roofing stage")) {
                resolver.Logger.Error("Stage {0} for roofing recipe '{1}': ingredient {2} cannot be resolved.", recipe.Stages.IndexOf(this), recipe.Name, ingred.Code);
                return false;
            }
        }
        return true;
    }

    public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)
    {
        Dictionary<string, string[]> mappings = new();
        if(Ingredient == null || Ingredient.Length == 0) return mappings;

        foreach(var ingredVar in Ingredient) {
            AssetLocation ingredientCode = ingredVar.Code;
            if(!ingredientCode.Path.Contains('*')) continue;
            int wildcardIndex = ingredientCode.Path.IndexOf('*');
            int wildcardEnd = ingredientCode.Path.Length - wildcardIndex - 1;

            List<string> codes = new();
            if(ingredVar.Type == EnumItemClass.Block) {
                foreach(var block in world.Blocks) {
                    if(block.IsMissing) continue;
                    //Skip blocks in SkipVariants
                    if(ingredVar.SkipVariants != null && WildcardUtil.MatchesVariants(ingredientCode, block.Code, ingredVar.SkipVariants)) continue;
                    //Only add codes in AllowedVariants
                    if(WildcardUtil.Match(ingredientCode, block.Code, ingredVar.AllowedVariants)) {
                        string code = block.Code.Path.Substring(wildcardIndex);
                        string wildcardPart = code.Substring(0, code.Length - wildcardEnd).DeDuplicate();
                        codes.Add(wildcardPart);
                    }
                }
            }
            else {
                foreach(var item in world.Items) {
                    if(item?.Code == null || item.IsMissing) continue;
                    //Skip items in SkipVariants
                    if(ingredVar.SkipVariants != null && WildcardUtil.MatchesVariants(ingredientCode, item.Code, ingredVar.SkipVariants)) continue;
                    //Only add codes in AllowedVariants
                    if(WildcardUtil.Match(ingredientCode, item.Code, ingredVar.AllowedVariants)) {
                        string code = item.Code.Path.Substring(wildcardIndex);
                        string wildcardPart = code.Substring(0, code.Length - wildcardEnd).DeDuplicate();
                        codes.Add(wildcardPart);
                    }
                }
            }

            if(ingredVar.Name == null || ingredVar.Name.Length == 0) {
                ingredVar.Name = "wildcard";
            }
            mappings[ingredVar.Name] = codes.ToArray();
        }

        return mappings;
    }

    public CraftingRecipeIngredient GetMatchingIngredient(ItemStack stack)
    {
        return Ingredient.FirstOrDefault(ingred => ingred.SatisfiesAsIngredient(stack, false), null);
    }

    public bool MatchesIngredient(ItemStack stack) {
        return GetMatchingIngredient(stack) != null;
    }

    public RoofingRecipeStage Clone()
    {
        RoofingRecipeStage stage = new();
        stage.IsFrame = IsFrame;
        
        stage.Ingredient = new CraftingRecipeIngredient[Ingredient.Length];
        for(int i = 0; i < Ingredient.Length; i++) {
            stage.Ingredient[i] = Ingredient[i].Clone();
        }

        stage.Result = Result.Clone();
        stage.StageIndex = StageIndex;
        return stage;
    }

    public void ToBytes(BinaryWriter writer)
    {
        writer.Write(IsFrame);
        writer.Write(Ingredient.Length);
        foreach(var ingred in Ingredient) {
            ingred.ToBytes(writer);
        }
        writer.Write(Result);
    }

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        IsFrame = reader.ReadBoolean();
        int numIngredients = reader.ReadInt32();
        Ingredient = new CraftingRecipeIngredient[numIngredients];
        for(int i = 0; i < numIngredients; i++) {
            Ingredient[i] = new CraftingRecipeIngredient();
            Ingredient[i].FromBytes(reader, resolver);
        }
        Result = new AssetLocation(reader.ReadString());
        ResolvedResult = resolver.GetBlock(Result);
    }
}

[JsonObject]
public class RoofingRecipe : IByteSerializable
{
    [JsonProperty]
    public AssetLocation Name;
    public Block ResolvedName;
    public int RecipeId;

    [JsonProperty]
    public RoofingRecipeStage[] Stages;

    [JsonProperty]
    public bool ReplaceDrops = false;
    [JsonProperty]
    public bool ReplaceGridRecipe = false;

    [JsonProperty]
    public EnumOrientableBehavior OrientableBehavior;
    public string OrientationCode = "none";

    [JsonProperty]
    public bool Enabled = true;

    public RoofingRecipeStage FrameStage => Stages.First(stage => stage.IsFrame);

    public bool Resolve(IWorldAccessor resolver, string sourceForError)
    {
        ResolvedName = resolver.BlockAccessor.GetBlock(Name);
        if(ResolvedName == null) {
            resolver.Logger.Error("Roofing recipe '{0}': name cannot be resolved", Name);
            return false;
        }

        foreach(var stage in Stages) {
            if(!stage.Resolve(this, resolver, sourceForError)) return false;
        }
        return true;
    }

    public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)
    {
        Dictionary<string, string[]> mappings = new();
        
        //Add OrientableBehavior mappings
        switch(OrientableBehavior) {
            case EnumOrientableBehavior.None: {

            } break;
            case EnumOrientableBehavior.NWOrientable: {
                mappings.Add("orientation", new[]{ "ns", "we"});
            } break;
            case EnumOrientableBehavior.HOrientable: {
                mappings.Add("orientation", new[]{"north", "east", "south", "west"});
            } break;
        }

        //Iterate through stages, look for mappings.
        if(Stages == null || Stages.Length == 0) return mappings;
        foreach(var stage in Stages) {
            mappings.AddRange(stage.GetNameToCodeMapping(world));
        }

        return mappings;
    }

    public RoofingRecipe Clone()
    {
        RoofingRecipe recipe = new();
        recipe.Name = Name.Clone();
        recipe.Stages = new RoofingRecipeStage[Stages.Length];
        for(int i = 0; i < Stages.Length; i++) {
            recipe.Stages[i] = Stages[i].Clone();
            recipe.Stages[i].Recipe = recipe;
        }

        recipe.ReplaceDrops = ReplaceDrops;
        recipe.ReplaceGridRecipe = ReplaceGridRecipe;
        recipe.OrientableBehavior = OrientableBehavior;
        recipe.OrientationCode = OrientationCode;
        recipe.Enabled = Enabled;

        return recipe;
    }

    public void ToBytes(BinaryWriter writer)
    {
        writer.Write(RecipeId);
        writer.Write(Name.ToShortString());
        writer.Write(ReplaceDrops);
        writer.Write(ReplaceGridRecipe);
        writer.Write((int)OrientableBehavior);
        writer.Write(OrientationCode);

        writer.Write(Stages.Length);
        foreach(var stage in Stages) {
            stage.ToBytes(writer);
        }
    }

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        RecipeId = reader.ReadInt32();
        Name = new AssetLocation(reader.ReadString());
        ResolvedName = resolver.GetBlock(Name);

        ReplaceDrops = reader.ReadBoolean();
        ReplaceGridRecipe = reader.ReadBoolean();
        OrientableBehavior = (EnumOrientableBehavior)reader.ReadInt32();
        OrientationCode = reader.ReadString();
        
        Stages = new RoofingRecipeStage[reader.ReadInt32()];
        for(int i = 0; i < Stages.Length; i++) {
            Stages[i] = new RoofingRecipeStage();
            Stages[i].FromBytes(reader, resolver);
            Stages[i].Recipe = this;
            Stages[i].StageIndex = i;
        }
    }
}