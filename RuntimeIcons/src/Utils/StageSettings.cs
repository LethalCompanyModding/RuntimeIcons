using RuntimeIcons.Config;
using UnityEngine;
using VertexLibrary;
using VertexLibrary.Caches;

namespace RuntimeIcons.Utils;

public class StageSettings
{
    public IVertexCache VertexCache { get; set; } = VertexesExtensions.GlobalPartialCache;
    
    public GrabbableObject TargetObject { get; private set; }
    public Transform TargetTransform => TargetObject.transform;
    public OverrideHolder OverrideHolder { get; private set; }

    internal Vector3 _position = Vector3.zero;
    internal Vector3 _cameraOffset = Vector3.zero;
    internal Quaternion _rotation = Quaternion.identity;

    public Vector3 Position => _position;
    
    public Vector3 CameraOffset => _cameraOffset;
    public Quaternion Rotation => _rotation;

    public StageSettings(GrabbableObject targetObject, OverrideHolder overrideHolder)
    {
        TargetObject = targetObject;
        OverrideHolder = overrideHolder;
    }
    
}