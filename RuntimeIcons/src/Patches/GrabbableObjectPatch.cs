using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using RuntimeIcons.Config;
using RuntimeIcons.Utils;
using UnityEngine;

namespace RuntimeIcons.Patches;

[HarmonyPatch]
public static class GrabbableObjectPatch
{
    private static ThrottleUtils.Semaphore _renderingSemaphore;

    internal static ThrottleUtils.Semaphore RenderingSemaphore
    {
        get
        {
            _renderingSemaphore ??= ThrottleUtils.Semaphore.CreateNewSemaphore(PluginConfig.RenderingAmount, PluginConfig.RenderingInterval, ThrottleUtils.SemaphoreTimeUnit.Update);
            return _renderingSemaphore;
        }
    }

    internal static bool ItemHasIcon(Item item)
    {
        var key = CategorizeItemPatch.GetPathForItem(item)
            .Replace(Path.DirectorySeparatorChar, '/');

        var inList = PluginConfig.ItemList.Contains(key);
        
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

        if (RuntimeIcons.OverrideMap.TryGetValue(key, out var holder) && holder.OverrideSprite &&
            item.itemIcon != holder.OverrideSprite)
            return false;
        
        return true;
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.Start))]
    private static void AfterStart(GrabbableObject __instance)
    {
        if (ItemHasIcon(__instance.itemProperties)) 
            return;
        
        __instance.itemProperties.itemIcon =  RuntimeIcons.LoadingSprite;
        
        __instance.StartCoroutine(ComputeSpriteCoroutine(__instance));
    }

    private static IEnumerator ComputeSpriteCoroutine(GrabbableObject @this)
    {
        //wait two frames for the animations to settle
        yield return null;
        yield return null;
        
        //throttle renders to not hang the game
        yield return new WaitUntil(()=>RenderingSemaphore.TryAcquire());
        
        if (ItemHasIcon(@this.itemProperties))
            yield break;
        
        ComputeSprite(@this);
        
        if (@this.itemProperties.itemIcon == RuntimeIcons.LoadingSprite2)
            @this.itemProperties.itemIcon =  RuntimeIcons.WarningSprite;
        
        UpdateIconsInHUD(@this.itemProperties);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.EquipItem))]
    private static void OnGrab(GrabbableObject __instance)
    {
        if (!__instance.IsOwner)
            return;
        
        if (__instance.itemProperties.itemIcon != RuntimeIcons.WarningSprite)
            return;
        
        RuntimeIcons.Log.LogInfo($"Attempting to refresh BrokenIcon for {__instance.itemProperties.itemName}!");
        
        RenderingSemaphore.TryAcquire();
        ComputeSprite(__instance);

        if (__instance.itemProperties.itemIcon == RuntimeIcons.LoadingSprite2)
            __instance.itemProperties.itemIcon =  RuntimeIcons.ErrorSprite;
        
        UpdateIconsInHUD(__instance.itemProperties);
    }
    

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ComputeSprite(GrabbableObject grabbableObject)
    {
        var key = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
            .Replace(Path.DirectorySeparatorChar, '/');

        RuntimeIcons.OverrideMap.TryGetValue(key, out var overrideHolder);
        
        RuntimeIcons.Log.LogWarning($"Computing {key} icon");
        
        grabbableObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite2;
        
        UpdateIconsInHUD(grabbableObject.itemProperties);

        if (overrideHolder!=null && overrideHolder.OverrideSprite)
        {
            RuntimeIcons.Log.LogWarning($"Using static icon from {overrideHolder.Source} for {key}");
            grabbableObject.itemProperties.itemIcon = overrideHolder.OverrideSprite;
            UpdateIconsInHUD(grabbableObject.itemProperties);
            RuntimeIcons.Log.LogInfo($"{key} now has a new icon | 1");
            return;
        }
        
        var stage = RuntimeIcons.CameraStage;
        try
        {
            var rotation = Quaternion.Euler(grabbableObject.itemProperties.restingRotation.x, grabbableObject.itemProperties.floorYOffset + 90f, grabbableObject.itemProperties.restingRotation.z);

            if (overrideHolder is { ItemRotation: not null })
            {
                rotation = Quaternion.Euler(overrideHolder.ItemRotation.Value + new Vector3(0, 90f, 0));
            }
            
            RuntimeIcons.Log.LogInfo($"Setting stage for {key}");
            
            stage.SetObjectOnStage(grabbableObject);
            
            stage.CenterObjectOnPivot(rotation);
            
            RuntimeIcons.Log.LogInfo($"StagedObject offset {stage.StagedTransform.localPosition} rotation {stage.StagedTransform.localRotation.eulerAngles}");

            if (overrideHolder is { StageRotation: not null })
            {
                stage.PivotTransform.rotation = Quaternion.Euler(overrideHolder.StageRotation.Value);
            }
            else
            {
                stage.FindOptimalRotation();
            }

            RuntimeIcons.Log.LogInfo($"Stage rotation {stage.PivotTransform.rotation.eulerAngles}");
            
            stage.PrepareCameraForShot();

            var texture = stage.TakeSnapshot();

            // UnPremultiply the texture
            texture.UnPremultiply();
            texture.Apply();
            
            if (PluginConfig.DumpToCache)
            {
                var outputPath = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties);
                var directory = Path.GetDirectoryName(outputPath) ?? "";
                var filename = Path.GetFileName(outputPath);
                
                texture.SavePNG(filename,
                    Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.PNG", directory));

                texture.SaveEXR(filename,
                    Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.EXR", directory));
            }

            var transparentCount = texture.GetTransparentCount();
            var totalPixels = texture.width * texture.height;
            var ratio = (float)transparentCount / (float)totalPixels;
            
            if (ratio <= PluginConfig.TransparencyRatio)
            {
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(texture.width / 2f, texture.height / 2f));
                sprite.name = sprite.texture.name = $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                grabbableObject.itemProperties.itemIcon = sprite;
                RuntimeIcons.Log.LogInfo($"{key} now has a new icon | 2");
            }
            else
            {
                RuntimeIcons.Log.LogError($"{key} Generated {ratio*100}% Empty Sprite!");
            }

        } catch (Exception ex){
			RuntimeIcons.Log.LogError($"Error generating {key}\n{ex}");
		}
        finally
        {
            stage.ResetStage();
        }
    }

    internal static void UpdateIconsInHUD(Item item)
    {
        if (!GameNetworkManager.Instance || !GameNetworkManager.Instance.localPlayerController)
            return;

        var itemSlots = GameNetworkManager.Instance.localPlayerController.ItemSlots;
        var itemSlotIcons = HUDManager.Instance.itemSlotIcons;
        for (var i = 0; i < itemSlots.Length; i++)
        {
            if (i >= itemSlotIcons.Length)
                break;
            if (!itemSlots[i] || itemSlots[i].itemProperties != item)
                continue;
            itemSlotIcons[i].sprite = item.itemIcon;
        }
    }
}