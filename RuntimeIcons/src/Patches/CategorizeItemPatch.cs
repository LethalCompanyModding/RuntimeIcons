using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using RuntimeIcons.Dependency;

namespace RuntimeIcons.Patches;

[HarmonyPatch]
public class CategorizeItemPatch
{
    internal static Item[] VanillaItems;
    internal static readonly Dictionary<Item, Tuple<string,string>> ItemModMap = [];

    internal static void Init()
    {
        RuntimeIcons.Hooks.Add(new Hook(AccessTools.Method(typeof(StartOfRound), nameof(StartOfRound.Awake)),
            PrepareItemCache));
    }
    
    private static void PrepareItemCache(Action<StartOfRound> orig, StartOfRound __instance)
    {
        ItemModMap.Clear();

        VanillaItems ??= __instance.allItemsList.itemsList.ToArray();
        
        foreach (var itemType in VanillaItems)
            ItemModMap.TryAdd(itemType, new Tuple<string, string>("Vanilla", ""));

        orig(__instance);
    }
    
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Start))]
    private static void PopulateModdedCache(StartOfRound __instance)
    {
        if (LethalLibProxy.Enabled)
            LethalLibProxy.GetModdedItems(in ItemModMap);

        if (LethalLevelLoaderProxy.Enabled)
            LethalLevelLoaderProxy.GetModdedItems(in ItemModMap);

        foreach (var itemType in __instance.allItemsList.itemsList)
            ItemModMap.TryAdd(itemType, new Tuple<string, string>("Unknown", ""));
    }

    public static string GetPathForItem(Item item)
    {
        var modTag = GetTagForItem(item);

        return GetPathForTag(modTag, item);
    }

    public static Tuple<string, string> GetTagForItem(Item item)
    {
        if (!ItemModMap.TryGetValue(item, out var modTag))
            modTag = new Tuple<string, string>("Unknown", "");
        return modTag;
    }

    public static string GetPathForTag(Tuple<string, string> modTag, Item item)
    {
        var cleanName = string.Join("_",
                item.itemName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('.');
        
        var cleanMod = string.Join("_",
                modTag.Item2.Split(Path.GetInvalidPathChars(), StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('.');

        var path = Path.Combine(modTag.Item1, cleanMod, cleanName);

        return path;
    }
    
    private static readonly Regex ConfigFilterRegex = new Regex(@"[\n\t\\\'\[\]]");

    public static string SanitizeForConfig(string input)
    {
        return ConfigFilterRegex.Replace(input, "").Trim();
    }
    
}