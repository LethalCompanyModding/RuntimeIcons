using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RuntimeIcons.Components;
using RuntimeIcons.Config;
using RuntimeIcons.Dependency;
using RuntimeIcons.Patches;
using RuntimeIcons.Utils;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using LogType = VertexLibrary.LogType;

namespace RuntimeIcons;

[BepInPlugin(GUID, NAME, VERSION)]
[BepInDependency("com.github.lethalcompanymodding.vertexlibrary", "1.0.0")]
[BepInDependency("BMX.LobbyCompatibility", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("ainavt.lc.lethalconfig", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("imabatby.lethallevelloader", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("evaisa.lethallib", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(CullFactoryCompatibility.GUID, BepInDependency.DependencyFlags.SoftDependency)]
public class RuntimeIcons : BaseUnityPlugin
{
    public const string GUID = MyPluginInfo.PLUGIN_GUID;
    public const string NAME = MyPluginInfo.PLUGIN_NAME;
    public const string VERSION = MyPluginInfo.PLUGIN_VERSION;
    internal static readonly ISet<Hook> Hooks = new HashSet<Hook>();
    internal static readonly Harmony Harmony = new(GUID);

    internal static ManualLogSource Log;

    internal static StageComponent CameraStage;
    internal static GameObject ThrottleGo;

    internal static Dictionary<string, OverrideHolder> OverrideMap = new(StringComparer.InvariantCultureIgnoreCase);

    public static Sprite LoadingSprite { get; private set; }
    public static Sprite WarningSprite { get; private set; }
    public static Sprite ErrorSprite { get; private set; }

    public static RuntimeIcons INSTANCE { get; private set; }


    private void Awake()
    {
        INSTANCE = this;
        Log = Logger;
        try
        {
            if (LobbyCompatibilityChecker.Enabled)
                LobbyCompatibilityChecker.Init();

            Log.LogInfo("Initializing Configs");

            PluginConfig.Init();

            Log.LogInfo("Preparing Stage");

            SetStage();
            ThrottleGo =ThrottleUtils.InitComponents();

            Log.LogInfo("Loading Overrides");

            LoadOverrides();

            Log.LogInfo("Loading Icons");

            LoadIcons();

            Log.LogInfo("Patching Methods");

            StartOfRoundPatch.Init();
            Harmony.PatchAll();

            Log.LogInfo(NAME + " v" + VERSION + " Loaded!");
        }
        catch (Exception ex)
        {
            Log.LogError("Exception while initializing: \n" + ex);
        }
    }

    internal static void VerboseMeshLog(LogType logLevel, Func<string> message)
    {
        var level = logLevel switch
        {
            LogType.Fatal => LogLevel.Fatal,
            LogType.Error => LogLevel.Error,
            LogType.Warning => LogLevel.Warning,
            LogType.Info1 or LogType.Info2 or LogType.Info3 or LogType.Info4 or LogType.Info => LogLevel.Info,
            LogType.Debug1 or LogType.Debug2 or LogType.Debug3 or LogType.Debug4 or LogType.Debug => LogLevel.Debug,
            LogType.All => LogLevel.All,
            _ => LogLevel.None
        };

        if ((level & PluginConfig.VerboseMeshLogs) != 0)
            VerboseMeshLog(level, message);
    }

    internal static void VerboseMeshLog(LogLevel logLevel, Func<string> message)
    {
        Log.Log(logLevel, message());
    }

    private void SetStage()
    {
        CameraStage = StageComponent.CreateStage(HideFlags.HideAndDontSave, LayerMask.GetMask("Default",
                "Player", "Water",
                "Props", "Room", "InteractableObject", "Foliage", "PhysicsObject", "Enemies", "PlayerRagdoll",
                "MapHazards", "MiscLevelGeometry", "Terrain"), $"{nameof(RuntimeIcons)}.Stage");
        DontDestroyOnLoad(CameraStage.gameObject);
        CameraStage.gameObject.transform.position = new Vector3(0, 1000, 1000);
        CameraStage.Resolution = new Vector2Int(256, 256);
        CameraStage.MarginPixels = new Vector2(32, 32);

        //add ceiling light!
        var lightGo1 = new GameObject("SpotLight 1")
        {
            hideFlags = hideFlags,
            layer = 1,
            transform =
            {
                parent = CameraStage.LightTransform,
                localPosition = new Vector3(0, 3, 0),
                rotation = Quaternion.LookRotation(Vector3.down)
            }
        };

        var light = lightGo1.AddComponent<Light>();
        light.type = LightType.Spot;
        light.shape = LightShape.Cone;
        light.color = Color.white;
        light.colorTemperature = 6901;
        light.useColorTemperature = true;
        light.shadows = LightShadows.Soft;
        light.spotAngle = 50.0f;
        light.innerSpotAngle = 21.8f;
        light.range = 7.11f;

        var lightData = lightGo1.AddComponent<HDAdditionalLightData>();
        lightData.affectDiffuse = true;
        lightData.affectSpecular = true;
        lightData.affectsVolumetric = true;
        lightData.applyRangeAttenuation = true;
        lightData.color = Color.white;
        lightData.colorShadow = true;
        lightData.shadowDimmer = 0.8f;
        lightData.shadowResolution.@override = 1024;
        lightData.customSpotLightShadowCone = 30f;
        lightData.distance = 150000000000;
        lightData.fadeDistance = 10000;
        lightData.innerSpotPercent = 82.7f;
        lightData.intensity = 75f;

        // add front light ( similar to ceiling one but facing a 45 angle )
        var lightGo2 = new GameObject("SpotLight 2")
        {
            hideFlags = hideFlags,
            layer = 1,
            transform =
            {
                parent = CameraStage.LightTransform,
                localPosition = new Vector3(-2.7f, 0, -2.7f),
                rotation = Quaternion.Euler(0, 45, 0)
            }
        };

        var light2 = lightGo2.AddComponent<Light>();
        light2.type = LightType.Spot;
        light2.shape = LightShape.Cone;
        light2.color = Color.white;
        light2.colorTemperature = 6901;
        light2.useColorTemperature = true;
        light2.shadows = LightShadows.Soft;
        light2.spotAngle = 50.0f;
        light2.innerSpotAngle = 21.8f;
        light2.range = 7.11f;

        var lightData2 = lightGo2.AddComponent<HDAdditionalLightData>();
        lightData2.affectDiffuse = true;
        lightData2.affectSpecular = true;
        lightData2.affectsVolumetric = true;
        lightData2.applyRangeAttenuation = true;
        lightData2.color = Color.white;
        lightData2.colorShadow = true;
        lightData2.shadowDimmer = 0.6f;
        lightData2.shadowResolution.@override = 1024;
        lightData2.customSpotLightShadowCone = 30f;
        lightData2.distance = 150000000000;
        lightData2.fadeDistance = 10000;
        lightData2.innerSpotPercent = 82.7f;
        lightData2.intensity = 50f;
        lightData2.shapeRadius = 0.5f;

        // add a second front light ( similar to the other one but does not have Specular )
        var lightGo3 = new GameObject("SpotLight 3")
        {
            hideFlags = hideFlags,
            layer = 1,
            transform =
            {
                parent = CameraStage.LightTransform,
                localPosition = new Vector3(2.7f, 0, -2.7f),
                rotation = Quaternion.Euler(0, -45, 0)
            }
        };

        var light3 = lightGo3.AddComponent<Light>();
        light3.type = LightType.Spot;
        light3.shape = LightShape.Cone;
        light3.color = Color.white;
        light3.colorTemperature = 6901;
        light3.useColorTemperature = true;
        light3.shadows = LightShadows.Soft;
        light3.spotAngle = 50.0f;
        light3.innerSpotAngle = 21.8f;
        light3.range = 7.11f;

        var lightData3 = lightGo3.AddComponent<HDAdditionalLightData>();
        lightData3.affectDiffuse = true;
        lightData3.affectSpecular = false;
        lightData3.affectsVolumetric = true;
        lightData3.applyRangeAttenuation = true;
        lightData3.color = Color.white;
        lightData3.colorShadow = true;
        lightData3.shadowDimmer = 0.4f;
        lightData3.shadowResolution.@override = 1024;
        lightData3.customSpotLightShadowCone = 30f;
        lightData3.distance = 150000000000;
        lightData3.fadeDistance = 10000;
        lightData3.innerSpotPercent = 82.7f;
        lightData3.intensity = 30f;
    }

    private void LoadIcons()
    {
        OverrideHolder holder;
        if (OverrideMap.TryGetValue("RuntimeIcons/Loading", out holder) && holder.OverrideSprite)
        {
            LoadingSprite = holder.OverrideSprite;
        }
        else
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LoadingSprite.png");
            LoadingSprite = SpriteUtils.GetSprite(stream);
        }

        LoadingSprite.name = LoadingSprite.texture.name = $"{nameof(RuntimeIcons)}.Loading";

        if (OverrideMap.TryGetValue("RuntimeIcons/Warning", out holder) && holder.OverrideSprite)
        {
            WarningSprite = holder.OverrideSprite;
        }
        else
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WarningSprite.png");
            WarningSprite = SpriteUtils.GetSprite(stream);
        }

        WarningSprite.name = WarningSprite.texture.name = $"{nameof(RuntimeIcons)}.Warning";

        if (OverrideMap.TryGetValue("RuntimeIcons/Error", out holder) && holder.OverrideSprite)
        {
            ErrorSprite = holder.OverrideSprite;
        }
        else
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ErrorSprite.png");
            ErrorSprite = SpriteUtils.GetSprite(stream);
        }

        ErrorSprite.name = ErrorSprite.texture.name = $"{nameof(RuntimeIcons)}.Error";
    }

    private static void LoadOverrides()
    {
        var configDirectory = Path.Combine(Paths.ConfigPath, nameof(RuntimeIcons));

        if (Directory.Exists(configDirectory)) 
            ProcessDirectory(configDirectory, "RuntimeIcons.Config");

        foreach (var directory in Directory.EnumerateDirectories(Paths.PluginPath)
                     .SelectMany(d => Directory.EnumerateDirectories(d, nameof(RuntimeIcons))))
        {
            var source = Path.GetFileName(Path.GetDirectoryName(directory));
            ProcessDirectory(directory, source);
        }

        return;

        void ProcessDirectory(string directory, string source)
        {
            var relativeDir = Path.GetRelativePath(Paths.BepInExRootPath, directory);

            Log.LogDebug($"Searching {relativeDir}");

            Dictionary<string, OverrideHolder> localMap = new();

            foreach (var icon in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
            {
                var key = Path.GetRelativePath(directory, Path.ChangeExtension(icon, null))
                    .Replace(Path.DirectorySeparatorChar, '/');
                var itemName = Path.GetFileNameWithoutExtension(icon);

                Log.LogDebug($"[{source}] Reading {key}.png");

                var data = File.ReadAllBytes(icon);

                var sprite = SpriteUtils.GetSprite(data);

                var texture = sprite.texture;
                if (texture.width != texture.height)
                {
                    Destroy(sprite);
                    Destroy(texture);
                    Log.LogError($"[{source}] Expected Icon {itemName}.png was not square!");
                }
                else
                {
                    sprite.name = sprite.texture.name = $"{nameof(RuntimeIcons)}.{itemName}";
                    var holder = new OverrideHolder
                    {
                        OverrideSprite = sprite,
                        Source = source
                    };
                    localMap[key] = holder;
                }
            }

            foreach (var jsonPath in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                var key = Path.GetRelativePath(directory, Path.ChangeExtension(jsonPath, null))
                    .Replace(Path.DirectorySeparatorChar, '/');

                Log.LogDebug($"[{source}] Reading {key}.json");

                try
                {
                    using var file = File.OpenText(jsonPath);
                    using var reader = new JsonTextReader(file);

                    var root = (JObject)JToken.ReadFrom(reader);

                    ProcessOverride(localMap, key, source, root);
                }
                catch (Exception ex)
                {
                    Log.LogError($"[{source}] Exception reading {key}.json\n{ex}");
                }
            }

            var overridesPath = Path.Combine(directory, "overrides.json");

            if (File.Exists(overridesPath))
            {
                Log.LogDebug($"[{source}] Found overrides.json");
                try
                {
                    using var file = File.OpenText(overridesPath);
                    using var reader = new JsonTextReader(file);

                    var root = (JObject)JToken.ReadFrom(reader);

                    foreach (var property in root.Properties())
                    {
                        var key = property.Name;

                        if (property.Value.Type != JTokenType.Object)
                        {
                            Log.LogWarning(
                                $"[{source}] overrides.json Key {key} has wrong type={property.Value.Type} Expected={JTokenType.Object}");
                            continue;
                        }

                        var value = (JObject)property.Value;

                        ProcessOverride(localMap, key, source, value);
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError($"[{source}] Exception reading overrides.json\n{ex}");
                }
            }
            
            Log.LogDebug($"[{source}] Applying overrides");
            
            foreach (var pair in localMap)
            {
                if (!pair.Key.Contains('/'))
                    continue;

                if (OverrideMap.TryGetValue(pair.Key, out var old))
                    if (old.Priority > pair.Value.Priority)
                        continue;

                Log.LogDebug($"[{source}] Overriding {pair.Key} with priority {pair.Value.Priority}");

                OverrideMap[pair.Key] = pair.Value;
            }
        }

        void ProcessOverride(Dictionary<string, OverrideHolder> localMap, string key, string source, JObject value)
        {
            if (!localMap.TryGetValue(key, out var holder))
            {
                holder = new OverrideHolder();
                holder.Source = source;
                localMap[key] = holder;
            }

            JToken token;

            if (value.TryGetValue("priority", out token) &&
                token.Type == JTokenType.Integer)
                holder.Priority = token.Value<int>();

            if (value.TryGetValue("item_rotation", out token) &&
                token.Type == JTokenType.Array)
                try
                {
                    var array = (List<float>)JsonConvert.DeserializeObject(token.ToString(), typeof(List<float>));
                    if (array.Count == 3) holder.ItemRotation = new Vector3(array[0], array[1], array[2]);
                }
                catch
                {
                }

            if (value.TryGetValue("stage_rotation", out token) &&
                token.Type == JTokenType.Array)
                try
                {
                    var array = (List<float>)JsonConvert.DeserializeObject(token.ToString(), typeof(List<float>));
                    if (array.Count == 3) holder.StageRotation = new Vector3(array[0], array[1], array[2]);
                }
                catch
                {
                }

            if (value.TryGetValue("icon_path", out token) &&
                token.Type == JTokenType.String)
            {
                var overrideKey = token.Value<string>()
                    .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLower();

                if (localMap.TryGetValue(overrideKey, out var target))
                {
                    if (target.OverrideSprite)
                        holder.OverrideSprite = target.OverrideSprite;
                    else
                        Log.LogWarning($"Key {overrideKey} is not a file in {source}");
                }
                else
                {
                    Log.LogWarning($"Key {overrideKey} does not exist in {source}");
                }
            }
        }
    }
}