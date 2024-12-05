using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using RuntimeIcons.Config;
using RuntimeIcons.Dotnet.Backports;
using RuntimeIcons.Patches;
using RuntimeIcons.Utils;
#if ENABLE_PROFILER_MARKERS
    using UnityEngine.Profiling;
    using Unity.Profiling;
#endif
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RuntimeIcons.Components;

public class CameraQueueComponent : MonoBehaviour
{
    private readonly PriorityQueue<RenderingRequest, long> _renderingQueue = new (50);

    private readonly ConcurrentQueue<StageComponent.StageSettings> _toComputeQueue = new();
    private readonly ConcurrentQueue<StageComponent.StageSettings> _readyQueue = new();
    private readonly ConcurrentQueue<(RenderingRequest request, float opaqueRatio)> _doneQueue = new();
    private readonly ConcurrentQueue<Item> _toRetryQueue = new();

    private Thread _computingThread;
    private readonly EventWaitHandle _computingHandle = new ManualResetEvent(false);
    //this exists simply so we can rook at the values in UE
    private readonly ThreadMemory _computingMemory = new();

    private readonly List<RenderingResult> _renderedItems = [];

    internal Camera StageCamera;
    internal StageComponent Stage;

    private bool _isStaged = false;

    private void Start()
    {
        _computingThread = new Thread(ComputeThread)
        {
            Name = nameof(ComputeThread),
            IsBackground = true
        };
        _computingThread.Start();
        StageCamera = Stage.Camera;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void ComputeThread()
    {
        #if ENABLE_PROFILER_MARKERS
            Profiler.BeginThreadProfiling(nameof(RuntimeIcons), nameof(ComputeThread));
        #endif

        //this assignments are like this simply so we can rook at the values in UE
        HashSet<Item> readyItems = _computingMemory.ReadyItems; // = [];
        ConcurrentDictionary<Item, List<StageComponent.StageSettings>> alternativeRequests = _computingMemory.AlternativeRequests; // = [];

        RuntimeIcons.Log.LogWarning("Starting compute thread!");
        StageComponent.StageSettings toCompute = null;
        while (true)
        {
            try
            {
                //if we're not doing anything
                if (_toComputeQueue.IsEmpty && _toRetryQueue.IsEmpty && _doneQueue.IsEmpty)
                {
                    //wait for something
                    _computingHandle.Reset();
                    _computingHandle.WaitOne();
                }

                //forget completed items
                while (_doneQueue.TryDequeue(out var itemAndResult))
                {
                    var request = itemAndResult.request;

                    if (itemAndResult.opaqueRatio < PluginConfig.TransparencyRatio)
                        RuntimeIcons.Log.LogInfo($"{request.ItemKey} now has a new icon");
                    else
                        RuntimeIcons.Log.LogError($"{request.ItemKey} Generated {itemAndResult.opaqueRatio * 100:.#}% Empty Sprite!");

                    readyItems.Remove(request.Item);
                    alternativeRequests.Remove(request.Item, out _);
                }

                //check if we have to retry some item
                while (_toRetryQueue.TryDequeue(out var item))
                {
                    if (alternativeRequests.TryGetValue(item, out var list))
                    {
                        if (list.Count <= 0)
                        {
                            alternativeRequests.Remove(item, out _);
                        }
                        else
                        {
                            toCompute = list[0];
                            list.RemoveAt(0);
                            break;
                        }
                    }

                    readyItems.Remove(item);
                }

                //if we have nothing to retry, pull new requests
                if (toCompute is null)
                {
                    while (_toComputeQueue.TryDequeue(out var stageSettings))
                    {
                        var item = stageSettings.TargetRequest.Item;

                        if (!readyItems.Add(item))
                        {
                            alternativeRequests.GetOrAdd(item, _ => []).Add(stageSettings);
                        }
                        else
                        {
                            toCompute = stageSettings;
                            break;
                        }
                    }
                }

                if (toCompute is not null)
                {
                    var target = toCompute.TargetRequest;
                    try
                    {
                        //pre-compute transform and FOV

                        RuntimeIcons.VerboseRenderingLog(LogLevel.Info, $"Computing stage for {target.ItemKey}");

                        var (targetPosition, targetRotation) = Stage.CenterObjectOnPivot(toCompute);

                        RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,
                            $"Item: offset {targetPosition} rotation {targetRotation}");

                        var (_, stageRotation) = Stage.FindOptimalRotation(toCompute);

                        RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,
                            $"Stage: rotation {stageRotation.eulerAngles}");

                        var (cameraOffset, cameraFov) = Stage.ComputeCameraAngleAndFOV(toCompute);

                        RuntimeIcons.VerboseRenderingLog(LogLevel.Debug, $"Camera Offset: {cameraOffset}");
                        RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,
                            $"Camera {(toCompute.CameraOrthographic ? "orthographicSize" : "field of view")}: {cameraFov}");

                        _readyQueue.Enqueue(toCompute);
                    }
                    catch (Exception ex)
                    {
                        var key = target.ItemKey;
                        RuntimeIcons.Log.LogError($"Error Computing {key}:\n{ex}");
                    }
                    finally
                    {
                        toCompute = null;
                    }
                }
            }
            catch (Exception ex)
            {
                RuntimeIcons.Log.LogError($"Something went wrong computing stageSettings\n{ex}");
            }
            Thread.Yield();
        }
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

    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker PullLastRenderMarker = new(nameof(PullLastRender));
    #endif

    private bool PullLastRender(RenderingResult render)
    {
        #if ENABLE_PROFILER_MARKERS
            using var markerAuto = PullLastRenderMarker.Auto();
        #endif

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
            //notify thread this item is completed!
            _doneQueue.Enqueue((render.Request, ratio));
            _computingHandle.Set();

            if (ratio < PluginConfig.TransparencyRatio)
            {
                var sprite = SpriteUtils.CreateSprite(texture);
                sprite.name = sprite.texture.name =
                    $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";

                grabbableObject.itemProperties.itemIcon = sprite;
            }
            else
            {
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

        while (_renderingQueue.TryPeek(out _, out var targetFrame))
        {
            if (targetFrame > currentFrame)
                break;

            var candidateRequest = _renderingQueue.Dequeue();

            if (!candidateRequest.GrabbableObject || candidateRequest.GrabbableObject.isPocketed ||
                candidateRequest.HasIcon) continue;

            var renderSettings = new StageComponent.StageSettings(Stage, candidateRequest);
            _toComputeQueue.Enqueue(renderSettings);
            _computingHandle.Set();
        }

        StageComponent.StageSettings found = null;
        RenderingRequest request;

        while (_readyQueue.TryDequeue(out var stageSettings))
        {
            request = stageSettings.TargetRequest;
            if (request.GrabbableObject && !request.GrabbableObject.isPocketed && !request.HasIcon)
            {
                found = stageSettings;
                break;
            }
            else
            {
                //notify thread to retry another item of this type
                _toRetryQueue.Enqueue(request.Item);
                _computingHandle.Set();
            }
        }

        if (found is null)
        {
            StageCamera.enabled = false;
            return;
        }

        request = found.TargetRequest;

        RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{request.ItemKey} is the next render");

        // Extract the image into a new texture without mipmaps
        var targetTexture = StageCamera.targetTexture;
        var texture = new Texture2D(targetTexture.width, targetTexture.height, targetTexture.graphicsFormat,
            mipCount: 1,
            TextureCreationFlags.DontInitializePixels)
        {
            name =
                $"{nameof(RuntimeIcons)}.{request.GrabbableObject.itemProperties.itemName}Texture",
            filterMode = FilterMode.Point,
        };

        _nextRender = new RenderingInstance(request, found, texture);

        if (StageCamera.orthographic)
            StageCamera.orthographicSize = found.CameraFOV;
        else
            StageCamera.fieldOfView = found.CameraFOV;
        StageCamera.transform.localRotation = found.CameraRotation;

        StageCamera.enabled = true;


    }

    private void Update()
    {
        if (_renderedItems.Count > 0)
        {
            var itemToApply = _renderedItems[0];
            if (PullLastRender(itemToApply))
                _renderedItems.RemoveAt(0);
        }


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

        if (!settings.TargetObject)
        {
            //skip this frame
            _nextRender = null;
            //notify thread to retry this item type
            _toRetryQueue.Enqueue(settings.TargetRequest.Item);
            _computingHandle.Set();
            return;
        }

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
            Item = GrabbableObject.itemProperties;
            ErrorSprite = errorSprite;

            ItemKey = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
                .Replace(Path.DirectorySeparatorChar, '/');

            RuntimeIcons.OverrideMap.TryGetValue(ItemKey, out var overrideHolder);

            OverrideHolder = overrideHolder;
        }

        internal readonly GrabbableObject GrabbableObject;

        internal readonly Item Item;

        internal readonly Sprite ErrorSprite;

        internal readonly OverrideHolder OverrideHolder;

        internal readonly string ItemKey;

        internal bool HasIcon
        {
            get
            {
                var inList = PluginConfig.ItemList.Contains(ItemKey);

                if (PluginConfig.ItemListBehaviour switch
                    {
                        PluginConfig.ListBehaviour.BlackList => inList,
                        PluginConfig.ListBehaviour.WhiteList => !inList,
                        _ => false
                    })
                    return true;

                if (!Item.itemIcon)
                    return false;
                if (Item.itemIcon == RuntimeIcons.LoadingSprite)
                    return false;
                if (Item.itemIcon.name == "ScrapItemIcon")
                    return false;
                if (Item.itemIcon.name == "ScrapItemIcon2")
                    return false;

                if (OverrideHolder?.OverrideSprite && Item.itemIcon != OverrideHolder.OverrideSprite)
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

    //this exists simply so we can rook at the values in UE
    public struct ThreadMemory
    {
        public ThreadMemory()
        {
        }

        internal readonly HashSet<Item> ReadyItems = [];
        internal readonly ConcurrentDictionary<Item, List<StageComponent.StageSettings>> AlternativeRequests = [];

    }

}