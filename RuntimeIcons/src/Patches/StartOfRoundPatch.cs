using System;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using RuntimeIcons.Dependency;
using RuntimeIcons.Utils;
using VertexLibrary;

namespace RuntimeIcons.Patches;

[HarmonyPatch]
public class StartOfRoundPatch
{
    internal static void Init()
    {
        RuntimeIcons.Hooks.Add(new Hook(AccessTools.Method(typeof(StartOfRound), nameof(StartOfRound.Awake)),
            PrepareItemCache));
    }

    private static void PrepareItemCache(Action<StartOfRound> orig, StartOfRound __instance)
    {
        ItemCategory.ItemModMap.Clear();

        ItemCategory.VanillaItems ??= __instance.allItemsList.itemsList.ToArray();

        foreach (var itemType in ItemCategory.VanillaItems) ItemCategory.ItemModMap.TryAdd(itemType, new Tuple<string, string>("Vanilla", ""));

        orig(__instance);
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Start))]
    private static void PopulateModdedCache(StartOfRound __instance)
    {
        if (LethalLibProxy.Enabled)
            LethalLibProxy.GetModdedItems(in ItemCategory.ItemModMap);

        if (LethalLevelLoaderProxy.Enabled)
            LethalLevelLoaderProxy.GetModdedItems(in ItemCategory.ItemModMap);

        foreach (var itemType in __instance.allItemsList.itemsList)
        {
            ItemCategory.ItemModMap.TryAdd(itemType, new Tuple<string, string>("Unknown", ""));
        }
    }
}