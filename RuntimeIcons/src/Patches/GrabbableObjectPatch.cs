using HarmonyLib;

namespace RuntimeIcons.Patches;

[HarmonyPatch]
public static class GrabbableObjectPatch
{
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.Start))]
    private static void AfterStart(GrabbableObject __instance)
    {
        RuntimeIcons.CameraStage.CameraQueue.EnqueueObject(__instance, RuntimeIcons.WarningSprite, 2);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.EquipItem))]
    private static void OnGrab(GrabbableObject __instance)
    {
        if (__instance.playerHeldBy != GameNetworkManager.Instance.localPlayerController)
            return;
        
        if (__instance.itemProperties.itemIcon != RuntimeIcons.WarningSprite)
            return;
        
        RuntimeIcons.Log.LogInfo($"Attempting to refresh BrokenIcon for {__instance.itemProperties.itemName}!");
        
        __instance.itemProperties.itemIcon = null;
        RuntimeIcons.CameraStage.CameraQueue.EnqueueObject(__instance, RuntimeIcons.ErrorSprite);
    }
}