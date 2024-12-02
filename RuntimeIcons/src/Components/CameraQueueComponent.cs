using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using RuntimeIcons.Config;
using RuntimeIcons.Dotnet.Backports;
using RuntimeIcons.Patches;
using RuntimeIcons.Utils;
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
            RuntimeIcons.Log.LogDebug($"{key} now has a new icon from {queueElement.OverrideHolder.Source}");
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
                // Use SpriteMeshType.FullRect, as Unity apparently gets very confused when creating a tight mesh
                // around our generated texture at runtime, cutting it off on the top or the bottom.
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(texture.width / 2f, texture.height / 2f), 100f, 0u, SpriteMeshType.FullRect);
                sprite.name = sprite.texture.name =
                    $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                grabbableObject.itemProperties.itemIcon = sprite;
                RuntimeIcons.Log.LogDebug($"{key} now has a new icon: {sprite.texture == texture}");
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

    private RenderingInstance _nextRender;

    private void PrepareNextRender()
    {
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

            RuntimeIcons.Log.LogDebug($"Computing stage for {target.ItemKey}");

            var renderSettings = new StageSettings(target);

            var (targetPosition, targetRotation) = Stage.CenterObjectOnPivot(renderSettings);

            RuntimeIcons.Log.LogDebug($"Item: offset {targetPosition} rotation {targetRotation}");

            var (_, stageRotation) = Stage.FindOptimalRotation(renderSettings);

            RuntimeIcons.Log.LogDebug($"Stage: rotation {stageRotation.eulerAngles}");

            var (cameraOffset, cameraFov) = Stage.PrepareCameraForShot(renderSettings);

            RuntimeIcons.Log.LogDebug($"Camera Offset: {cameraOffset}");
            RuntimeIcons.Log.LogDebug(
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
            var pullStartTime = Time.realtimeSinceStartupAsDouble;

            var itemToApply = _renderedItems[0];
            if (PullLastRender(itemToApply))
                _renderedItems.RemoveAt(0);

            var pullTime = Time.realtimeSinceStartupAsDouble - pullStartTime;
            RuntimeIcons.Log.LogInfo($"{Time.frameCount}: Pulling count and creating sprite for {itemToApply.Request.ItemKey} took {pullTime * 1_000_000} microseconds");
        }

        StageCamera.enabled = false;

        var prepareStartTime = Time.realtimeSinceStartupAsDouble;

        PrepareNextRender();

        var prepareTime = Time.realtimeSinceStartupAsDouble - prepareStartTime;
        if (_nextRender != null)
            RuntimeIcons.Log.LogInfo($"{Time.frameCount}: Preparing to render {_nextRender.Request.ItemKey} took {prepareTime * 1_000_000} microseconds");
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

        var key = _nextRender.Request.ItemKey;
        var settings = _nextRender.Settings;

        try
        {
            //mark the item as `Rendered` ( LoadingSprite is considered invalid icon while LoadingSprite2 is considered a valid icon )
            //See RenderingRequest.HasIcon
            settings.TargetObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite2;

            RuntimeIcons.Log.LogDebug($"Setting stage for {key}");

            Stage.SetStageFromSettings(_nextRender.Settings);

            _isolatorHolder = new StageComponent.IsolateStageLights(settings.TargetObject.gameObject, Stage.LightGo);
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogError($"Error Rendering {key}\n{ex}");
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        CameraCleanup();

        if (camera != StageCamera)
            return;

        var cmd = new CommandBuffer();
        var texture = camera.targetTexture;
        var transparentCountID = UnpremultiplyAndCountTransparent.Execute(cmd, texture);

        cmd.CopyTexture(texture, _nextRender.Texture);
        var renderFence = cmd.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);

        if (PluginConfig.DumpToCache)
        {
            var targetItem = _nextRender.Request.GrabbableObject.itemProperties;
            cmd.RequestAsyncReadback(_nextRender.Texture, request =>
            {
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

        _renderedItems.Add(new RenderingResult(_nextRender.Request, _nextRender.Texture, renderFence, transparentCountID));
        _nextRender = null;
    }

    private void CameraCleanup()
    {
        try
        {
            //cleanup
            if (Stage.StagedTransform)
            {
                Stage.ResetStage();
            }

            //re-enable lights if we had an isolator active
            if (_isolatorHolder != null)
            {
                _isolatorHolder.Dispose();
                _isolatorHolder = null;
            }
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogFatal($"Exception Resetting Stage: \n{ex}");
        }
    }

    private class RenderingInstance(RenderingRequest request, StageSettings settings, Texture2D texture)
    {
        public readonly RenderingRequest Request = request;
        public readonly StageSettings Settings = settings;
        public readonly Texture2D Texture = texture;
    }

    private class RenderingResult(RenderingRequest request, Texture2D texture, GraphicsFence fence, int computeID)
    {
        private bool _fencePassed;

        public bool FencePassed
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

        public readonly RenderingRequest Request = request;

        public readonly Texture2D Texture = texture;

        public readonly GraphicsFence Fence = fence;

        public readonly int ComputeID = computeID;
    }
}