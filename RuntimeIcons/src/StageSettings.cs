using RuntimeIcons.Config;
using UnityEngine;

namespace RuntimeIcons;

public class StageSettings
{
    public RenderingRequest TargetRequest { get; private set; }

    public GrabbableObject TargetObject => TargetRequest.GrabbableObject;
    public Transform TargetTransform => TargetObject.transform;
    public OverrideHolder OverrideHolder => TargetRequest.OverrideHolder;

    internal Vector3 _position = Vector3.zero;
    internal Vector3 _cameraOffset = Vector3.zero;
    internal Quaternion _rotation = Quaternion.identity;

    public Vector3 Position => _position;

    public Vector3 CameraOffset => _cameraOffset;
    public Quaternion Rotation => _rotation;

    public StageSettings(RenderingRequest renderingRequest)
    {
        TargetRequest = renderingRequest;
    }
}