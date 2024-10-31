using System.IO;
using UnityEngine;

namespace RuntimeIcons.Utils;

public static class SpriteUtils
{
    public static Sprite GetSprite(byte[] data)
    {
        var texture = new Texture2D(1, 1);
        texture.LoadImage(data);
        
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(texture.width / 2f, texture.height / 2f));
    }
    public static Sprite GetSprite(Stream stream)
    {
        byte[] data;

        int i;
        using (BinaryReader br = new BinaryReader(stream))
        {
            i = (int)(stream.Length);
            data = br.ReadBytes(i); // (500000);
        }
        
        var texture = new Texture2D(1, 1);
        texture.LoadImage(data);
        
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(texture.width / 2f, texture.height / 2f));
    }
}