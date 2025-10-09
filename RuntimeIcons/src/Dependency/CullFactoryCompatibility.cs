using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using CullFactory.Behaviours.API;
using UnityEngine;

namespace RuntimeIcons.Dependency;

internal class CullFactoryCompatibility
{
    public const string GUID = "com.fumiko.CullFactory";
    public const string VERSION = "1.4.0";

    private static bool? _enabled;

    public static bool Enabled
    {
        get
        {
            if (!_enabled.HasValue)
            {
                if (Chainloader.PluginInfos.TryGetValue(GUID, out var mod) &&
                    mod.Metadata.Version >= Version.Parse(VERSION))
                    _enabled = true;
                else
                    _enabled = false;
            }
            return _enabled.Value;
        }
    }

    public static void DisableCullingForCamera(Camera camera)
    {
        if (!Enabled)
            return;

        DisableCullingForCameraImpl(camera);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void DisableCullingForCameraImpl(Camera camera)
    {
        var cullingOptions = camera.gameObject.AddComponent<CameraCullingOptions>();
        cullingOptions.DisableCulling = true;
    }
}
