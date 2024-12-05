using System;
using HarmonyLib;
using UnityEngine;
using VertexLibrary;
#if ENABLE_PROFILER_MARKERS
    using Unity.Profiling;
#endif

namespace RuntimeIcons.Patches;

[HarmonyPatch(typeof(MenuManager))]
internal static class MenuManagerPatch
{
    
    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker SearchItemsMarker = new("SearchItems");
        private static readonly ProfilerMarker CacheVertexesMarker = new("CacheVertexes");
    #endif

    [HarmonyFinalizer]
    [HarmonyPatch(nameof(MenuManager.Start))]
    private static void OnStart(MenuManager __instance)
    {
        #if ENABLE_PROFILER_MARKERS
            using var markerAuto = SearchItemsMarker.Auto();
        #endif
        
        try
        {
            //cache vertexes for all known items
            var items = Resources.FindObjectsOfTypeAll<Item>();

            RuntimeIcons.Log.LogWarning($"Caching vertexes for {items.Length} items!");
            foreach (var item in items)
            {
                var prefab = item.spawnPrefab;

                if (prefab)
                {
                    #if ENABLE_PROFILER_MARKERS
                        using var markerAuto2 = CacheVertexesMarker.Auto();
                    #endif
                    prefab.transform.CacheVertexes(new ExecutionOptions()
                    {
                        CullingMask = RuntimeIcons.RenderingStage.CullingMask,
                        LogHandler = RuntimeIcons.VerboseMeshLog,
                        VertexCache = RuntimeIcons.RenderingStage.VertexCache
                    });
                }
            }
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogFatal($"Exception while caching items: {ex}");
        }
    }
    
}