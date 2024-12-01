using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using RuntimeIcons.Config;
using RuntimeIcons.Dotnet.Backports;
using RuntimeIcons.Patches;
using RuntimeIcons.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RuntimeIcons.Components;

public class CameraQueueComponent : MonoBehaviour
{
    private readonly PriorityQueue<RenderingElement, long> _renderingQueue = new (50);

    private readonly List<RenderingElement> _renderedItems = [];

    private Camera StageCamera { get; set; }
    internal StageComponent Stage { get; set; }

    private void Start()
    {
        StageCamera = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private class RenderingElement
    {
        internal readonly GrabbableObject _grabbableObject;

        internal readonly Sprite _errorSprite;

        internal readonly string _itemKey;
        internal readonly OverrideHolder _override;

        internal Texture2D _texture;
        internal GraphicsFence? _renderFence;
        internal int _transparentCountID;

        public RenderingElement(GrabbableObject grabbableObject, Sprite errorSprite)
        {
            _grabbableObject = grabbableObject;
            _errorSprite = errorSprite;

            _itemKey = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
                .Replace(Path.DirectorySeparatorChar, '/');

            _override = RuntimeIcons.OverrideMap.GetValueOrDefault(_itemKey);
        }
    }

    public bool EnqueueObject(GrabbableObject grabbableObject, Sprite errorSprite = null, long delay = 0)
    {
        if (!grabbableObject)
            throw new ArgumentNullException(nameof(grabbableObject));
        
        if (!errorSprite)
            errorSprite = RuntimeIcons.ErrorSprite;
        
        var queueElement = new RenderingElement(grabbableObject, errorSprite);
        var key = queueElement._itemKey;

        if (ItemHasIcon(queueElement))
            return false;
        
        RuntimeIcons.Log.LogWarning($"Computing {key} icon");
        
        if (queueElement._override?.OverrideSprite)
        {
            grabbableObject.itemProperties.itemIcon = queueElement._override.OverrideSprite;
            HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
            RuntimeIcons.Log.LogDebug($"{key} now has a new icon from {queueElement._override.Source}");
            return true;
        }
        
        grabbableObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite;
        HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);

        _renderingQueue.Enqueue(queueElement, Time.frameCount + delay);

        return true;
    }

    private bool PullLastRender(RenderingElement render)
    {
        // Check if the fence has passed, and if it has, remove the fence so that
        // subsequent calls pass this check without querying the fence.
        if (render._renderFence.HasValue && !render._renderFence.Value.passed)
            return false;
        render._renderFence = null;

        if (!UnpremultiplyAndCountTransparent.TryGetTransparentCount(render._transparentCountID, out var transparentCount))
            return false;

        var texture = render._texture;

        var totalPixels = texture.width * texture.height;
        var ratio = transparentCount / (float)totalPixels;

        var grabbableObject = render._grabbableObject;
        var key = render._itemKey;

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
                grabbableObject.itemProperties.itemIcon = render._errorSprite;
                Destroy(texture);
            }
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogError($"Error generating {key}\n{ex}");
            grabbableObject.itemProperties.itemIcon = render._errorSprite;
        }

        HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
        return true;
    }

    private StageSettings RenderSettings { get; set; }
    private RenderingElement _nextRender = null;

    private void PrepareForNextRender()
    {
        var currentFrame = Time.frameCount;

        while (_renderingQueue.TryPeek(out _, out var targetFrame))
        {
            if (targetFrame > currentFrame)
                break;

            var toRender = _renderingQueue.Dequeue();

            if (toRender._grabbableObject && !toRender._grabbableObject.isPocketed && !ItemHasIcon(toRender))
            {
                _nextRender = toRender;
                break;
            }
        }

        if (_nextRender is null)
            return;

        try
        {
            //pre-compute transform and FOV

            RuntimeIcons.Log.LogDebug($"Computing stage for {_nextRender._itemKey}");

            RenderSettings = new StageSettings(_nextRender._grabbableObject, _nextRender._override);

            var (targetPosition, targetRotation) = Stage.CenterObjectOnPivot(RenderSettings);

            RuntimeIcons.Log.LogDebug($"Item: offset {targetPosition} rotation {targetRotation}");

            var (_, stageRotation) = Stage.FindOptimalRotation(RenderSettings);

            RuntimeIcons.Log.LogDebug($"Stage: rotation {stageRotation.eulerAngles}");

            var (cameraOffset, cameraFov) = Stage.PrepareCameraForShot(RenderSettings);

            RuntimeIcons.Log.LogDebug($"Camera Offset: {cameraOffset}");
            RuntimeIcons.Log.LogDebug(
                $"Camera {(StageCamera.orthographic ? "orthographicSize" : "field of view")}: {cameraFov}");

            // Extract the image into a new texture without mipmaps
            var targetTexture = StageCamera.targetTexture;
            _nextRender._texture = new Texture2D(targetTexture.width, targetTexture.height, targetTexture.graphicsFormat,
                mipCount: 1,
                TextureCreationFlags.DontInitializePixels)
            {
                name =
                    $"{nameof(RuntimeIcons)}.{RenderSettings.TargetObject.itemProperties.itemName}Texture",
                filterMode = FilterMode.Point,
            };

            StageCamera.enabled = true;
        }
        catch (Exception ex)
        {
            var key = _nextRender._itemKey;
            _nextRender = null;
            RenderSettings = null;
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
            RuntimeIcons.Log.LogInfo($"{Time.frameCount}: Pulling count and creating sprite for {itemToApply._itemKey} took {pullTime * 1_000_000} microseconds");
        }

        StageCamera.enabled = false;

        RenderSettings = null;

        var prepareStartTime = Time.realtimeSinceStartupAsDouble;

        PrepareForNextRender();

        var prepareTime = Time.realtimeSinceStartupAsDouble - prepareStartTime;
        if (_nextRender != null)
            RuntimeIcons.Log.LogInfo($"{Time.frameCount}: Preparing to render {_nextRender._itemKey} took {prepareTime * 1_000_000} microseconds");
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

        var key = _nextRender._itemKey;

        try
        {
            _nextRender._grabbableObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite2;

            RuntimeIcons.Log.LogDebug($"Setting stage for {key}");

            Stage.SetStageFromSettings(RenderSettings);

            _isolatorHolder = new StageComponent.IsolateStageLights(RenderSettings.TargetObject.gameObject, Stage.LightGo);
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
        _nextRender._transparentCountID = UnpremultiplyAndCountTransparent.Execute(cmd, texture);

        cmd.CopyTexture(texture, _nextRender._texture);
        _nextRender._renderFence = cmd.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);

        if (PluginConfig.DumpToCache)
        {
            var outputPath = CategorizeItemPatch.GetPathForItem(_nextRender._grabbableObject.itemProperties);
            cmd.RequestAsyncReadback(_nextRender._texture, request =>
            {
                var rawData = request.GetData<half>();
                var saveTexture = new Texture2D(texture.width, texture.height, texture.graphicsFormat, TextureCreationFlags.DontUploadUponCreate);
                saveTexture.SetPixelData(rawData, 0);

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

        _renderedItems.Add(_nextRender);
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
        }catch (Exception ex)
        {
            RuntimeIcons.Log.LogFatal($"Exception Resetting Stage: \n{ex}");
        }
    }

    private static bool ItemHasIcon(RenderingElement element)
    {
        var key = element._itemKey;
        var item = element._grabbableObject.itemProperties;

        var inList = PluginConfig.ItemList.Contains(key);
        
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

        if (element._override?.OverrideSprite && item.itemIcon != element._override.OverrideSprite)
            return false;
        
        return true;
    }
    
}