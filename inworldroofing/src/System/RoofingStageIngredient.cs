using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InWorldRoofing;

[JsonObject]
public class RoofingStageIngredient {
    [JsonProperty]
    public EnumItemClass Type;

    [JsonProperty]
    public AssetLocation Code;
    [JsonProperty]
    public string Name;

    [JsonProperty]
    public int Quantity;
    
    [JsonProperty]
    public string[] AllowedVariants;
    [JsonProperty]
    public string[] SkipVariants;

    public bool IsWildcard;
    public bool IsResolved = false;
    public ItemStack ResolvedItemstack;

    public bool CollectibleEquals(RoofingStageIngredient other)
    {
        if(Type != other.Type) return false;
        if(Code != other.Code) return false;
        if(AllowedVariants != other.AllowedVariants) return false;
        return true;
    }

    public bool Resolve(IWorldAccessor resolver, string sourceForErrorLogging)
    {
        if(IsResolved) return true;
        if(Code.Path.Contains('*')) {
            IsWildcard = true;
            return true;
        }
        if(Type == EnumItemClass.Block) {
            Block block = resolver.GetBlock(Code);
            if(block == null || block.IsMissing) {
                resolver.Logger.Warning($"Failed to resolve roofing stage ingredient with code {Code} in {sourceForErrorLogging}");
                return false;
            }
            
            ResolvedItemstack = new ItemStack(block, Quantity);
        }else {
            Item item = resolver.GetItem(Code);
            if(item == null || item.IsMissing) {
                resolver.Logger.Warning($"Failed to resolve roofing stage ingredient with code {Code} in {sourceForErrorLogging}");
                return false;
            }
            
            ResolvedItemstack = new ItemStack(item, Quantity);
        }
        IsResolved = true;
        return true;
    }

    public bool SatisfiesAsIngredient(ItemStack inputStack, bool checkStacksize = true) {
        if(inputStack == null) return false;
        if(IsWildcard) {
            if(Type != inputStack.Class) return false;
            if(!WildcardUtil.Match(Code, inputStack.Collectible.Code, AllowedVariants))
                return false;
            
            if(checkStacksize && inputStack.StackSize < Quantity) return false;
        }else{
            if(!ResolvedItemstack.Satisfies(inputStack)) return false;
            if(checkStacksize && inputStack.StackSize < ResolvedItemstack.StackSize)
                return false;
        }
        return true;
    }

    public RoofingStageIngredient Clone()
    {
        return CloneTo<RoofingStageIngredient>();
    }

    public T CloneTo<T>() where T : RoofingStageIngredient, new()
    {
        T val = new T
        {
            Code = Code.Clone(),
            Type = Type,
            Name = Name,
            Quantity = Quantity,
            IsWildcard = IsWildcard,
            AllowedVariants = ((AllowedVariants == null) ? null : ((string[])AllowedVariants.Clone())),
            SkipVariants = ((SkipVariants == null) ? null : ((string[])SkipVariants.Clone())),
            ResolvedItemstack = ResolvedItemstack?.Clone(),
        };

        return val;
    }
}