using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using JetBrains.Annotations;
using LethalLib.Modules;

namespace RuntimeIcons.Dependency;

public static class LethalLibProxy
{
    private static bool? _enabled;

    public static bool Enabled
    {
        get
        {
            _enabled ??= Chainloader.PluginInfos.ContainsKey("evaisa.lethallib");
            return _enabled.Value;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void GetModdedItems([NotNull] in Dictionary<Item, (string api, string modname)> items)
    {
        RuntimeIcons.Log.LogInfo("LethalLib found, reading Items.scrapItems");
        foreach (var scrapItem in Items.scrapItems) items.TryAdd(scrapItem.item, ("LethalLib", scrapItem.modName));
        foreach (var scrapItem in Items.plainItems) items.TryAdd(scrapItem.item, ("LethalLib", scrapItem.modName));
        foreach (var scrapItem in Items.shopItems)  items.TryAdd(scrapItem.item, ("LethalLib", scrapItem.modName));
    }
}
