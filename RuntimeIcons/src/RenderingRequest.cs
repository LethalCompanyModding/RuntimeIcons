using System.IO;
using RuntimeIcons.Config;
using RuntimeIcons.Patches;
using UnityEngine;

namespace RuntimeIcons;

public struct RenderingRequest
{
    public RenderingRequest(GrabbableObject grabbableObject, Sprite errorSprite)
    {
        GrabbableObject = grabbableObject;
        ErrorSprite = errorSprite;

        ItemKey = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
            .Replace(Path.DirectorySeparatorChar, '/');

        RuntimeIcons.OverrideMap.TryGetValue(ItemKey, out var overrideHolder);

        OverrideHolder = overrideHolder;
    }

    public GrabbableObject GrabbableObject { get; }

    public Sprite ErrorSprite { get; }

    public OverrideHolder OverrideHolder { get; }

    public string ItemKey { get; }

    public bool HasIcon
    {
        get
        {
            var item = GrabbableObject.itemProperties;

            var inList = PluginConfig.ItemList.Contains(ItemKey);

            if (PluginConfig.ItemListBehaviour switch
                {
                    PluginConfig.ListBehaviour.BlackList => inList,
                    PluginConfig.ListBehaviour.WhiteList => !inList,
                    _ => false
                })
                return true;

            if (!item.itemIcon)
                return false;
            if (item.itemIcon == RuntimeIcons.LoadingSprite)
                return false;
            if (item.itemIcon.name == "ScrapItemIcon")
                return false;
            if (item.itemIcon.name == "ScrapItemIcon2")
                return false;

            if (OverrideHolder?.OverrideSprite && item.itemIcon != OverrideHolder.OverrideSprite)
                return false;

            return true;
        }
    }
}