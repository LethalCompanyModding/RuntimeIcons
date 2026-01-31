using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using Dawn;
using JetBrains.Annotations;

namespace RuntimeIcons.Dependency;

public static class DawnLibProxy
{
    private static bool? _enabled;

    public static bool Enabled
    {
        get
        {
            _enabled ??= Chainloader.PluginInfos.ContainsKey("com.github.teamxiaolan.dawnlib");
            return _enabled.Value;
        }
    }
    
    // ReSharper disable function SuspiciousTypeConversion.Global
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void GetModdedItems([NotNull] in Dictionary<Item, (string api, string modname)> items)
    {
        RuntimeIcons.Log.LogWarning("DawnLib found, reading LethalContent.Items for modded items");
        foreach (var (key, dawnItemInfo) in LethalContent.Items)
        {
            if (key.IsVanilla())
            {
                items[dawnItemInfo.Item] = ("Vanilla", "");
            }
            else
            {
                items[dawnItemInfo.Item] = ("DawnLib", key.Namespace);
            }
            
        }
    }
    
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static (string api, string modname)? DefineItem(Item item)
    {
        var info = item.GetDawnInfo();

        //Note: while this is marked as never null, it indeed can be null!
        if (info == null)
        {
            return null;
        }
        
        if (info.Key.IsVanilla())
        {
            return ("Vanilla", "");
        }

        return ("DawnLib", info.Key.Namespace);
    }
}
