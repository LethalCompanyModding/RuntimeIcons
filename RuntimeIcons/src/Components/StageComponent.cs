using System;
using System.Collections.Generic;
using System.Linq;
using RuntimeIcons.Dependency;
using RuntimeIcons.Patches;
using RuntimeIcons.Utils;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using VertexLibrary;
using VertexLibrary.Caches;
using static UnityEngine.Rendering.HighDefinition.RenderPipelineSettings;

namespace RuntimeIcons.Components;

public class StageComponent : MonoBehaviour
{
    internal CameraQueueComponent CameraQueue { get; private set; }

    private StageComponent()
    {
    }

    public IVertexCache VertexCache { get; set; } = VertexesExtensions.GlobalPartialCache;

    public GameObject LightGo { get; private set; }


    private GameObject CameraGo { get; set; }

    public Transform LightTransform => LightGo.transform;
    private Transform CameraTransform => CameraGo.transform;

    private Camera _camera;
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

    private TransformMemory Memory { get; set; }

    public static StageComponent CreateStage(HideFlags hideFlags, int cameraLayerMask = 1, string stageName = "Stage",
        bool orthographic = false)
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

        stageComponent.NewCameraTexture();

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
    
    public void SetStageFromSettings(StageSettings stageSettings)
    {
        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));

        var grabbableObject = stageSettings.TargetObject;
        var targetTransform = stageSettings.TargetTransform;

        if (StagedTransform && StagedTransform != targetTransform)
            throw new InvalidOperationException("An Object is already on stage!");
        
        StagedTransform = targetTransform;
        StagedItem = grabbableObject;

        Memory = new TransformMemory(StagedTransform);

        StagedTransform.position = CameraTransform.position + stageSettings._cameraOffset + stageSettings._position;
        StagedTransform.rotation = stageSettings.Rotation;
        LightTransform.position = StagedTransform.position;
    }  
    
    public (Vector3 position, Quaternion rotation) CenterObjectOnPivot(StageSettings stageSettings)
    {
        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));

        var overrideHolder = stageSettings.OverrideHolder;
        var grabbableObject = stageSettings.TargetObject;
        var targetTransform = stageSettings.TargetTransform;
        
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


        var matrix = Matrix4x4.TRS(Vector3.zero, rotation, targetTransform.localScale);

        var executionOptions = new ExecutionOptions()
        {
            VertexCache = VertexCache,
            CullingMask = CullingMask,
            LogHandler = RuntimeIcons.VerboseMeshLog,
            OverrideMatrix = matrix
        };

        if (!targetTransform.gameObject.TryGetBounds(out var bounds, executionOptions))
            throw new InvalidOperationException("This object has no Renders!");

        stageSettings._position = -bounds.center;
        stageSettings._rotation = rotation;

        return (stageSettings.Position, stageSettings.Rotation);
    }

    public (Vector3 offset, float fov) PrepareCameraForShot(StageSettings stageSettings)
    {
        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));
        
        var targetTransform = stageSettings.TargetTransform;
        
        var matrix = Matrix4x4.TRS(stageSettings.Position, stageSettings.Rotation, targetTransform.localScale);

        var executionOptions = new ExecutionOptions()
        {
            VertexCache = VertexCache,
            CullingMask = CullingMask,
            LogHandler = RuntimeIcons.VerboseMeshLog,
            OverrideMatrix = matrix,
        };

        var vertices = targetTransform.GetVertexes(executionOptions);
        if (vertices.Length == 0)
            throw new InvalidOperationException("This object has no Renders!");

        var bounds = vertices.GetBounds();
        if (!bounds.HasValue)
            throw new InvalidOperationException("This object has no Bounds!");

        // Adjust the pivot so that the object doesn't clip into the near plane
        var distanceToCamera = Math.Max(_camera.nearClipPlane + bounds.Value.size.z, 3f);
        stageSettings._cameraOffset = -bounds.Value.center + Vector3.forward * distanceToCamera;

        // Calculate the camera size to fit the object being displayed
        Vector2 marginFraction = MarginPixels / _resolution;
        Vector2 fovScale = Vector2.one / (Vector2.one - marginFraction);

        if (_camera.orthographic)
        {
            var sizeY = bounds.Value.extents.y * fovScale.y;
            var sizeX = bounds.Value.extents.x * fovScale.x * _camera.aspect;
            var size = Math.Max(sizeX, sizeY);
            _camera.orthographicSize = size;
            return (stageSettings._cameraOffset, _camera.orthographicSize);
        }
        else
        {
            var updateMatrix = Matrix4x4.TRS(_camera.transform.position + stageSettings._cameraOffset, Quaternion.identity, Vector3.one);
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = updateMatrix.MultiplyPoint3x4(vertices[i]);
            
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
            var fovAngleY =
                Camera.HorizontalToVerticalFieldOfView(Math.Max(-angleMinY, angleMaxY) * 2, _camera.aspect) *
                fovScale.x;
            _camera.fieldOfView = Math.Max(fovAngleX, fovAngleY);
            return (stageSettings._cameraOffset, _camera.fieldOfView);
        }
    }

    public void ResetStage()
    {
        if (StagedTransform)
        {
            StagedTransform.SetParent(Memory.Parent, false);

            StagedTransform.localScale = Memory.LocalScale;
            StagedTransform.SetLocalPositionAndRotation(Memory.LocalPosition, Memory.LocalRotation);
        }

        StagedTransform = null;
        StagedItem = null;
        Memory = default;

        LightTransform.localPosition = Vector3.zero;
        LightTransform.rotation = Quaternion.identity;
        CameraTransform.localRotation = Quaternion.identity;
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

        public IsolateStageLights(params GameObject[] stageObjects)
        {
            _lightMemory = UnityEngine.Pool.HashSetPool<Light>.Get();

            _ambientLight = RenderSettings.ambientLight;
            RenderSettings.ambientLight = Color.black;

            List<Light> localLights = [];
            foreach (var gameObject in stageObjects)
            {
                var lights = gameObject.GetComponentsInChildren<Light>();
                localLights.AddRange(lights);
            }

            var globalLights = FindObjectsOfType<Light>().Where(l => !localLights.Contains(l)).Where(l => l.enabled)
                .ToArray();

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

    private static void GetCameraAngles(Camera camera, Vector3 direction, IEnumerable<Vector3> vertices,
        out float angleMin, out float angleMax)
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

    public (Vector3 position, Quaternion rotation) FindOptimalRotation(StageSettings stageSettings)
    {
        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));
        
        var overrideHolder = stageSettings.OverrideHolder;
        var targetObject = stageSettings.TargetObject;
        var targetItem = targetObject.itemProperties;
        var targetTransform = stageSettings.TargetTransform;
        
        Quaternion targetRotation = Quaternion.identity;

        if (overrideHolder is { StageRotation: not null })
        {
            targetRotation = Quaternion.Euler(overrideHolder.StageRotation.Value);
        }
        else
        {
            
            var matrix = Matrix4x4.TRS(stageSettings.Position, stageSettings.Rotation, targetTransform.localScale);
            
            var executionOptions = new ExecutionOptions()
            {
                VertexCache = VertexCache,
                CullingMask = CullingMask,
                LogHandler = RuntimeIcons.VerboseMeshLog,
                OverrideMatrix = matrix
            };

            if (!targetTransform.TryGetBounds(out var bounds, executionOptions))
                throw new InvalidOperationException("This object has no Renders!");

            if (bounds.size == Vector3.zero)
                throw new InvalidOperationException("This object has no Bounds!");

            if (bounds.size.y < bounds.size.x / 2f && bounds.size.y < bounds.size.z / 2f)
            {
                if (bounds.size.z < bounds.size.x * 0.5f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -45 y | 1");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }
                else if (bounds.size.z < bounds.size.x * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -90 y | 2");

                    targetRotation = Quaternion.AngleAxis(-90, Vector3.up) * targetRotation;
                }
                else if (bounds.size.x < bounds.size.z * 0.5f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -90 y | 3");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }

                RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -80 x");

                targetRotation = Quaternion.AngleAxis(-80, Vector3.right) * targetRotation;

                RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated 15 y");

                targetRotation = Quaternion.AngleAxis(-15, Vector3.up) * targetRotation;
            }
            else
            {
                if (bounds.size.x < bounds.size.z * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -25 x | 1");

                    targetRotation = Quaternion.AngleAxis(-25, Vector3.right) * targetRotation;

                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -45 y | 1");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }
                else if ((Mathf.Abs(bounds.size.y - bounds.size.x) / bounds.size.x < 0.01f) &&
                         bounds.size.x < bounds.size.z * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -25 x | 2");

                    targetRotation = Quaternion.AngleAxis(-25, Vector3.right) * targetRotation;

                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated 45 y | 2");

                    targetRotation = Quaternion.AngleAxis(45, Vector3.up) * targetRotation;
                }
                else if ((Mathf.Abs(bounds.size.y - bounds.size.z) / bounds.size.z < 0.01f) &&
                         bounds.size.z < bounds.size.x * 0.85f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated 25 z | 3");

                    targetRotation = Quaternion.AngleAxis(25, Vector3.forward) * targetRotation;

                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -45 y | 3");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }
                else if (bounds.size.y < bounds.size.x / 2f || bounds.size.x < bounds.size.y / 2f)
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated 45 z | 4");

                    targetRotation = Quaternion.AngleAxis(45, Vector3.forward) * targetRotation;

                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -25 x | 4");

                    targetRotation = Quaternion.AngleAxis(25, Vector3.up) * targetRotation;
                }
                else
                {
                    RuntimeIcons.Log.LogDebug($"{targetItem.itemName} rotated -25 x | 5");

                    targetRotation = Quaternion.AngleAxis(-25, Vector3.right) * targetRotation;
                }
            }
        }

        stageSettings._position = targetRotation * stageSettings._position;
        stageSettings._rotation = targetRotation * stageSettings._rotation;

        return (Vector3.zero, targetRotation);
    }

    internal RenderTexture NewCameraTexture()
    {
        var destTexture = RenderTexture.GetTemporary(
            Resolution.x,
            Resolution.y, 8, GraphicsFormat.R16G16B16A16_SFloat);
        _camera.targetTexture = destTexture;
        return destTexture;
    }
}