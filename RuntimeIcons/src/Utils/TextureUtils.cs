/*
Portion of Code from https://gist.github.com/mickdekkers/5c3c62539c057010d4497f9865060e20
By https://github.com/mickdekkers
As stated in this comment https://gist.github.com/mickdekkers/5c3c62539c057010d4497f9865060e20?permalink_comment_id=3253740#gistcomment-3253740
to be considered MIT licensed
*/

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace RuntimeIcons.Utils;

public static class TextureUtils
{
    /// <summary>
    /// Sanitizes a filename string by replacing illegal characters with underscores.
    /// </summary>
    /// <param name="dirty">The unsanitized filename string.</param>
    /// <returns>A sanitized filename string with illegal characters replaced with underscores.</returns>
    private static string SanitizeFilename (string dirty)
    {
        string invalidFileNameChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidFileNameChars);

        return Regex.Replace(dirty, invalidRegStr, "_");
    }
    
    public static FileInfo SaveFile (byte[] bytes, string filename, string directory, string extension)
    {
        directory = Directory.CreateDirectory(directory).FullName;
        filename = filename != "" ? SanitizeFilename(filename) + extension : DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-ffff") + extension;
        string filepath = Path.Combine(directory, filename);

        File.WriteAllBytes(filepath, bytes);

        return new FileInfo(filepath);
    }
    
    public static FileInfo SavePNG (this Texture2D tex, string filename = "", string directory = "")
    {
        return SaveFile(tex.EncodeToPNG(), filename.IsNullOrWhiteSpace() ? tex.name : filename, directory.IsNullOrWhiteSpace() ? "" : directory, ".png");
    }
    
    public static FileInfo SaveEXR (this Texture2D tex, string filename = "", string directory = "")
    {
        return SaveFile(tex.EncodeToEXR(), filename.IsNullOrWhiteSpace() ? tex.name : filename, directory.IsNullOrWhiteSpace() ? "" : directory, ".exr");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RGBA(half r, half g, half b, half a)
    {
        public half r = r;
        public half g = g;
        public half b = b;
        public half a = a;

        public static implicit operator Color(RGBA color)
        {
            return new Color(color.r, color.g, color.b, color.a);
        }

        public static implicit operator RGBA(Color color)
        {
            return new RGBA((half)color.r, (half)color.g, (half)color.b, (half)color.a);
        }
    }

    public static Texture2D GetNonPremultipliedTexture(this Texture2D tex)
    {
        
        var newTex = Object.Instantiate(tex);
        newTex.name = $"{tex.name}-NonPremultiplied";
        
        newTex.UnPremultiply();

        return newTex;
    }
    
    public static void UnPremultiply(this Texture2D tex)
    {
        if (tex.graphicsFormat != GraphicsFormat.R16G16B16A16_SFloat)
            throw new NotImplementedException("Texture to un-premultiply must have 16-bit floating point components");

        var pixels = tex.GetPixelData<RGBA>(0);
        for (var i = 0; i < pixels.Length; i++)
        {
            RGBA color = pixels[i];
            if (color.a == 0)
                continue;
            color.r /= color.a;
            color.g /= color.a;
            color.b /= color.a;
            pixels[i] = color;
        }
    }
    
    public static long GetTransparentCount(this Texture2D tex)
    {
        var count = 0;
        for (var x = 0; x < tex.width; x++)
        for (var y = 0; y < tex.height; y++)
            if (tex.GetPixel(x, y).a == 0)
                count++;
        return count;
    }
}