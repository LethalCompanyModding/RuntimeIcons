using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using RuntimeIcons.Dependency;
using UnityEngine;

namespace RuntimeIcons.Config;

internal static class RotationEditor
{
    internal static void Init()
    {
        if (LethalConfigProxy.Enabled)
        {
            var configFile = new ConfigFile(Path.GetTempFileName(), false, MetadataHelper.GetMetadata(RuntimeIcons.INSTANCE));

            var eulerAngles = configFile.Bind("Rotation Calculator", "Euler Angles", "0,0,0", "The Euler angles representing this rotation");

            var angle = configFile.Bind("Rotation Calculator", "Angle", 0.0f, new ConfigDescription("rotation angle", new AcceptableValueRange<float>(-360, 360)));

            LethalConfigProxy.AddConfig(eulerAngles);
            LethalConfigProxy.AddConfig(angle);
            LethalConfigProxy.AddButton("Rotation Calculator", "Apply X Rotation", "translate current EulerAngles around world X Axis by Rotation amount", "X Rot",
                () => ApplyRotation(angle.Value, Vector3.right));
            LethalConfigProxy.AddButton("Rotation Calculator", "Apply Y Rotation", "translate current EulerAngles around world X Axis by Rotation amount", "Y Rot",
                () => ApplyRotation(angle.Value, Vector3.up));
            LethalConfigProxy.AddButton("Rotation Calculator", "Apply Z Rotation", "translate current EulerAngles around world X Axis by Rotation amount", "Z Rot",
                () => ApplyRotation(angle.Value, Vector3.forward));

            return;

            void ApplyRotation(float angle, Vector3 axis)
            {
                var euler = Vector3.zero;
                var arr = eulerAngles.Value.Split(",");
                if (arr.Length == 3)
                {
                    if (float.TryParse(arr[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    {
                        if (float.TryParse(arr[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                        {
                            if (float.TryParse(arr[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                            {
                                euler = new Vector3(x, y, z);
                            }
                        }
                    }
                }

                var quat = Quaternion.Euler(euler);

                var rot = Quaternion.AngleAxis(angle, axis);

                var res = rot * quat;

                eulerAngles.Value = $"{WrapAroundAngle(res.eulerAngles.x)},{WrapAroundAngle(res.eulerAngles.y)},{WrapAroundAngle(res.eulerAngles.z)}";
            }

        }
    }

    private static float WrapAroundAngle(float angle)
    {
        // reduce the angle
        angle %= 360;

        // force it to be the positive remainder, so that 0 <= angle < 360
        angle = (angle + 360) % 360;

        // force into the minimum absolute value residue class, so that -180 < angle <= 180
        if (angle > 180)
            angle -= 360;

        //round value to not have Exponential Notation
        angle = (float)Math.Round(angle, 2);

        return angle;
    }
}