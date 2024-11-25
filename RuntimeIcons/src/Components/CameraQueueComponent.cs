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
    internal PriorityQueue<RenderingElement, long> RenderingQueue { get; } = new (50);
    internal List<RenderingResult> RenderResults { get; } = [];
    
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
    
    private void Update()
    {
        int i = 0;
        while (i < RenderResults.Count)
        {
            var curr = RenderResults[i];

            if (!curr.Fence.passed)
            {
                i++;
                continue;
            }
            
            RenderResults.RemoveAt(i);
            
            var grabbableObject = curr.Element.GrabbableObject;
            var key = curr.Element.ItemKey;
            var errorSprite = curr.Element.ErrorSprite;
            
            try
            {
                //TODO enqueue compute shaders
                //TODO add callback to compute shader finish

                // Activate the temporary render texture
                var previouslyActiveRenderTexture = RenderTexture.active;

                var srcTexture = curr.Texture;
                RenderTexture.active = srcTexture;

                // Extract the image into a new texture without mipmaps
                var texture = new Texture2D(srcTexture.width, srcTexture.height, GraphicsFormat.R16G16B16A16_SFloat, 1,
                    TextureCreationFlags.DontInitializePixels)
                {
                    name = $"{nameof(RuntimeIcons)}.{ToRender.Value.GrabbableObject.itemProperties.itemName}Texture",
                    filterMode = FilterMode.Point,
                };

                texture.ReadPixels(new Rect(0, 0, srcTexture.width, srcTexture.height), 0, 0);
                texture.Apply();

                // Reactivate the previously active render texture
                RenderTexture.active = previouslyActiveRenderTexture;

                // Clean up after ourselves
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

                if (ratio <= PluginConfig.TransparencyRatio)
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(texture.width / 2f, texture.height / 2f));
                    sprite.name = sprite.texture.name =
                        $"{nameof(RuntimeIcons)}.{grabbableObject.itemProperties.itemName}";
                    grabbableObject.itemProperties.itemIcon = sprite;
                    RuntimeIcons.Log.LogInfo($"{key} now has a new icon | 2");
                }
                else
                {
                    RuntimeIcons.Log.LogError($"{key} Generated {ratio * 100}% Empty Sprite!");
                    grabbableObject.itemProperties.itemIcon = errorSprite;
                    Destroy(texture);
                }
            } catch (Exception ex){
                RuntimeIcons.Log.LogError($"Error generating {key}\n{ex}");
                grabbableObject.itemProperties.itemIcon = errorSprite;
            }

            HudUtils.UpdateIconsInHUD(grabbableObject.itemProperties);
        }
        
        var currentFrame = Time.frameCount;
        ToRender = null;

        while (RenderingQueue.TryPeek(out var target, out var targetFrame))
        {
            RuntimeIcons.Log.LogFatal($"Peek {target.ItemKey} target {targetFrame} current {currentFrame}");
            if (targetFrame > currentFrame)
                break;

            var element = RenderingQueue.Dequeue();
            RuntimeIcons.Log.LogFatal($"Poll {target.ItemKey}");

            if (element.GrabbableObject && !ItemHasIcon(element))
            {
                RuntimeIcons.Log.LogFatal($"ToRender {target.ItemKey}");
                ToRender = element;
                break;
            }
        }

        //enable the camera if we have something to render
        StageCamera.enabled = ToRender.HasValue;
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
        var grabbableObject = renderingTarget.GrabbableObject;
        var overrideHolder = renderingTarget.OverrideHolder;
        var key = renderingTarget.ItemKey;

        RuntimeIcons.Log.LogFatal($"Rendering {key}");
        
        try
        {
            var destTexture = RenderTexture.GetTemporary(
                Stage.Resolution.x,
                Stage.Resolution.y, 8, GraphicsFormat.R16G16B16A16_SFloat);
            StageCamera.targetTexture = destTexture;
            
            Quaternion rotation;
            if (overrideHolder is { ItemRotation: not null })
            {
                rotation = Quaternion.Euler(overrideHolder.ItemRotation.Value + new Vector3(0, 90f, 0));
            }
            else
            {
                rotation = Quaternion.Euler(grabbableObject.itemProperties.restingRotation.x,
                    grabbableObject.itemProperties.floorYOffset + 90f,
                    grabbableObject.itemProperties.restingRotation.z);
            }

            RuntimeIcons.Log.LogInfo($"Setting stage for {key}");

            Stage.SetObjectOnStage(grabbableObject);

            Stage.CenterObjectOnPivot(rotation);

            RuntimeIcons.Log.LogInfo(
                $"StagedObject offset {Stage.StagedTransform.localPosition} rotation {Stage.StagedTransform.localRotation.eulerAngles}");

            if (overrideHolder is { StageRotation: not null })
            {
                Stage.PivotTransform.rotation = Quaternion.Euler(overrideHolder.StageRotation.Value);
            }
            else
            {
                Stage.FindOptimalRotation();
            }

            RuntimeIcons.Log.LogInfo($"Stage rotation {Stage.PivotTransform.rotation.eulerAngles}");

            Stage.PrepareCameraForShot();

            _isolatorHolder = new StageComponent.IsolateStageLights(Stage.PivotGo);

            //Turn on the stage Lights
            Stage.LightGo.SetActive(true);
        }
        catch (Exception ex)
        {
            RuntimeIcons.Log.LogError($"Error Rendering {key}\n{ex}");
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        CameraCleanup();

        if (camera == StageCamera && ToRender.HasValue)
        {
            var cmd = new CommandBuffer();
            var fence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);

            RenderResults.Add(new RenderingResult(ToRender.Value, fence, StageCamera.targetTexture));
            
            context.ExecuteCommandBuffer(cmd);
        }
    }

    private void CameraCleanup()
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
            //Turn off the stage Lights
            Stage.LightGo.SetActive(false);
        }
    }
    
    public struct RenderingElement
    {
        private readonly GrabbableObject _grabbableObject;
        private readonly Sprite _errorSprite;
        private readonly OverrideHolder _overrideHolder;
        private readonly string _itemKey;

        public RenderingElement(GrabbableObject grabbableObject, Sprite errorSprite)
        {
            _grabbableObject = grabbableObject;
            _errorSprite = errorSprite;
            
            _itemKey = CategorizeItemPatch.GetPathForItem(grabbableObject.itemProperties)
                .Replace(Path.DirectorySeparatorChar, '/');

            RuntimeIcons.OverrideMap.TryGetValue(_itemKey, out _overrideHolder);
        }

        public GrabbableObject GrabbableObject => _grabbableObject;

        public Sprite ErrorSprite => _errorSprite;
        public OverrideHolder OverrideHolder => _overrideHolder;
        
        public string ItemKey => _itemKey;
        
    }
    
    public struct RenderingResult
    {
        private RenderingElement _element;
        private GraphicsFence _fence;
        private RenderTexture _texture;

        public RenderingResult(RenderingElement element, GraphicsFence fence, RenderTexture texture)
        {
            _element = element;
            _fence = fence;
            _texture = texture;
        }

        public RenderingElement Element => _element;

        public GraphicsFence Fence => _fence;

        public RenderTexture Texture => _texture;
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