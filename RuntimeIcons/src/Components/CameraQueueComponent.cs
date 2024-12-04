using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using RuntimeIcons.Config;
using RuntimeIcons.Dotnet.Backports;
using RuntimeIcons.Patches;
using RuntimeIcons.Utils;
#if ENABLE_PROFILER_MARKERS
    using Unity.Profiling;
#endif
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RuntimeIcons.Components;

public class CameraQueueComponent : MonoBehaviour
{
    private readonly PriorityQueue<RenderingRequest, long> _renderingQueue = new (50);

    private readonly List<RenderingResult> _renderedItems = [];

    private Camera StageCamera { get; set; }
    internal StageComponent Stage { get; set; }

    private bool _isStaged = false;

    private void Start()
    {
        StageCamera = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    public bool EnqueueObject(GrabbableObject grabbableObject, Sprite errorSprite = null, long delay = 0)
    {
        if (!grabbableObject)
            throw new ArgumentNullException(nameof(grabbableObject));

        if (!errorSprite)
            errorSprite = RuntimeIcons.ErrorSprite;

        var queueElement = new RenderingRequest(grabbableObject, errorSprite);
        var key = queueElement.ItemKey;

        if (queueElement.HasIcon)
            return false;

        RuntimeIcons.Log.LogWarning($"Computing {key} icon");

        if (queueElement.OverrideHolder?.OverrideSprite)
        {
            grabbableObject.itemProperties.itemIcon = queueElement.OverrideHolder.OverrideSprite;
            HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
            RuntimeIcons.Log.LogInfo($"{key} now has a new icon from {queueElement.OverrideHolder.Source}");
            return true;
        }

        grabbableObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite;
        HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);

        _renderingQueue.Enqueue(queueElement, Time.frameCount + delay);

        return true;
    }

    private bool PullLastRender(RenderingResult render)
    {
        // Check if the fence has passed
        if (!render.FencePassed)
            return false;

        if (!UnpremultiplyAndCountTransparent.TryGetTransparentCount(render.ComputeID, out var transparentCount))
            return false;

        var texture = render.Texture;

        var totalPixels = texture.width * texture.height;
        var ratio = transparentCount / (float)totalPixels;

        var grabbableObject = render.Request.GrabbableObject;
        var key = render.Request.ItemKey;

        try
        {
            if (ratio < PluginConfig.TransparencyRatio)
            {
                var sprite = SpriteUtils.CreateSprite(texture);
                sprite.name = sprite.texture.name =
                    $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                
                grabbableObject.itemProperties.itemIcon = sprite;
                
                RuntimeIcons.Log.LogInfo($"{key} now has a new icon: {sprite.texture == texture}");
            }
            else
            {
                RuntimeIcons.Log.LogError($"{key} Generated {ratio * 100}% Empty Sprite!");
                grabbableObject.itemProperties.itemIcon = render.Request.ErrorSprite;
                Destroy(texture);
            }
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogError($"Error generating {key}\n{ex}");
            grabbableObject.itemProperties.itemIcon = render.Request.ErrorSprite;
        }

        HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
        return true;
    }

    private RenderingInstance? _nextRender;

    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker PrepareNextRenderMarker = new(nameof(PrepareNextRender));
    #endif

    private void PrepareNextRender()
    {
        #if ENABLE_PROFILER_MARKERS
            var markerAuto = PrepareNextRenderMarker.Auto();
        #endif

        _nextRender = null;

        var currentFrame = Time.frameCount;
        RenderingRequest? found = null;

        while (_renderingQueue.TryPeek(out _, out var targetFrame))
        {
            if (targetFrame > currentFrame)
                break;

            var candidateRequest = _renderingQueue.Dequeue();

            if (candidateRequest.GrabbableObject && !candidateRequest.GrabbableObject.isPocketed && !candidateRequest.HasIcon)
            {
                found = candidateRequest;
                break;
            }
        }

        if (found is null)
            return;

        var target = found.Value;

        try
        {
            //pre-compute transform and FOV

            RuntimeIcons.VerboseRenderingLog(LogLevel.Info,$"Computing stage for {target.ItemKey}");

            var renderSettings = new StageComponent.StageSettings(Stage, target);

            var (targetPosition, targetRotation) = Stage.CenterObjectOnPivot(renderSettings);

            RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"Item: offset {targetPosition} rotation {targetRotation}");

            var (_, stageRotation) = Stage.FindOptimalRotation(renderSettings);

            RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"Stage: rotation {stageRotation.eulerAngles}");

            var (cameraOffset, cameraFov) = Stage.ComputeCameraAngleAndFOV(renderSettings);

            RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"Camera Offset: {cameraOffset}");
            RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,
                $"Camera {(StageCamera.orthographic ? "orthographicSize" : "field of view")}: {cameraFov}");

            // Extract the image into a new texture without mipmaps
            var targetTexture = StageCamera.targetTexture;
            var texture = new Texture2D(targetTexture.width, targetTexture.height, targetTexture.graphicsFormat,
                mipCount: 1,
                TextureCreationFlags.DontInitializePixels)
            {
                name =
                    $"{nameof(RuntimeIcons)}.{target.GrabbableObject.itemProperties.itemName}Texture",
                filterMode = FilterMode.Point,
            };

            _nextRender = new RenderingInstance(target, renderSettings, texture);

            if (StageCamera.orthographic)
                StageCamera.orthographicSize = renderSettings.CameraFOV;
            else
                StageCamera.fieldOfView = renderSettings.CameraFOV;
            StageCamera.transform.localRotation = renderSettings.CameraRotation;

            StageCamera.enabled = true;
        }
        catch (Exception ex)
        {
            var key = target.ItemKey;
            _nextRender = null;
            RuntimeIcons.Log.LogError($"Error Computing {key}:\n{ex}");
        }
    }

    private void Update()
    {
        if (_renderedItems.Count > 0)
        {
            var itemToApply = _renderedItems[0];
            if (PullLastRender(itemToApply))
                _renderedItems.RemoveAt(0);
        }

        StageCamera.enabled = false;

        PrepareNextRender();
    }

    private StageComponent.IsolateStageLights _isolatorHolder;

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        CameraCleanup();

        //if it's not our stage camera do nothing
        if (camera != StageCamera)
            return;

        //if we have something to render
        if (_nextRender is null)
            return;

        var key = _nextRender.Value.Request.ItemKey;
        var settings = _nextRender.Value.Settings;

        try
        {
            //mark the item as `Rendered` ( LoadingSprite is considered invalid icon while LoadingSprite2 is considered a valid icon )
            //See RenderingRequest.HasIcon
            settings.TargetObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite2;

            RuntimeIcons.VerboseRenderingLog(LogLevel.Info,$"Setting stage for {key}");

            // Set the item on the stage and isolate lighting.
            Stage.SetStageFromSettings(_nextRender.Value.Settings);

            _isolatorHolder = new StageComponent.IsolateStageLights(settings.TargetObject.gameObject, Stage.LightGo);
            _isStaged = true;
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogError($"Error Rendering {key}\n{ex}");
        }
    }

    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker OnEndCameraMarker = new(nameof(OnEndCameraRendering));
        private static readonly ProfilerMarker DumpRenderMarker = new("Dump Icon Render");
    #endif

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        #if ENABLE_PROFILER_MARKERS
            using var markerAuto = OnEndCameraMarker.Auto();
        #endif

        CameraCleanup();

        //if it's not our stage camera do nothing
        if (camera != StageCamera)
            return;

        //if we have something to render
        if (_nextRender is null)
            return;

        var instance = _nextRender.Value;
        _nextRender = null;

        var cmd = CommandBufferPool.Get();
        var texture = camera.targetTexture;
        var transparentCountID = UnpremultiplyAndCountTransparent.Execute(cmd, texture);

        cmd.CopyTexture(texture, instance.Texture);
        var renderFence = cmd.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);

        if (PluginConfig.DumpToCache)
        {
            var targetItem = instance.Request.GrabbableObject.itemProperties;
            cmd.RequestAsyncReadback(instance.Texture, request =>
            {
                #if ENABLE_PROFILER_MARKERS
                    using var dumpMarkerAuto = DumpRenderMarker.Auto();
                #endif

                var rawData = request.GetData<byte>();

                var outputPath = CategorizeItemPatch.GetPathForItem(targetItem);
                var directory = Path.GetDirectoryName(outputPath) ?? "";
                var filename = Path.GetFileName(outputPath);

                var pngData = ImageConversion.EncodeNativeArrayToPNG(rawData, texture.graphicsFormat, (uint)texture.width, (uint)texture.height);
                var pngDirectory = Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.PNG", directory);
                var pngPath = Path.Combine(pngDirectory, filename + ".png");
                Directory.CreateDirectory(pngDirectory);
                File.WriteAllBytesAsync(pngPath, [.. pngData]);

                var exrData = ImageConversion.EncodeNativeArrayToEXR(rawData, texture.graphicsFormat, (uint)texture.width, (uint)texture.height);
                var exrDirectory = Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.EXR", directory);
                var exrPath = Path.Combine(exrDirectory, filename + ".exr");
                Directory.CreateDirectory(exrDirectory);
                File.WriteAllBytesAsync(exrPath, [.. exrData]);
            });
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        _renderedItems.Add(new RenderingResult(instance.Request, instance.Texture, renderFence, transparentCountID));
    }

    private void CameraCleanup()
    {
        if (_isStaged)
        {
            _isStaged = false;

            //cleanup
            Stage.ResetStage();

            //re-enable lights if we had an isolator active
            if (_isolatorHolder != null)
            {
                var holder = _isolatorHolder;
                _isolatorHolder = null;
                holder.Dispose();
            }
        }
    }
    
    internal readonly struct RenderingRequest
    {
        internal RenderingRequest(GrabbableObject grabbableObject, Sprite errorSprite)
        {
            GrabbableObject = grabbableObject;
            ErrorSprite = errorSprite;

            ItemKey = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
                .Replace(Path.DirectorySeparatorChar, '/');

            RuntimeIcons.OverrideMap.TryGetValue(ItemKey, out var overrideHolder);

            OverrideHolder = overrideHolder;
        }

        internal readonly GrabbableObject GrabbableObject;

        internal readonly Sprite ErrorSprite;

        internal readonly OverrideHolder OverrideHolder;

        internal readonly string ItemKey;

        internal bool HasIcon
        {
            get
            {
                var item = GrabbableObject.itemProperties;

                var inList = PluginConfig.ItemList.Contains(ItemKey);

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

                if (OverrideHolder?.OverrideSprite && item.itemIcon != OverrideHolder.OverrideSprite)
                    return false;

                return true;
            }
        }
    }

    private readonly struct RenderingInstance(RenderingRequest request, StageComponent.StageSettings settings, Texture2D texture)
    {
        internal readonly RenderingRequest Request = request;
        internal readonly StageComponent.StageSettings Settings = settings;
        internal readonly Texture2D Texture = texture;
    }

    private class RenderingResult(RenderingRequest request, Texture2D texture, GraphicsFence fence, int computeID)
    {
        private bool _fencePassed;

        internal bool FencePassed
        {
            get
            {
                if (_fencePassed)
                    return true;

                if (!Fence.passed)
                    return false;

                //cache the passed state so subsequent calls do not query the fence
                _fencePassed = true;
                return true;
            }
        }

        internal readonly RenderingRequest Request = request;

        internal readonly Texture2D Texture = texture;

        internal readonly GraphicsFence Fence = fence;

        internal readonly int ComputeID = computeID;
    }
}