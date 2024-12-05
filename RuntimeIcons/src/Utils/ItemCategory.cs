using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RuntimeIcons.Utils;

public class ItemCategory
{
    internal static Item[] VanillaItems;
    internal static readonly Dictionary<Item, Tuple<string,string>> ItemModMap = [];

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