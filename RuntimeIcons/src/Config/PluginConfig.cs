using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RuntimeIcons.Dependency;
using LogLevel = BepInEx.Logging.LogLevel;

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
                
        _itemListConfig = config.Bind("Config", "Item List", "", "List of items to filter\nExample: Vanilla/Big bolt, Unknown/Body");
        
        _failPercentage = config.Bind("Config", "Transparency Threshold", 0.98f, new ConfigDescription("Maximum percentage of transparent pixels to consider a valid image", new AcceptableValueRange<float>(0f, 1f)));
        
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
                    var startOfRound = StartOfRound.Instance;
                    if (!startOfRound)
                        return;
                            
                    if (!startOfRound.localPlayerController.currentlyHeldObjectServer)
                        return;

                    var heldItem = startOfRound.localPlayerController.currentlyHeldObjectServer;

                    var oldIcon = heldItem.itemProperties.itemIcon;
                    heldItem.itemProperties.itemIcon = null;

                    RuntimeIcons.RenderingStage.CameraQueue.EnqueueObject(heldItem, oldIcon);
                    
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
   

}