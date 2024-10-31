using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using RuntimeIcons.Components;
using RuntimeIcons.Config;
using RuntimeIcons.Utils;
using UnityEngine;
using VertexLibrary;
using Object = UnityEngine.Object;

namespace RuntimeIcons.Patches;

[HarmonyPatch]
public static class GrabbableObjectPatch
{
    internal static bool ItemHasIcon(Item item)
    {
        if (!item.itemIcon)
            return false;
        if (item.itemIcon == RuntimeIcons.LoadingSprite)
            return false;
        if (item.itemIcon.name == "ScrapItemIcon")
            return false;
        if (item.itemIcon.name == "ScrapItemIcon2")
            return false;
        return true;
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.Start))]
    private static void AfterStart(GrabbableObject __instance)
    {
        if (ItemHasIcon(__instance.itemProperties)) 
            return;

        var inList = PluginConfig.ItemList.Contains(__instance.itemProperties.itemName);
        
        if (PluginConfig.ItemListBehaviour switch
            {
                PluginConfig.ListBehaviour.BlackList => inList,
                PluginConfig.ListBehaviour.WhiteList => !inList,
                PluginConfig.ListBehaviour.None => false,
                _ => false
            })
            return;
        
        __instance.StartCoroutine(ComputeSpriteCoroutine(__instance));
    }

    private static IEnumerator ComputeSpriteCoroutine(GrabbableObject @this)
    {
        //wait two frames for the animations to settle
        yield return null;
        yield return null;
        
        //throttle renders to not hang the game
        yield return new WaitUntil(()=>StartOfRoundPatch.AvailableRenders > 0);
        
        if (ItemHasIcon(@this.itemProperties))
            yield break;
        
        ComputeSprite(@this);
        
        if (@this.itemProperties.itemIcon == RuntimeIcons.LoadingSprite)
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
        
        ComputeSprite(__instance);

        if (__instance.itemProperties.itemIcon == RuntimeIcons.LoadingSprite)
            __instance.itemProperties.itemIcon =  RuntimeIcons.ErrorSprite;
        
        UpdateIconsInHUD(__instance.itemProperties);
    }
    

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ComputeSprite(GrabbableObject grabbableObject)
    {
        RuntimeIcons.Log.LogWarning($"Computing {grabbableObject.itemProperties.itemName} icon");
        
        grabbableObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite;
        
        UpdateIconsInHUD(@grabbableObject.itemProperties);

        if (PluginConfig.FileOverrides.TryGetValue(grabbableObject.itemProperties.itemName,
                out var filename))
        {
            
            RuntimeIcons.Log.LogWarning($"Assigning {filename} to {grabbableObject.itemProperties.itemName}");
            try
            {
                if (File.Exists(filename))
                {
                    var fileData = File.ReadAllBytes(filename);

                    var sprite = SpriteUtils.GetSprite(fileData);
                    
                    
                    
                    var texture = sprite.texture;
                    if (texture.width != texture.height)
                    {
                        Object.Destroy(sprite);
                        Object.Destroy(texture);
                        RuntimeIcons.Log.LogError($"Expected Icon {filename} was not square!");
                    }
                    else
                    {
                        var transparentCount = texture.GetTransparentCount();
                        var totalPixels = texture.width * texture.height;
                        var ratio = (float)transparentCount / (float)totalPixels;

                        if (ratio <= PluginConfig.TransparencyRatio)
                        {
                            sprite.name = $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                            grabbableObject.itemProperties.itemIcon = sprite;
                            UpdateIconsInHUD(grabbableObject.itemProperties);
                            RuntimeIcons.Log.LogInfo($"{grabbableObject.itemProperties.itemName} now has a new icon | 1");
                            return;
                        }
                        else
                        {
                            Object.Destroy(sprite);
                            Object.Destroy(texture);
                            RuntimeIcons.Log.LogError($"Expected Icon {filename} was {ratio*100}% Empty!");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RuntimeIcons.Log.LogError($"Failed to read {filename}:\n{ex}");
            }
            finally
            {
                RuntimeIcons.Log.LogWarning($"Fallback to Staged image for {grabbableObject.itemProperties.itemName}");
            }
        }

        //we're rendering!
        StartOfRoundPatch.AvailableRenders--;
        
        var stage = RuntimeIcons.CameraStage;
        try
        {
            var rotation = Quaternion.Euler(grabbableObject.itemProperties.restingRotation.x, grabbableObject.itemProperties.floorYOffset + 90f, grabbableObject.itemProperties.restingRotation.z);
            
            RuntimeIcons.Log.LogInfo($"Setting stage for {grabbableObject.NetworkObject.gameObject.name}");
            
            stage.SetObjectOnStage(grabbableObject.NetworkObject.gameObject);
            
            stage.CenterObjectOnPivot(rotation);
            
            RuntimeIcons.Log.LogInfo($"StagedObject offset {stage.StagedTransform.localPosition} rotation {stage.StagedTransform.localRotation.eulerAngles}");

            FindOptimalRotation(stage, grabbableObject);
            
            RuntimeIcons.Log.LogInfo($"Stage rotation {stage.PivotTransform.rotation.eulerAngles}");
            
            stage.PrepareCameraForShot();

            var texture = stage.TakeSnapshot();

            // UnPremultiply the texture
            texture.UnPremultiply();
            texture.Apply();
            
            if (PluginConfig.DumpToCache)
            {

                var outputName = grabbableObject.itemProperties.itemName;
                var sanitizedName = String.Join("_",
                        outputName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                    .TrimEnd('.');
                
                var subFolder = "";

                if (StartOfRoundPatch.ItemModMap.TryGetValue(grabbableObject.itemProperties, out var modTag))
                {
                    if (!modTag.Item1.Equals("Vanilla"))
                    {
                        var sanitizedMod = String.Join("_",
                                modTag.Item2.Split(Path.GetInvalidPathChars(), StringSplitOptions.RemoveEmptyEntries))
                            .TrimEnd('.');
                        
                        subFolder = $"{modTag.Item1}{Path.DirectorySeparatorChar}{sanitizedMod}";
                    }
                }
                
                texture.SavePNG(sanitizedName,
                    Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.PNG", subFolder));

                texture.SaveEXR(sanitizedName,
                    Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.EXR", subFolder));
            }

            var transparentCount = texture.GetTransparentCount();
            var totalPixels = texture.width * texture.height;
            var ratio = (float)transparentCount / (float)totalPixels;
            
            if (ratio <= PluginConfig.TransparencyRatio)
            {
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(texture.width / 2f, texture.height / 2f));
                sprite.name = $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                grabbableObject.itemProperties.itemIcon = sprite;
                RuntimeIcons.Log.LogInfo($"{grabbableObject.itemProperties.itemName} now has a new icon | 2");
            }
            else
            {
                RuntimeIcons.Log.LogError($"{grabbableObject.itemProperties.itemName} Generated {ratio*100}% Empty Sprite!");
            }

        }
        finally
        {
            stage.ResetStage();
        }
    }

    public static void FindOptimalRotation(StageComponent stage, GrabbableObject grabbable)
    {
        var pivotTransform = stage.PivotTransform;
        pivotTransform.rotation = Quaternion.identity;
        
        if (PluginConfig.RotationOverrides.TryGetValue(grabbable.itemProperties.itemName,
                out var rotations))
        {
            pivotTransform.Rotate(rotations, Space.World);
        }
        else
        {
            pivotTransform.rotation = Quaternion.identity;
            
            var executionOptions = new ExecutionOptions()
            {
                VertexCache = stage.VertexCache,
                CullingMask = stage.CullingMask,
                LogHandler = RuntimeIcons.VerboseMeshLog
            };
            
            if (!pivotTransform.TryGetBounds(out var bounds, executionOptions))
                throw new InvalidOperationException("This object has no Renders!");

            if (bounds.size == Vector3.zero)
                throw new InvalidOperationException("This object has no Bounds!");

            if (bounds.size.y < bounds.size.x / 2f && bounds.size.y <  bounds.size.z / 2f)
            {
                if (bounds.size.z < bounds.size.x * 0.5f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -45 y | 1");
                    pivotTransform.Rotate(Vector3.up, -45, Space.World);
                }
                else if (bounds.size.z < bounds.size.x * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -90 y | 2");
                    pivotTransform.Rotate(Vector3.up, -90, Space.World);
                }
                else if (bounds.size.x < bounds.size.z * 0.5f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -90 y | 3");
                    pivotTransform.Rotate(Vector3.up, -45, Space.World);
                }
                
                RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -80 x");
                pivotTransform.Rotate(Vector3.right, -80, Space.World);
                
                RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated 15 y");
                pivotTransform.Rotate(Vector3.up, 15, Space.World);
            }
            else
            {
                if (bounds.size.x < bounds.size.z * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -25 x | 1");
                    pivotTransform.Rotate(Vector3.right, -25, Space.World);
                    
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -45 y | 1");
                    pivotTransform.Rotate(Vector3.up, -45, Space.World);
                }
                else if ((Mathf.Abs(bounds.size.y - bounds.size.x) / bounds.size.x < 0.01f) && bounds.size.x < bounds.size.z * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -25 x | 2");
                    pivotTransform.Rotate(Vector3.right, -25, Space.World);
                    
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated 45 y | 2");
                    pivotTransform.Rotate(Vector3.up, 45, Space.World);
                }
                else if ((Mathf.Abs(bounds.size.y - bounds.size.z) / bounds.size.z < 0.01f) && bounds.size.z < bounds.size.x * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated 25 z | 3");
                    pivotTransform.Rotate(Vector3.forward, 25, Space.World);
                    
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -45 y | 3");
                    pivotTransform.Rotate(Vector3.up, -45, Space.World);
                }
                else if (bounds.size.y < bounds.size.x / 2f || bounds.size.x < bounds.size.y / 2f)
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated 45 z | 4");
                    pivotTransform.Rotate(Vector3.forward, 45, Space.World);
                    
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -25 x | 4");
                    pivotTransform.Rotate(Vector3.right, -25, Space.World);
                }
                else
                {
                    RuntimeIcons.Log.LogDebug($"{grabbable.itemProperties.itemName} rotated -25 x | 5");
                    pivotTransform.Rotate(Vector3.right, -25, Space.World);
                }
            }
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