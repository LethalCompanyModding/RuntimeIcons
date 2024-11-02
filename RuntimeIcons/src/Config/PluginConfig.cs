using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RuntimeIcons.Dependency;
using RuntimeIcons.Patches;
using UnityEngine;
using LogLevel = BepInEx.Logging.LogLevel;
using Object = UnityEngine.Object;

namespace RuntimeIcons.Config;

internal static class PluginConfig
{

    internal static LogLevel VerboseMeshLogs => _verboseMeshLogs.Value;
    internal static bool DumpToCache => _dumpToCache.Value;
    internal static float TransparencyRatio => _failPercentage.Value;
    internal static ISet<string> ItemList { get; private set; }
    internal static ListBehaviour ItemListBehaviour => _itemListBehaviourConfig.Value;

    private static ConfigEntry<ListBehaviour> _itemListBehaviourConfig;
    private static ConfigEntry<string> _itemListConfig;
    private static ConfigEntry<float> _failPercentage;
    private static ConfigEntry<LogLevel> _verboseMeshLogs;
    private static ConfigEntry<bool> _dumpToCache;

    internal static void Init()
    {
        var config = RuntimeIcons.INSTANCE.Config;
        //Initialize Configs

        _verboseMeshLogs = config.Bind("Debug", "Verbose Mesh Logs", LogLevel.None,"Print Extra logs!");
        _dumpToCache = config.Bind("Debug", "Dump sprites to cache", false,"Save the generated sprites into the cache folder");
        
        _itemListBehaviourConfig = config.Bind("Config", "List Behaviour", ListBehaviour.BlackList, "What mode to use to filter what items will get new icons");
                
        _itemListConfig = config.Bind("Config", "Item List", "Body,", "List of items to filter");
        
        _failPercentage = config.Bind("Config", "Transparency Threshold", 0.95f, new ConfigDescription("Maximum percentage of transparent pixels to consider a valid image", new AcceptableValueRange<float>(0f, 1f)));
        
        
        ParseBlacklist();
        _itemListConfig.SettingChanged += (_, _) => ParseBlacklist();
                

        if (LethalConfigProxy.Enabled)
        {
            LethalConfigProxy.AddConfig(_verboseMeshLogs);
            LethalConfigProxy.AddConfig(_dumpToCache);
            
            LethalConfigProxy.AddConfig(_itemListConfig);
            LethalConfigProxy.AddConfig(_itemListBehaviourConfig);
            LethalConfigProxy.AddConfig(_failPercentage);
                    
            LethalConfigProxy.AddButton("Debug", "Refresh Held Item", "Regenerate Sprite for held Item", "Refresh",
                () =>
                {
                    if (!StartOfRound.Instance)
                        return;
                            
                    if (!StartOfRound.Instance.localPlayerController.currentlyHeldObjectServer)
                        return;
                            
                    GrabbableObjectPatch.ComputeSprite(StartOfRound.Instance.localPlayerController.currentlyHeldObjectServer);
                    
                    GrabbableObjectPatch.UpdateIconsInHUD(StartOfRound.Instance.localPlayerController.currentlyHeldObjectServer.itemProperties);
                });
            LethalConfigProxy.AddButton("Debug", "Render All Loaded Items", "Finds all items in the resources of the game to render them. Must be in a game.", "Render All Items",
                () =>
                {
                    if (StartOfRound.Instance == null)
                        return;

                    StartOfRound.Instance.StartCoroutine(RenderAllCoroutine());

                });
        }
                
        CleanAndSave();
        
        RotationEditor.Init();
        
        return;

        void ParseBlacklist()
        {
            var items = _itemListConfig.Value.Split(",");

            ItemList = items.Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).ToHashSet();
        }
    }

    public enum ListBehaviour
    {
        None,
        BlackList,
        WhiteList
    }


    internal static void CleanAndSave()
    {
        var config = RuntimeIcons.INSTANCE.Config;
        //remove unused options
        var orphanedEntriesProp = AccessTools.Property(config.GetType(), "OrphanedEntries");

        var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp!.GetValue(config, null);

        orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
        config.Save(); // Save the config file
    }

    private static IEnumerator RenderAllCoroutine()
    {
        var items = Resources.FindObjectsOfTypeAll<Item>();
        var renderedItems = new HashSet<Item>();

        foreach (var item in items)
        {
            if (!item.spawnPrefab)
                continue;

            var originalIcon = item.itemIcon;
            item.itemIcon = null;

            var spawnedItem = Object.Instantiate(item.spawnPrefab);
            
            try
            {
                var grabbableObject = spawnedItem.GetComponentInChildren<GrabbableObject>();
                grabbableObject.Start();
                grabbableObject.Update();
                var animators = grabbableObject.GetComponentsInChildren<Animator>();
                foreach (var animator in animators)
                    animator.Update(Time.deltaTime);
                GrabbableObjectPatch.ComputeSprite(grabbableObject);
            }
            catch { }
            finally
            {
                Object.Destroy(spawnedItem);
            }

            if (item.itemIcon && item.itemIcon != RuntimeIcons.LoadingSprite)
                renderedItems.Add(item);

            item.itemIcon = originalIcon;
            
            yield return null;
        }

        var reportBuilder = new StringBuilder("Items that failed to render: ");
        var anyFailed = false;

        foreach (var item in items)
        {
            if (!renderedItems.Contains(item))
            {
                reportBuilder.Append(item.itemName);
                if (GrabbableObjectPatch.ItemHasIcon(item))
                    reportBuilder.Append(" (✓)");
                else
                    reportBuilder.Append(" (✗)");
                reportBuilder.Append(", ");
                anyFailed = true;
            }
        }

        if (anyFailed)
        {
            reportBuilder.Length -= 2;
            RuntimeIcons.Log.LogInfo(reportBuilder);
        }
        else
        {
            RuntimeIcons.Log.LogInfo("No items failed to render.");
        }
    }
    

}