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
    private static bool _runOnce;

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
        
        if (_runOnce)
            return;
        _runOnce = true;

        try
        {
            //cache vertexes for all known items
            var items = Resources.FindObjectsOfTypeAll<GrabbableObject>();

            RuntimeIcons.Log.LogInfo($"Caching vertexes for {items.Length} items!");
            foreach (var item in items)
            {
                #if ENABLE_PROFILER_MARKERS
                    using var markerAuto2 = CacheVertexesMarker.Auto();
                #endif

                item.transform.CacheVertexes(new ExecutionOptions()
                {
                    CullingMask = RuntimeIcons.RenderingStage.CullingMask,
                    LogHandler = RuntimeIcons.VerboseMeshLog,
                    VertexCache = RuntimeIcons.RenderingStage.VertexCache
                });
            }
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogFatal($"Exception while caching items: {ex}");
        }
    }
    
}
