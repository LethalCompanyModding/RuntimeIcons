using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RuntimeIcons.Patches;

[HarmonyPatch(typeof(HDRenderPipeline))]
internal static class HDRenderPipelinePatch
{
    internal static Action<ScriptableRenderContext, Camera> beginCameraRendering;

#if ENABLE_PROFILER_MARKERS
    private static readonly ProfilerMarker CameraCullingMarker = new("Cull");
    private static readonly ProfilerMarker CameraRenderingMarker = new("Render");
#endif

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(HDRenderPipeline.Render), [typeof(ScriptableRenderContext), typeof(List<Camera>)])]
    private static IEnumerable<CodeInstruction> AddCameraRenderHook(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions)
            .MatchForward(true, [
                new CodeMatch(insn => insn.IsLdloc()),
                new CodeMatch(insn => insn.IsLdloc()),
                new CodeMatch(OpCodes.Callvirt, typeof(List<HDRenderPipeline.RenderRequest>).GetMethod("get_Item", [typeof(int)])),
                new CodeMatch(insn => insn.opcode == OpCodes.Stloc || insn.opcode == OpCodes.Stloc_S),
            ]);

        if (matcher.IsInvalid)
        {
            RuntimeIcons.Log.LogError($"Failed to hook into HDRP to render icons with transparency.");
            RuntimeIcons.Log.LogError($"RuntimeIcons will not function correctly.");
            return instructions;
        }

        var loadRenderRequest = new CodeInstruction(matcher.Instruction.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, matcher.Instruction.operand);
        matcher
            .Advance(1)
            .Insert([
                new CodeInstruction(loadRenderRequest),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, typeof(HDRenderPipelinePatch).GetMethod(nameof(BeginCameraRenderingHook), BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(HDRenderPipeline.RenderRequest), typeof(ScriptableRenderContext)], null)),
            ]);

#if ENABLE_PROFILER_MARKERS
        matcher
            .Insert([
                new CodeInstruction(OpCodes.Ldsflda, typeof(HDRenderPipelinePatch).GetField(nameof(CameraRenderingMarker), BindingFlags.NonPublic | BindingFlags.Static)),
                new CodeInstruction(loadRenderRequest),
                new CodeInstruction(OpCodes.Ldfld, typeof(HDRenderPipeline.RenderRequest).GetField(nameof(HDRenderPipeline.RenderRequest.hdCamera))),
                new CodeInstruction(OpCodes.Ldfld, typeof(HDCamera).GetField(nameof(HDCamera.camera))),
                new CodeInstruction(OpCodes.Call, typeof(ProfilerMarker).GetMethod(nameof(ProfilerMarker.Begin), [typeof(UnityEngine.Object)])),
            ]);

        matcher
            .Start()
            .MatchForward(false, [
                new CodeMatch(insn => (insn.opcode == OpCodes.Ldloc || insn.opcode == OpCodes.Ldloc_S) && ((LocalBuilder)insn.operand).LocalIndex == 16),
                new CodeMatch(OpCodes.Ldnull),
                new CodeMatch(OpCodes.Call, typeof(UnityEngine.Object).GetMethod("op_Equality")),
                new CodeMatch(OpCodes.Brtrue),
            ]);

        var cameraLocal = (LocalBuilder)matcher.Instruction.operand;

        matcher
            .Advance(3);
        var label = (Label)matcher.Instruction.operand;

        matcher
            .Advance(1)
            .Insert([
                new CodeInstruction(OpCodes.Ldsflda, typeof(HDRenderPipelinePatch).GetField(nameof(CameraCullingMarker), BindingFlags.NonPublic | BindingFlags.Static)),
                new CodeInstruction(OpCodes.Ldloc, cameraLocal),
                new CodeInstruction(OpCodes.Call, typeof(ProfilerMarker).GetMethod(nameof(ProfilerMarker.Begin), [typeof(UnityEngine.Object)])),
            ]);

        matcher
            .MatchForward(false, [
                new CodeMatch(insn => insn.labels.Contains(label)),
            ])
            .Insert([
                new CodeInstruction(OpCodes.Ldsflda, typeof(HDRenderPipelinePatch).GetField(nameof(CameraCullingMarker), BindingFlags.NonPublic | BindingFlags.Static)),
                new CodeInstruction(OpCodes.Call, typeof(ProfilerMarker).GetMethod(nameof(ProfilerMarker.End), [])),
            ]);

        matcher
            .MatchForward(true, [
                new CodeMatch(OpCodes.Call, typeof(ScriptableRenderContext).GetMethod(nameof(ScriptableRenderContext.Submit))),
            ])
            .Advance(1)
            .Insert([
                new CodeInstruction(OpCodes.Ldsflda, typeof(HDRenderPipelinePatch).GetField(nameof(CameraRenderingMarker), BindingFlags.NonPublic | BindingFlags.Static)),
                new CodeInstruction(OpCodes.Call, typeof(ProfilerMarker).GetMethod(nameof(ProfilerMarker.End), [])),
            ]);
#endif

        return matcher.Instructions();
    }

    private static void BeginCameraRenderingHook(HDRenderPipeline.RenderRequest request, ScriptableRenderContext context)
    {
        beginCameraRendering?.Invoke(context, request.hdCamera.camera);
    }
}
