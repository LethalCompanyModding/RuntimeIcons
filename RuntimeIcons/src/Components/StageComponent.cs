using System;
using System.Collections.Generic;
using System.Linq;
using RuntimeIcons.Dependency;
using RuntimeIcons.Patches;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using VertexLibrary;
using VertexLibrary.Caches;
using static UnityEngine.Rendering.HighDefinition.RenderPipelineSettings;

namespace RuntimeIcons.Components;

public class StageComponent : MonoBehaviour
{
    internal CameraQueueComponent CameraQueue { get; private set; }
    private StageComponent(){}

    public IVertexCache VertexCache { get; set; } = VertexesExtensions.GlobalPartialCache;

    public GameObject LightGo { get; private set; }

    public GameObject PivotGo
    {
        get
        {
            if (!_targetGo)
                _targetGo = CreatePivotGo();
            return _targetGo;
        }
    }

    private GameObject CameraGo { get; set; }

    public Transform LightTransform => LightGo.transform;
    public Transform PivotTransform => PivotGo.transform;
    private Transform CameraTransform => CameraGo.transform;
    
    private Camera _camera;
    private GameObject _targetGo;
    private Vector2Int _resolution = new Vector2Int(128, 128);

    private ColorBufferFormat? _originalColorBufferFormat;

    public Vector2Int Resolution
    {
        get => _resolution;
        set
        {
            _resolution = value;
            _camera.aspect = (float)_resolution.x / _resolution.y;
        }
    }

    public Vector2 MarginPixels = new Vector2(0, 0);

    public int CullingMask => _camera.cullingMask;

    public Transform StagedTransform { get; private set; }
    public GrabbableObject StagedItem { get; private set; }
    
    private TransformMemory Memory { get;  set; }

    private GameObject CreatePivotGo()
    {
        //add the StageTarget
        var targetGo = new GameObject($"{transform.name}.Pivot")
        {
            transform =
            {
                position = transform.position,
                rotation = transform.rotation
            }
        };
        return targetGo;
    }

    public static StageComponent CreateStage(HideFlags hideFlags, int cameraLayerMask = 1, string stageName = "Stage", bool orthographic = false)
    {
        //create the root Object for the Stage
        var stageGo = new GameObject(stageName)
        {
            hideFlags = hideFlags
        };

        //add the component to the stage
        var stageComponent = stageGo.AddComponent<StageComponent>();

        //add the stage Lights
        
        var lightsGo = new GameObject("Stage Lights")
        {
            hideFlags = hideFlags,
            transform =
            {
                parent = stageGo.transform
            }
        };
        //disable the lights by default
        lightsGo.SetActive(false);
        stageComponent.LightGo = lightsGo;
        
        //add Camera
        var cameraGo = new GameObject("Camera")
        {
            hideFlags = hideFlags,
            transform =
            {
                parent = stageGo.transform
            }
        };
        stageComponent.CameraGo = cameraGo;

        // Add a Camera component to the GameObject
        var cam = cameraGo.AddComponent<Camera>();
        stageComponent._camera = cam;

        CullFactoryCompatibility.DisableCullingForCamera(cam);

        // Configure the Camera
        cam.cullingMask = cameraLayerMask;
        cam.orthographic = orthographic;
        cam.aspect = (float)stageComponent.Resolution.x / stageComponent.Resolution.y;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 10f;
        cam.enabled = false;

        var hdrpCam = cameraGo.AddComponent<HDAdditionalCameraData>();
        hdrpCam.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        hdrpCam.backgroundColorHDR = Color.clear;
        hdrpCam.customRenderingSettings = true;
        void SetOverride(FrameSettingsField setting, bool enabled)
        {
            hdrpCam.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)setting] = true;
            hdrpCam.renderingPathCustomFrameSettings.SetEnabled(setting, enabled);
        }
        SetOverride(FrameSettingsField.CustomPass, false);
        SetOverride(FrameSettingsField.CustomPostProcess, false);
        SetOverride(FrameSettingsField.Tonemapping, false);
        SetOverride(FrameSettingsField.ColorGrading, false);

        var cameraQueue = cameraGo.AddComponent<CameraQueueComponent>();
        cameraQueue.Stage = stageComponent;
        stageComponent.CameraQueue = cameraQueue;

        return stageComponent;
    }

    private void Awake()
    {
        HDRenderPipelinePatch.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }
    
    public void SetObjectOnStage(GrabbableObject targetItem)
    {
        var targetTransform = targetItem.transform;
        
        if (StagedTransform && StagedTransform != targetTransform)
            throw new InvalidOperationException("An Object is already on stage!");
        
        PivotTransform.parent = null;
        PivotTransform.position = Vector3.zero;
        PivotTransform.rotation = Quaternion.identity;
        SceneManager.MoveGameObjectToScene(PivotGo, targetTransform.gameObject.scene);
        
        StagedTransform = targetTransform;
        StagedItem = targetItem;
        
        Memory = new TransformMemory(StagedTransform);
        
        StagedTransform.SetParent(PivotTransform, false);
        StagedTransform.localPosition = Vector3.zero;
    }
    
    public void CenterObjectOnPivot(Quaternion? overrideRotation = null)
    {
        if (!StagedTransform)
            throw new InvalidOperationException("No Object on stage!");

        if (overrideRotation.HasValue)
            StagedTransform.rotation = overrideRotation.Value;
        
        var matrix = Matrix4x4.TRS(Vector3.zero, StagedTransform.rotation, StagedTransform.localScale);
        
        var executionOptions = new ExecutionOptions()
        {
            VertexCache = VertexCache,
            CullingMask = CullingMask,
            LogHandler = RuntimeIcons.VerboseMeshLog,
            OverrideMatrix = matrix
        };
        
        if (!StagedTransform.gameObject.TryGetBounds(out var bounds, executionOptions))
            throw new InvalidOperationException("This object has no Renders!");
        
        StagedTransform.localPosition = -bounds.center;
        
    }

    public void PrepareCameraForShot(Vector3 offset, float fov)
    {
        if (!StagedTransform)
            throw new InvalidOperationException("No Object on stage!");
        
        PivotTransform.position = _camera.transform.position + offset;
        LightTransform.position = PivotTransform.position;
        if (_camera.orthographic)
        {
            _camera.orthographicSize = fov;
        }
        else
        {
            _camera.fieldOfView = fov;
        }
    }
    
    public void PrepareCameraForShot()
    {
        if (!StagedTransform)
            throw new InvalidOperationException("No Object on stage!");

        PivotTransform.position = Vector3.zero;
        var executionOptions = new ExecutionOptions()
        {
            VertexCache = VertexCache,
            CullingMask = CullingMask,
            LogHandler = RuntimeIcons.VerboseMeshLog,
            OverrideMatrix = PivotTransform.localToWorldMatrix,
        };
        var worldToLocal = PivotTransform.worldToLocalMatrix;

        var vertices = PivotGo.GetVertexes(executionOptions);
        if (vertices.Length == 0)
            throw new InvalidOperationException("This object has no Renders!");

        var bounds = vertices.GetBounds();
        if (!bounds.HasValue)
            throw new InvalidOperationException("This object has no Bounds!");

        // Adjust the pivot so that the object doesn't clip into the near plane
        var distanceToCamera = Math.Max(_camera.nearClipPlane + bounds.Value.size.z, 3f);
        PivotTransform.position = _camera.transform.position - bounds.Value.center + _camera.transform.forward * distanceToCamera;

        // Calculate the camera size to fit the object being displayed
        Vector2 marginFraction = MarginPixels / _resolution;
        Vector2 fovScale = Vector2.one / (Vector2.one - marginFraction);

        if (_camera.orthographic)
        {
            var sizeY = bounds.Value.extents.y * fovScale.y;
            var sizeX = bounds.Value.extents.x * fovScale.x * _camera.aspect;
            var size = Math.Max(sizeX, sizeY);
            _camera.orthographicSize = size;
        }
        else
        {
            var matrix = PivotTransform.localToWorldMatrix * worldToLocal;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);

            const int iterations = 2;

            float angleMinX, angleMaxX;
            float angleMinY, angleMaxY;

            for (var i = 0; i < iterations; i++)
            {
                GetCameraAngles(_camera, CameraTransform.right, vertices, out angleMinY, out angleMaxY);
                _camera.transform.Rotate(Vector3.up, (angleMinY + angleMaxY) / 2, Space.World);

                GetCameraAngles(_camera, -CameraTransform.up, vertices, out angleMinX, out angleMaxX);
                _camera.transform.Rotate(Vector3.right, (angleMinX + angleMaxX) / 2, Space.Self);
            }

            GetCameraAngles(_camera, CameraTransform.right, vertices, out angleMinY, out angleMaxY);
            GetCameraAngles(_camera, -CameraTransform.up, vertices, out angleMinX, out angleMaxX);

            var fovAngleX = Math.Max(-angleMinX, angleMaxX) * 2 * fovScale.y;
            var fovAngleY = Camera.HorizontalToVerticalFieldOfView(Math.Max(-angleMinY, angleMaxY) * 2, _camera.aspect) * fovScale.x;
            _camera.fieldOfView = Math.Max(fovAngleX, fovAngleY);
        }

        LightTransform.position = PivotTransform.position;
    }

    public void ResetStage()
    {
        if (StagedTransform)
        {
            StagedTransform.SetParent(Memory.Parent, false);

            StagedTransform.localScale = Memory.LocalScale;
            StagedTransform.SetLocalPositionAndRotation(Memory.LocalPosition,Memory.LocalRotation);
        }

        StagedTransform = null;
        StagedItem = null;
        Memory = default;
        
        PivotTransform.parent = null;
        PivotTransform.position = transform.position;
        PivotTransform.rotation = Quaternion.identity;
        LightTransform.localPosition = Vector3.zero;
        LightTransform.rotation = Quaternion.identity;
        CameraTransform.localRotation = Quaternion.identity;
    }

    public Texture2D TakeSnapshot()
    {
        return TakeSnapshot(Color.clear);
    }
    
    public Texture2D TakeSnapshot(Color backgroundColor)
    {
        // Set the background color of the camera
        _camera.backgroundColor = backgroundColor;

        // Get a temporary render texture and render the camera

        var destTexture = RenderTexture.GetTemporary(Resolution.x, Resolution.y, 8, GraphicsFormat.R16G16B16A16_SFloat);
        _camera.targetTexture = destTexture;

        using (new IsolateStageLights(PivotGo))
        {
            //Turn on the stage Lights
            LightGo.SetActive(true);
            
            _camera.Render();
            
            //Turn off the stage Lights
            LightGo.SetActive(false);
        }

        // Activate the temporary render texture
        var previouslyActiveRenderTexture = RenderTexture.active;
        RenderTexture.active = destTexture;

        // Extract the image into a new texture without mipmaps
        var texture = new Texture2D(destTexture.width, destTexture.height, GraphicsFormat.R16G16B16A16_SFloat, 1, TextureCreationFlags.DontInitializePixels)
        {
            name = $"{nameof(RuntimeIcons)}.{StagedTransform.name}Texture",
            filterMode = FilterMode.Point,
        };
        
        texture.ReadPixels(new Rect(0, 0, destTexture.width, destTexture.height), 0, 0);
        texture.Apply();
        
        // Reactivate the previously active render texture
        RenderTexture.active = previouslyActiveRenderTexture;
        
        // Clean up after ourselves
        _camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(destTexture);
        
        RuntimeIcons.Log.LogInfo($"{texture.name} Rendered");
        // Return the texture
        return texture;
    }

    private void SetAlphaEnabled(bool enabled)
    {
        ref var settings = ref ((HDRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).m_RenderPipelineSettings;

        var prevFormat = settings.colorBufferFormat;

        if (!enabled)
        {
            if (_originalColorBufferFormat.HasValue)
            {
                settings.colorBufferFormat = _originalColorBufferFormat.Value;
                _originalColorBufferFormat = null;
            }
        }
        else
        {
            if (!_originalColorBufferFormat.HasValue)
                _originalColorBufferFormat = settings.colorBufferFormat;
            settings.colorBufferFormat = ColorBufferFormat.R16G16B16A16;
        }

        if (settings.colorBufferFormat != prevFormat)
        {
            var pipeline = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
            var alpha = settings.SupportsAlpha();
            pipeline.m_EnableAlpha = alpha && settings.postProcessSettings.supportsAlpha;
            pipeline.m_KeepAlpha = alpha;
        }
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _camera)
            return;

        SetAlphaEnabled(true);
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _camera)
            return;

        SetAlphaEnabled(false);
    }

    internal class IsolateStageLights : IDisposable
    {
        private readonly HashSet<Light> _lightMemory;
        private readonly Color _ambientLight;
        
        public IsolateStageLights(GameObject stagePivot)
        {
            _lightMemory = UnityEngine.Pool.HashSetPool<Light>.Get();

            _ambientLight = RenderSettings.ambientLight;
            RenderSettings.ambientLight = Color.black;

            var localLights = stagePivot.GetComponentsInChildren<Light>();

            var globalLights = FindObjectsOfType<Light>().Where(l => !localLights.Contains(l)).Where(l => l.enabled).ToArray();

            foreach (var light in globalLights)
            {
                light.enabled = false;
                _lightMemory.Add(light);
            }
        }

        public void Dispose()
        {
            RenderSettings.ambientLight = _ambientLight;
            
            foreach (var light in _lightMemory)
            {
                light.enabled = true;
            }

            UnityEngine.Pool.HashSetPool<Light>.Release(_lightMemory);
        }
    }

    public record struct TransformMemory
    {
        public readonly Transform Parent;
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;
        public readonly Vector3 LocalScale;

        public TransformMemory(Transform target)
        {
            this.Parent = target.parent;
            this.LocalPosition = target.localPosition;
            this.LocalRotation = target.localRotation;
            this.LocalScale = target.localScale;
        }
    }

    private static void GetCameraAngles(Camera camera, Vector3 direction, IEnumerable<Vector3> vertices, out float angleMin, out float angleMax)
    {
        var position = camera.transform.position;
        var forwardPlane = new Plane(camera.transform.forward, position);
        var directionPlane = new Plane(direction, position);
        var tangentMin = float.PositiveInfinity;
        var tangentMax = float.NegativeInfinity;

        foreach (var vertex in vertices)
        {
            var tangent = directionPlane.GetDistanceToPoint(vertex) / forwardPlane.GetDistanceToPoint(vertex);
            tangentMin = Math.Min(tangent, tangentMin);
            tangentMax = Math.Max(tangent, tangentMax);
        }

        angleMin = Mathf.Atan(tangentMin) * Mathf.Rad2Deg;
        angleMax = Mathf.Atan(tangentMax) * Mathf.Rad2Deg;
    }

    public void FindOptimalRotation()
    {
        var pivotTransform = PivotTransform;
        pivotTransform.rotation = Quaternion.identity;
        
        pivotTransform.rotation = Quaternion.identity;
        
        var executionOptions = new ExecutionOptions()
        {
            VertexCache = VertexCache,
            CullingMask = CullingMask,
            LogHandler = RuntimeIcons.VerboseMeshLog
        };
        
        if (!pivotTransform.TryGetBounds(out var bounds, executionOptions))
            throw new InvalidOperationException("This object has no Renders!");

        if (bounds.size == Vector3.zero)
            throw new InvalidOperationException("This object has no Bounds!");

        if (bounds.size.y < bounds.size.x / 2f && bounds.size.y <  bounds.size.z / 2f)
        {
            if (bounds.size.z < bounds.size.x * 0.5f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -45 y | 1");
                pivotTransform.Rotate(Vector3.up, -45, Space.World);
            }
            else if (bounds.size.z < bounds.size.x * 0.85f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -90 y | 2");
                pivotTransform.Rotate(Vector3.up, -90, Space.World);
            }
            else if (bounds.size.x < bounds.size.z * 0.5f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -90 y | 3");
                pivotTransform.Rotate(Vector3.up, -45, Space.World);
            }
            
            RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -80 x");
            pivotTransform.Rotate(Vector3.right, -80, Space.World);
            
            RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated 15 y");
            pivotTransform.Rotate(Vector3.up, 15, Space.World);
        }
        else
        {
            if (bounds.size.x < bounds.size.z * 0.85f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -25 x | 1");
                pivotTransform.Rotate(Vector3.right, -25, Space.World);
                
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -45 y | 1");
                pivotTransform.Rotate(Vector3.up, -45, Space.World);
            }
            else if ((Mathf.Abs(bounds.size.y - bounds.size.x) / bounds.size.x < 0.01f) && bounds.size.x < bounds.size.z * 0.85f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -25 x | 2");
                pivotTransform.Rotate(Vector3.right, -25, Space.World);
                
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated 45 y | 2");
                pivotTransform.Rotate(Vector3.up, 45, Space.World);
            }
            else if ((Mathf.Abs(bounds.size.y - bounds.size.z) / bounds.size.z < 0.01f) && bounds.size.z < bounds.size.x * 0.85f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated 25 z | 3");
                pivotTransform.Rotate(Vector3.forward, 25, Space.World);
                
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -45 y | 3");
                pivotTransform.Rotate(Vector3.up, -45, Space.World);
            }
            else if (bounds.size.y < bounds.size.x / 2f || bounds.size.x < bounds.size.y / 2f)
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated 45 z | 4");
                pivotTransform.Rotate(Vector3.forward, 45, Space.World);
                
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -25 x | 4");
                pivotTransform.Rotate(Vector3.right, -25, Space.World);
            }
            else
            {
                RuntimeIcons.Log.LogDebug($"{StagedItem.itemProperties.itemName} rotated -25 x | 5");
                pivotTransform.Rotate(Vector3.right, -25, Space.World);
            }
        }
    }
}
