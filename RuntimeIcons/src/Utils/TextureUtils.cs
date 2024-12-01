/*
Portion of Code from https://gist.github.com/mickdekkers/5c3c62539c057010d4497f9865060e20
By https://github.com/mickdekkers
As stated in this comment https://gist.github.com/mickdekkers/5c3c62539c057010d4497f9865060e20?permalink_comment_id=3253740#gistcomment-3253740
to be considered MIT licensed
*/

using System;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

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
        
        RuntimeIcons.Log.LogDebug($"Saving {filename} to {directory}");

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
}