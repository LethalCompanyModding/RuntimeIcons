using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RuntimeIcons.Patches;

[HarmonyPatch(typeof(HDRenderPipeline))]
internal static class HDRenderPipelinePatch
{
    internal static Action<ScriptableRenderContext, Camera> beginCameraRendering;

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
                new(loadRenderRequest),
                new(OpCodes.Ldarg_1),
                new(OpCodes.Call, typeof(HDRenderPipelinePatch).GetMethod(nameof(BeginCameraRenderingHook), BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(HDRenderPipeline.RenderRequest), typeof(ScriptableRenderContext)], null)),
            ]);

        return matcher.Instructions();
    }

    private static void BeginCameraRenderingHook(HDRenderPipeline.RenderRequest request, ScriptableRenderContext context)
    {
        beginCameraRendering?.Invoke(context, request.hdCamera.camera);
    }
}
