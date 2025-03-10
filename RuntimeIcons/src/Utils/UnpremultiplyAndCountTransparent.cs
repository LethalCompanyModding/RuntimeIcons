using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeIcons.Utils;

internal static class UnpremultiplyAndCountTransparent
{
    private static ComputeShader _unpremultiplyAndCountTransparentShader;

    private static int _unpremultiplyAndCountTransparentHandle;
    private static uint _unpremultiplyAndCountTransparentThreadWidth;
    private static uint _unpremultiplyAndCountTransparentThreadHeight;

    private static ComputeBuffer _transparentCountBuffer;
    private static uint[] _transparentCountZeroes = new uint[] { 0u };

    private static int _currentTransparentCountID = 0;
    private static readonly Dictionary<int, uint> _transparentCounts = [];

    private static int _texturePropertyID;

    internal static bool LoadShaders(AssetBundle bundle)
    {
        _unpremultiplyAndCountTransparentShader = bundle.LoadAsset<ComputeShader>("Assets/Shaders/UnpremultiplyAndCountTransparent.compute");

        if (!_unpremultiplyAndCountTransparentShader)
        {
            RuntimeIcons.Log.LogFatal("Failed to load the compute shader.");
            return false;
        }

        _unpremultiplyAndCountTransparentHandle = _unpremultiplyAndCountTransparentShader.FindKernel("UnpremultiplyAndCountTransparent");
        _unpremultiplyAndCountTransparentShader.GetKernelThreadGroupSizes(_unpremultiplyAndCountTransparentHandle, out _unpremultiplyAndCountTransparentThreadWidth, out _unpremultiplyAndCountTransparentThreadHeight, out _);

        _transparentCountBuffer = new ComputeBuffer(1, sizeof(uint));
        _unpremultiplyAndCountTransparentShader.SetBuffer(_unpremultiplyAndCountTransparentHandle, "TransparentCount", _transparentCountBuffer);

        _texturePropertyID = Shader.PropertyToID("Texture");
        
        return true;
    }

    public static int Execute(CommandBuffer cmd, RenderTexture texture)
    {
        if (_unpremultiplyAndCountTransparentShader == null)
        {
            RuntimeIcons.Log.LogError("UnpremultiplyAndCountTransparent has been called before the shader was loaded.");
            return -1;
        }

        var threadGroupsX = (int)(texture.width / _unpremultiplyAndCountTransparentThreadWidth);
        var threadGroupsY = (int)(texture.height / _unpremultiplyAndCountTransparentThreadHeight);

        if (threadGroupsX * _unpremultiplyAndCountTransparentThreadWidth > texture.width
            || threadGroupsY * _unpremultiplyAndCountTransparentThreadHeight > texture.height)
        {
            RuntimeIcons.Log.LogError($"Texture size must be a multiple of {_unpremultiplyAndCountTransparentThreadWidth}x{_unpremultiplyAndCountTransparentThreadHeight}: {texture.width}x{texture.height}");
            return -1;
        }

        cmd.SetComputeTextureParam(_unpremultiplyAndCountTransparentShader, _unpremultiplyAndCountTransparentHandle, _texturePropertyID, texture);
        cmd.SetBufferData(_transparentCountBuffer, _transparentCountZeroes);
        cmd.DispatchCompute(_unpremultiplyAndCountTransparentShader, _unpremultiplyAndCountTransparentHandle, threadGroupsX, threadGroupsY, 1);

        var countID = _currentTransparentCountID++;
        cmd.RequestAsyncReadback(_transparentCountBuffer, r => _transparentCounts[countID] = r.GetData<uint>()[0]);
        return countID;
    }

    public static bool TryGetTransparentCount(int id, out uint count)
    {
        count = uint.MaxValue;
        if (id == -1)
            return true;
        if (_transparentCounts.Remove(id, out count))
            return true;
        return false;
    }
}
