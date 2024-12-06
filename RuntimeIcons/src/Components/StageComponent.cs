using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using RuntimeIcons.Config;
using RuntimeIcons.Dependency;
using RuntimeIcons.Patches;
#if ENABLE_PROFILER_MARKERS
    using Unity.Profiling;
#endif
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

    internal GameObject LightGo { get; private set; }


    private GameObject CameraGo { get; set; }

    internal Transform LightTransform => LightGo.transform;
    private Transform CameraTransform => CameraGo.transform;

    internal Camera Camera;
    private Vector2Int _resolution = new Vector2Int(128, 128);

    private ColorBufferFormat? _originalColorBufferFormat;

    internal Vector2Int Resolution
    {
        get => _resolution;
        set
        {
            _resolution = value;
            Camera.aspect = (float)_resolution.x / _resolution.y;
            CreateRenderTexture();
        }
    }

    internal Vector2 MarginPixels = new Vector2(0, 0);

    internal int CullingMask => Camera.cullingMask;

    internal Transform StagedTransform { get; private set; }
    internal GrabbableObject StagedItem { get; private set; }

    private TransformMemory Memory { get; set; }

    internal static StageComponent CreateStage(HideFlags hideFlags, int cameraLayerMask = 1, string stageName = "Stage",
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
        var cameraGo = new GameObject($"{stageName}.Camera")
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
        stageComponent.Camera = cam;

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
        hdrpCam.hasPersistentHistory = true;
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

        stageComponent.CreateRenderTexture();

        return stageComponent;
    }

    private void CreateRenderTexture()
    {
        Camera.targetTexture = new RenderTexture(
            Resolution.x,
            Resolution.y,
            depth: 0,
            GraphicsFormat.R16G16B16A16_SFloat,
            mipCount: 1)
        {
            enableRandomWrite = true,
        };
    }

    private void Awake()
    {
        HDRenderPipelinePatch.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    internal void SetStageFromSettings(StageSettings stageSettings)
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

        StagedTransform.position = CameraTransform.position + stageSettings.CameraOffset + stageSettings.Position;
        StagedTransform.rotation = stageSettings.Rotation;
        LightTransform.position = CameraTransform.position + stageSettings.CameraOffset;
    }

    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker CenterObjectOnPivotMarker = new(nameof(CenterObjectOnPivot));
    #endif

    internal (Vector3 position, Quaternion rotation) CenterObjectOnPivot(StageSettings stageSettings)
    {
        #if ENABLE_PROFILER_MARKERS
            using var markerAuto = CenterObjectOnPivotMarker.Auto();
        #endif

        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));

        if (stageSettings.State != StageSettingsState.Vertices)
        {
            RuntimeIcons.VerboseRenderingLog(LogLevel.Warning,
                $"wrong state for the settings object: expected {nameof(StageSettingsState.Centered)} but got {stageSettings.State}. SKIPPING");
            return (Vector3.zero, Quaternion.identity);
        }

        var bounds = stageSettings.StagedVertexes.GetBounds();
        if (bounds is null)
            throw new InvalidOperationException("This object has no Renders!");

        stageSettings.Position = -bounds.Value.center;

        var vertices = stageSettings.StagedVertexes;
        for (var i = 0; i < vertices.Length; i++)
            vertices[i] = stageSettings.Position + vertices[i];

        stageSettings.State = StageSettingsState.Centered;

        return (stageSettings.Position, stageSettings.Rotation);
    }

    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker FindOptimalRotationMarker = new(nameof(FindOptimalRotation));
    #endif

    internal (Vector3 position, Quaternion rotation) FindOptimalRotation(StageSettings stageSettings)
    {
        #if ENABLE_PROFILER_MARKERS
            using var markerAuto = FindOptimalRotationMarker.Auto();
        #endif

        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));
        
        if (stageSettings.State != StageSettingsState.Centered)
        {
            RuntimeIcons.VerboseRenderingLog(LogLevel.Warning,
                $"wrong state for the settings object: expected {nameof(StageSettingsState.Centered)} but got {stageSettings.State}. SKIPPING");
            return (Vector3.zero, Quaternion.identity);
        }

        var overrideHolder = stageSettings.OverrideHolder;
        var targetObject = stageSettings.TargetObject;
        var targetItem = targetObject.itemProperties;

        Quaternion targetRotation = Quaternion.identity;

        if (overrideHolder is { StageRotation: not null })
        {
            targetRotation = Quaternion.Euler(overrideHolder.StageRotation.Value);
        }
        else
        {

            var bObj = stageSettings.StagedVertexes.GetBounds();

            if (bObj is null)
                throw new InvalidOperationException("This object has no Renders!");

            var bounds = bObj.Value;

            if (bounds.size == Vector3.zero)
                throw new InvalidOperationException("This object has no Bounds!");

            if (bounds.size.y < bounds.size.x / 2f && bounds.size.y < bounds.size.z / 2f)
            {
                if (bounds.size.z < bounds.size.x * 0.5f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -45 y | 1");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }
                else if (bounds.size.z < bounds.size.x * 0.85f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -90 y | 2");

                    targetRotation = Quaternion.AngleAxis(-90, Vector3.up) * targetRotation;
                }
                else if (bounds.size.x < bounds.size.z * 0.5f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -90 y | 3");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }

                RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -80 x");

                targetRotation = Quaternion.AngleAxis(-80, Vector3.right) * targetRotation;

                RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated 15 y");

                targetRotation = Quaternion.AngleAxis(-15, Vector3.up) * targetRotation;
            }
            else
            {
                if (bounds.size.x < bounds.size.z * 0.85f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -25 x | 1");

                    targetRotation = Quaternion.AngleAxis(-25, Vector3.right) * targetRotation;

                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -45 y | 1");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }
                else if ((Mathf.Abs(bounds.size.y - bounds.size.x) / bounds.size.x < 0.01f) &&
                         bounds.size.x < bounds.size.z * 0.85f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -25 x | 2");

                    targetRotation = Quaternion.AngleAxis(-25, Vector3.right) * targetRotation;

                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated 45 y | 2");

                    targetRotation = Quaternion.AngleAxis(45, Vector3.up) * targetRotation;
                }
                else if ((Mathf.Abs(bounds.size.y - bounds.size.z) / bounds.size.z < 0.01f) &&
                         bounds.size.z < bounds.size.x * 0.85f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated 25 z | 3");

                    targetRotation = Quaternion.AngleAxis(25, Vector3.forward) * targetRotation;

                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -45 y | 3");

                    targetRotation = Quaternion.AngleAxis(-45, Vector3.up) * targetRotation;
                }
                else if (bounds.size.y < bounds.size.x / 2f || bounds.size.x < bounds.size.y / 2f)
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated 45 z | 4");

                    targetRotation = Quaternion.AngleAxis(45, Vector3.forward) * targetRotation;

                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -25 x | 4");

                    targetRotation = Quaternion.AngleAxis(25, Vector3.up) * targetRotation;
                }
                else
                {
                    RuntimeIcons.VerboseRenderingLog(LogLevel.Debug,$"{targetItem.itemName} rotated -25 x | 5");

                    targetRotation = Quaternion.AngleAxis(-25, Vector3.right) * targetRotation;
                }
            }
        }

        //rotate the memory and vertices
        stageSettings.Position = targetRotation * stageSettings.Position;
        stageSettings.Rotation = targetRotation * stageSettings.Rotation;

        var vertices = stageSettings.StagedVertexes;
        for (var i = 0; i < vertices.Length; i++)
            vertices[i] = targetRotation * vertices[i];

        //re-center memory and vertices
        var bounds2 = vertices.GetBounds()!.Value;

        stageSettings.Position -= bounds2.center;

        for (var i = 0; i < vertices.Length; i++)
            vertices[i] -= bounds2.center;
        
        stageSettings.State = StageSettingsState.Rotated;

        return (-bounds2.center, targetRotation);
    }

    private int iterations = 1;

    #if ENABLE_PROFILER_MARKERS
        private static readonly ProfilerMarker ComputeCameraAngleAndFOVMarker = new(nameof(ComputeCameraAngleAndFOV));
    #endif

    internal (Vector3 offset, float fov) ComputeCameraAngleAndFOV(StageSettings stageSettings)
    {
        #if ENABLE_PROFILER_MARKERS
            using var marker = ComputeCameraAngleAndFOVMarker.Auto();
        #endif

        if (stageSettings == null)
            throw new ArgumentNullException(nameof(stageSettings));
        
        if (stageSettings.State != StageSettingsState.Rotated)
        {
            RuntimeIcons.VerboseRenderingLog(LogLevel.Warning,
                $"wrong state for the settings object: expected {nameof(StageSettingsState.Centered)} but got {stageSettings.State}. SKIPPING");
            return (Vector3.zero, 0);
        }

        var vertices = stageSettings.StagedVertexes;
        if (vertices.Length == 0)
            throw new InvalidOperationException("This object has no Renders!");

        var bounds = vertices.GetBounds();
        if (!bounds.HasValue)
            throw new InvalidOperationException("This object has no Bounds!");

        // Adjust the pivot so that the object doesn't clip into the near plane
        var distanceToCamera = Math.Max(stageSettings.CameraNearClip + bounds.Value.size.z, 3f);
        stageSettings.CameraOffset = -bounds.Value.center + Vector3.forward * distanceToCamera;

        for (var i = 0; i < vertices.Length; i++)
            vertices[i] += stageSettings.CameraOffset;
        
        // Calculate the camera size to fit the object being displayed
        var marginFraction = MarginPixels / _resolution;
        var fovScale = Vector2.one / (Vector2.one - marginFraction);

        if (stageSettings.CameraOrthographic)
        {
            var sizeY = bounds.Value.extents.y * fovScale.y;
            var sizeX = bounds.Value.extents.x * fovScale.x * stageSettings.CameraAspect;
            var size = Math.Max(sizeX, sizeY);
            stageSettings.CameraFOV = size;
        }
        else
        {
            Quaternion rotation = Quaternion.identity;

            Vector3 forward, right, down;
            float angleMinX, angleMaxX;
            float angleMinY, angleMaxY;

            for (var i = 0; i < iterations; i++)
            {
                forward = rotation * Vector3.forward;
                right = rotation * Vector3.right;

                GetCameraAngles(forward, right, vertices, out angleMinY, out angleMaxY);
                rotation = Quaternion.AngleAxis((angleMinY + angleMaxY) / 2, Vector3.up) * rotation;

                forward = rotation * Vector3.forward;
                down = rotation * Vector3.down;
                GetCameraAngles(forward, down, vertices, out angleMinX, out angleMaxX);
                rotation = rotation * Quaternion.AngleAxis((angleMinX + angleMaxX) / 2, Vector3.right);
            }

            stageSettings.CameraRotation = rotation;

            forward = rotation * Vector3.forward;
            right = rotation * Vector3.right;
            down = rotation * Vector3.down;
            GetCameraAngles(forward, right, vertices, out angleMinY, out angleMaxY);
            GetCameraAngles(forward, down, vertices, out angleMinX, out angleMaxX);

            var fovAngleX = Math.Max(-angleMinX, angleMaxX) * 2 * fovScale.y;
            var fovAngleY =
                HorizontalToVerticalFOV(Math.Max(-angleMinY, angleMaxY) * 2, stageSettings.CameraAspect) *
                fovScale.x;
            stageSettings.CameraFOV = Math.Max(fovAngleX, fovAngleY) * Mathf.Rad2Deg;
        }
        
        stageSettings.State = StageSettingsState.CameraValues;
        
        return (stageSettings.CameraOffset, stageSettings.CameraFOV);
    }

    private static void GetCameraAngles(Vector3 forward, Vector3 direction, IEnumerable<Vector3> vertices,
        out float angleMin, out float angleMax)
    {
        var tangentMin = float.PositiveInfinity;
        var tangentMax = float.NegativeInfinity;

        foreach (var vertex in vertices)
        {
            var tangent = Vector3.Dot(vertex, direction) / Vector3.Dot(vertex, forward);
            tangentMin = Math.Min(tangent, tangentMin);
            tangentMax = Math.Max(tangent, tangentMax);
        }

        angleMin = Mathf.Atan(tangentMin);
        angleMax = Mathf.Atan(tangentMax);
    }

    private float HorizontalToVerticalFOV(float horizontalFOV, float aspectRatio)
    {
        return 2f * Mathf.Atan(Mathf.Tan(horizontalFOV * 0.5f) / aspectRatio);
    }

    internal void ResetStage()
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
        if (camera != Camera)
            return;

        SetAlphaEnabled(true);
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != Camera)
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

    internal record struct TransformMemory
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

    internal class StageSettings
    {
        internal StageSettingsState State = StageSettingsState.None;

        internal CameraQueueComponent.RenderingRequest TargetRequest;

        internal readonly Vector3[] StagedVertexes;

        internal GrabbableObject TargetObject => TargetRequest.GrabbableObject;
        internal Transform TargetTransform => TargetObject.transform;
        internal OverrideHolder OverrideHolder => TargetRequest.OverrideHolder;

        internal Vector3 Position = Vector3.zero;
        internal Vector3 CameraOffset = Vector3.zero;
        internal Quaternion Rotation = Quaternion.identity;

        internal readonly bool CameraOrthographic;
        internal readonly float CameraAspect;
        internal readonly float CameraNearClip;

        internal Quaternion CameraRotation = Quaternion.identity;
        internal float CameraFOV = 45;

        internal StageSettings(StageComponent stage, CameraQueueComponent.RenderingRequest renderingRequest)
        {
            CameraOrthographic = stage.Camera.orthographic;
            CameraAspect = stage.Camera.aspect;
            CameraNearClip = stage.Camera.nearClipPlane;

            TargetRequest = renderingRequest;

            if (OverrideHolder is { ItemRotation: not null })
            {
                Rotation = Quaternion.Euler(OverrideHolder.ItemRotation.Value + new Vector3(0, 90f, 0));
            }
            else
            {
                Rotation = Quaternion.Euler(TargetObject.itemProperties.restingRotation.x,
                    TargetObject.itemProperties.floorYOffset + 90f,
                    TargetObject.itemProperties.restingRotation.z);
            }

            var matrix = Matrix4x4.TRS(Vector3.zero, Rotation, TargetTransform.lossyScale);

            var executionOptions = new ExecutionOptions()
            {
                VertexCache = stage.VertexCache,
                CullingMask = stage.CullingMask,
                LogHandler = RuntimeIcons.VerboseMeshLog,
                OverrideMatrix = matrix
            };

            StagedVertexes = TargetTransform.gameObject.GetVertexes(executionOptions);
            State = StageSettingsState.Vertices;
        }
    }
    
    public enum StageSettingsState
    {
        None,
        Vertices,
        Centered,
        Rotated,
        CameraValues
    }
}