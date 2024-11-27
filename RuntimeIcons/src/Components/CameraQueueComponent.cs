using System;
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
    internal PriorityQueue<RenderingElement, long> RenderingQueue { get; } = new (50);
    
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
        
        var queueElement = new RenderingElement(grabbableObject, errorSprite);
        var key = queueElement.ItemKey;

        if (ItemHasIcon(queueElement))
            return false;
        
        RuntimeIcons.Log.LogWarning($"Computing {key} icon");
        
        if (queueElement.OverrideHolder?.OverrideSprite)
        {
            RuntimeIcons.Log.LogWarning($"Using static icon from {queueElement.OverrideHolder.Source} for {key}");
            grabbableObject.itemProperties.itemIcon = queueElement.OverrideHolder.OverrideSprite;
            HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
            RuntimeIcons.Log.LogInfo($"{key} now has a new icon");
            return true;
        }
        
        grabbableObject.itemProperties.itemIcon = RuntimeIcons.LoadingSprite;
        HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);

        RenderingQueue.Enqueue(queueElement, Time.frameCount + delay);

        return true;
    }
    
    private RenderingElement? ToRender { get; set; }
    private StageSettings RenderSettings { get; set; }
    
    private void Update()
    {
        var currentFrame = Time.frameCount;
        
        if (ToRender.HasValue)
        {
            var targetElement = ToRender.Value;
            var grabbableObject = targetElement.GrabbableObject;
            var key = targetElement.ItemKey;
            var errorSprite = targetElement.ErrorSprite;

            try
            {
                //TODO enqueue compute shaders
                //TODO add callback to compute shader finish

                // Activate the temporary render texture
                var previouslyActiveRenderTexture = RenderTexture.active;

                var srcTexture = StageCamera.targetTexture;
                RenderTexture.active = srcTexture;

                // Extract the image into a new texture without mipmaps
                var texture = new Texture2D(srcTexture.width, srcTexture.height, GraphicsFormat.R16G16B16A16_SFloat,
                    1,
                    TextureCreationFlags.DontInitializePixels)
                {
                    name =
                        $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}Texture",
                    filterMode = FilterMode.Point,
                };

                texture.ReadPixels(new Rect(0, 0, srcTexture.width, srcTexture.height), 0, 0);
                texture.Apply();

                // Reactivate the previously active render texture
                RenderTexture.active = previouslyActiveRenderTexture;

                // Clean up after ourselves
                StageCamera.targetTexture = null;
                RenderTexture.ReleaseTemporary(srcTexture);

                // UnPremultiply the texture
                texture.UnPremultiply();
                texture.Apply();

                if (PluginConfig.DumpToCache)
                {
                    var outputPath = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties);
                    var directory = Path.GetDirectoryName(outputPath) ?? "";
                    var filename = Path.GetFileName(outputPath);

                    texture.SavePNG(filename,
                        Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.PNG", directory));

                    texture.SaveEXR(filename,
                        Path.Combine(Paths.CachePath, $"{nameof(RuntimeIcons)}.EXR", directory));
                }

                var transparentCount = texture.GetTransparentCount();
                var totalPixels = texture.width * texture.height;
                var ratio = (float)transparentCount / (float)totalPixels;

                if (ratio < PluginConfig.TransparencyRatio)
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(texture.width / 2f, texture.height / 2f));
                    sprite.name = sprite.texture.name =
                        $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                    grabbableObject.itemProperties.itemIcon = sprite;
                    RuntimeIcons.Log.LogInfo($"{key} now has a new icon");
                }
                else
                {
                    RuntimeIcons.Log.LogError($"{key} Generated {ratio * 100}% Empty Sprite!");
                    grabbableObject.itemProperties.itemIcon = errorSprite;
                    Destroy(texture);
                }
            }
            catch (Exception ex)
            {
                RuntimeIcons.Log.LogError($"Error generating {key}\n{ex}");
                grabbableObject.itemProperties.itemIcon = errorSprite;
            }

            HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
        }
        
        ToRender = null;
        RenderSettings = null;

        while (RenderingQueue.TryPeek(out var target, out var targetFrame))
        {
            if (targetFrame > currentFrame)
                break;

            var element = RenderingQueue.Dequeue();

            if (element.GrabbableObject&& !element.GrabbableObject.isPocketed && !ItemHasIcon(element) )
            {
                ToRender = element;
                break;
            }
        }

        //enable the camera if we have something to render
        StageCamera.enabled = ToRender.HasValue;
        
        if (!ToRender.HasValue)
            return;
        
        try
        {
            //pre-compute transform and FOV

            RuntimeIcons.Log.LogInfo($"Computing stage for {ToRender.Value.ItemKey}");

            RenderSettings = new StageSettings(ToRender.Value.GrabbableObject, ToRender.Value.OverrideHolder);

            var (targetPosition, targetRotation) = Stage.CenterObjectOnPivot(RenderSettings);

            RuntimeIcons.Log.LogInfo($"Item: offset {targetPosition} rotation {targetRotation}");

            var ( _, stageRotation) = Stage.FindOptimalRotation(RenderSettings);

            RuntimeIcons.Log.LogInfo($"Stage: rotation {stageRotation.eulerAngles}");

            var ( cameraOffset, cameraFov) = Stage.PrepareCameraForShot(RenderSettings);

            RuntimeIcons.Log.LogInfo($"Camera Offset: {cameraOffset}");
            RuntimeIcons.Log.LogInfo(
                $"Camera {(StageCamera.orthographic ? "orthographicSize" : "field of view")}: {cameraFov}");

            //initialize destination texture before the rendering loop
            Stage.NewCameraTexture();
        }
        catch (Exception ex)
        {
            var key = ToRender.Value.ItemKey;
            ToRender = null;
            RenderSettings = null;
            StageCamera.enabled = false;
            RuntimeIcons.Log.LogError($"Error Computing {key}:\n{ex}");
        }

    }

    private StageComponent.IsolateStageLights _isolatorHolder;
    
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        CameraCleanup();

        //if it's not our stage camera do nothing
        if (camera != StageCamera) 
            return;
        
        //if we have something to render
        if (!ToRender.HasValue) 
            return;
            
        var renderingTarget = ToRender.Value;
        var key = renderingTarget.ItemKey;
        
        try
        {
            RuntimeIcons.Log.LogInfo($"Setting stage for {key}");

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
    
    public struct RenderingElement
    {

        public RenderingElement(GrabbableObject grabbableObject, Sprite errorSprite)
        {
            GrabbableObject = grabbableObject;
            ErrorSprite = errorSprite;
            
            ItemKey = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
                .Replace(Path.DirectorySeparatorChar, '/');

            RuntimeIcons.OverrideMap.TryGetValue(ItemKey, out var overrideHolder);

            OverrideHolder = overrideHolder;
        }

        public GrabbableObject GrabbableObject { get; }

        public Sprite ErrorSprite { get; }

        public OverrideHolder OverrideHolder { get; }
        
        public string ItemKey { get; }
    }

    internal static bool ItemHasIcon(RenderingElement element)
    {
        var key = element.ItemKey;
        var item = element.GrabbableObject.itemProperties;

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

        if (element.OverrideHolder?.OverrideSprite && item.itemIcon != element.OverrideHolder.OverrideSprite)
            return false;
        
        return true;
    }
    
}