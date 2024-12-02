using System.IO;
using UnityEngine;

namespace RuntimeIcons.Utils;

public static class SpriteUtils
{
    public static Sprite CreateSprite(Texture2D texture)
    {
        // Use SpriteMeshType.FullRect, as Unity apparently gets very confused when creating a tight mesh
        // around our generated textures at runtime, occasionally cutting them off on the top or the bottom.
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(texture.width / 2f, texture.height / 2f),
            pixelsPerUnit: 100f, extrude: 0u, SpriteMeshType.FullRect);
    }

    public static Sprite CreateSprite(byte[] data)
    {
        var texture = new Texture2D(1, 1);
        texture.LoadImage(data);

        return CreateSprite(texture);
    }

    public static Sprite CreateSprite(Stream stream)
    {
        byte[] data;

        int i;
        using (BinaryReader br = new BinaryReader(stream))
        {
            i = (int)(stream.Length);
            data = br.ReadBytes(i); // (500000);
        }

        return CreateSprite(data);
    }

    public class SpriteInfo
    {
        public string Source { get; internal set; }
    }
}