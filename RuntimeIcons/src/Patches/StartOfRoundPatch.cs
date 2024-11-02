using System;
using System.Collections.Generic;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using RuntimeIcons.Dependency;

namespace RuntimeIcons.Patches;

[HarmonyPatch]
public class StartOfRoundPatch
{
    internal static int AvailableRenders { get; set; } = 0;
    
    internal static readonly Dictionary<Item, Tuple<string,string>> ItemModMap = [];

    internal static void Init()
    {
        RuntimeIcons.Hooks.Add(new Hook(AccessTools.Method(typeof(StartOfRound), nameof(StartOfRound.Awake)),
            PrepareItemCache));
    }
    
    private static void PrepareItemCache(Action<StartOfRound> orig, StartOfRound __instance)
    {
        ItemModMap.Clear();
        foreach (var itemType in __instance.allItemsList.itemsList)
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
    
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.LateUpdate))]
    private static void TrackNewRenders(StartOfRound __instance)
    {
        AvailableRenders = 1;
    }

}