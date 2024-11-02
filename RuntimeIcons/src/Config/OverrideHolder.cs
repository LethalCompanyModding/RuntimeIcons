using UnityEngine;

namespace RuntimeIcons.Config;

public class OverrideHolder
{
    public string Source { get; internal set; } = nameof(RuntimeIcons);
    
    public Sprite OverrideSprite { get;  internal set; } = null!;

    public int Priority { get; internal set; } = 0;

    public Vector3? ItemRotation { get; internal set; } = null!;
    public Vector3? StageRotation { get; internal set; } = null!;

}