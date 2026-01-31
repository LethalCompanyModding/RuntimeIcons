using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuntimeIcons.Dependency;

namespace RuntimeIcons.Utils;

public static class ItemCategory
{
    internal static readonly Dictionary<Item, (string api, string modname)> ItemModMap = [];

    public static string GetPathForItem(Item item)
    {
        var modTag = GetTagForItem(item);

        return Path.Combine(((List<string>)[modTag.api, modTag.modname, item.itemName]).Select(p => string.Join("_",
                p.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('.')).ToArray());
    }

    public static (string api, string modname) GetTagForItem(Item item)
    {
        if (ItemModMap.TryGetValue(item, out var modTag)) 
            return modTag;
        
        modTag = ("Unknown", "");

        //ready to support other Libraries that can register items at runtime
        if (DawnLibProxy.Enabled)
        {
            var dawnTag = DawnLibProxy.DefineItem(item);
            
            if (dawnTag != null)
                modTag = dawnTag.Value;
        }
        
        ItemModMap.TryAdd(item, modTag);
        return modTag;
    }
}
