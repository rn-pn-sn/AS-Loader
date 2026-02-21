using AssetStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using static AssetStudio.JsonConverterHelper;

/// <summary>
/// AssetStudioBundle logic identical to AssetBundle.
/// </summary>
/// <remarks>
/// For load/unload AssetStudioBundle use AssetStudioLoader!
/// </remarks>
public class AssetStudioBundle
{
    private BundleFile bundle;
    private string cache_key;

    public bool isLoaded = false;
    public bool MeshLazyLoad = true;
    public bool LoadViaTypeTree = true;

    public List<SerializedFile> AssetsFileList = new List<SerializedFile>();
    public HashSet<string> assetsFileListHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, BinaryReader> resourceFileReaders = new ConcurrentDictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);

    public AssetStudioBundle(FileReader reader, string cache = null, bool isMultiBundle = false)
    {
        bundle = new BundleFile(reader, isMultiBundle);
        cache_key = cache;
        isLoaded = LoadBundleFiles(reader);
        AssetStudioLogger.Log($"[AssetStudioBundle] {reader.FileName} isLoaded:{isLoaded}", true);
    }

    public bool LoadBundleFiles(FileReader reader)
    {
        foreach (var file in bundle.fileList)
        {
            if (file.stream == null)
                continue;

            file.stream.Position = 0;
            string dummyPath = null;
            if (reader.FullPath != null)
            {
                dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
            }
            var subReader = new FileReader(dummyPath, file.stream);
            if (subReader.FileType == FileType.AssetsFile)
            {
                if (!LoadAssetsFromMemory(subReader, reader.FullPath, bundle.m_Header.unityRevision))
                    return false;
            }
            else
            {
                resourceFileReaders.TryAdd(file.fileName, subReader);
            }
        }
        ReadAssets();
        ProcessAssets();
        return true;
    }

    private bool LoadAssetsFromMemory(FileReader reader, string originalPath, UnityVersion assetBundleUnityVer = null)
    {
        if (!assetsFileListHash.Contains(reader.FileName))
        {
            try
            {
                var assetsFile = new SerializedFile(reader, this);
                assetsFile.originalPath = originalPath;
                if (assetBundleUnityVer != null && assetsFile.header.m_Version < SerializedFileFormatVersion.Unknown_7)
                {
                    assetsFile.version = assetBundleUnityVer;
                }
                CheckStrippedVersion(assetsFile, assetBundleUnityVer);
                AssetsFileList.Add(assetsFile);
                assetsFileListHash.Add(assetsFile.fileName);
            }
            catch (NotSupportedException e)
            {
                Debug.LogError(e.Message);
                reader.Dispose();
                return false;
            }
            catch (Exception e)
            {
                AssetStudioLogger.Warn($"Failed to read assets file \"{reader.FullPath}\" from {Path.GetFileName(originalPath)}\n{e}");
                resourceFileReaders.TryAdd(reader.FileName, reader);
            }
        }
        else
        {
            AssetStudioLogger.Log($"Skipping \"{originalPath}\" ({reader.FileName})", true);
        }
        return true;
    }

    private void CheckStrippedVersion(SerializedFile assetsFile, UnityVersion bundleUnityVer = null)
    {
        if (assetsFile.version.IsStripped)
        {
            var msg = "The asset's Unity version has been stripped.";
            if (bundleUnityVer != null && !bundleUnityVer.IsStripped)
                msg += $"\n\nAssumed Unity version based on asset bundle: {bundleUnityVer}";
            throw new NotSupportedException(msg);
        }
    }

    private void ReadAssets()
    {
        AssetStudioLogger.Log("[AssetStudioBundle] Read assets...", true);

        var jsonOptions = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new ByteArrayConverter(),
                new PPtrConverter()
            },

            // To include fields, you can use:
            TypeNameHandling = TypeNameHandling.All, // This will include type information and fields

            // Or use a simpler approach with ObjectCreationHandling:
            ObjectCreationHandling = ObjectCreationHandling.Reuse,

            // Handle special floating point values
            FloatParseHandling = FloatParseHandling.Double,

            // To make property names case insensitive
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            // OR for exact case matching but case-insensitive deserialization:
            // Use a custom contract resolver or set NullValueHandling and DefaultValueHandling

            // For case-insensitive property matching during deserialization
            Error = (sender, args) =>
            {
                // Handle errors for missing properties
                args.ErrorContext.Handled = true;
            }
        };

        var progressCount = AssetsFileList.Sum(x => x.m_Objects.Count);

        foreach (var assetsFile in AssetsFileList)
        {
            AssetsFile = assetsFile;
            foreach (var objectInfo in assetsFile.m_Objects)
            {
                var objectReader = new ObjectReader(assetsFile.reader, assetsFile, objectInfo);
                try
                {
                    AssetStudio.Object obj = null;
                    switch (objectReader.type)
                    {
                        case ClassIDType.Animation:
                            obj = new AssetStudio.Animation(objectReader);
                            break;
                        case ClassIDType.AnimationClip:
                            obj = objectReader.serializedType?.m_Type != null && LoadViaTypeTree
                                ? new AssetStudio.AnimationClip(objectReader, TypeTreeHelper.ReadTypeByteArray(objectReader.serializedType.m_Type, objectReader), jsonOptions, objectInfo)
                                : new AssetStudio.AnimationClip(objectReader);
                            break;
                        case ClassIDType.Animator:
                            obj = new AssetStudio.Animator(objectReader);
                            break;
                        case ClassIDType.AnimatorController:
                            obj = new AssetStudio.AnimatorController(objectReader);
                            break;
                        case ClassIDType.AnimatorOverrideController:
                            obj = new AssetStudio.AnimatorOverrideController(objectReader);
                            break;
                        case ClassIDType.AssetBundle:
                            obj = new AssetStudio.AssetBundle(objectReader);
                            break;
                        case ClassIDType.AudioClip:
                            obj = new AssetStudio.AudioClip(objectReader);
                            break;
                        case ClassIDType.Avatar:
                            obj = new AssetStudio.Avatar(objectReader);
                            break;
                        case ClassIDType.BuildSettings:
                            obj = new AssetStudio.BuildSettings(objectReader);
                            break;
                        case ClassIDType.Font:
                            obj = new AssetStudio.Font(objectReader);
                            break;
                        case ClassIDType.GameObject:
                            obj = new AssetStudio.GameObject(objectReader);
                            break;
                        case ClassIDType.Material:
                            obj = objectReader.serializedType?.m_Type != null && LoadViaTypeTree
                                ? new AssetStudio.Material(objectReader, TypeTreeHelper.ReadTypeByteArray(objectReader.serializedType.m_Type, objectReader), jsonOptions)
                                : new AssetStudio.Material(objectReader);
                            break;
                        case ClassIDType.Mesh:
                            obj = new AssetStudio.Mesh(objectReader);
                            break;
                        case ClassIDType.MeshFilter:
                            obj = new AssetStudio.MeshFilter(objectReader);
                            break;
                        case ClassIDType.MeshRenderer:
                            obj = new AssetStudio.MeshRenderer(objectReader);
                            break;
                        case ClassIDType.MonoBehaviour:
                            obj = new AssetStudio.MonoBehaviour(objectReader);
                            break;
                        case ClassIDType.MonoScript:
                            obj = new AssetStudio.MonoScript(objectReader);
                            break;
                        case ClassIDType.MovieTexture:
                            obj = new AssetStudio.MovieTexture(objectReader);
                            break;
                        case ClassIDType.PlayerSettings:
                            obj = new AssetStudio.PlayerSettings(objectReader);
                            break;
                        case ClassIDType.PreloadData:
                            obj = new AssetStudio.PreloadData(objectReader);
                            break;
                        case ClassIDType.RectTransform:
                            obj = new AssetStudio.RectTransform(objectReader);
                            break;
                        case ClassIDType.Shader:
                            if (objectReader.version < 2021)
                                obj = new AssetStudio.Shader(objectReader);
                            break;
                        case ClassIDType.SkinnedMeshRenderer:
                            obj = new AssetStudio.SkinnedMeshRenderer(objectReader);
                            break;
                        case ClassIDType.Sprite:
                            obj = new AssetStudio.Sprite(objectReader);
                            break;
                        case ClassIDType.SpriteAtlas:
                            obj = new SpriteAtlas(objectReader);
                            break;
                        case ClassIDType.TextAsset:
                            obj = new AssetStudio.TextAsset(objectReader);
                            break;
                        case ClassIDType.Texture2D:
                            obj = objectReader.serializedType?.m_Type != null && LoadViaTypeTree
                                ? new AssetStudio.Texture2D(objectReader, TypeTreeHelper.ReadTypeByteArray(objectReader.serializedType.m_Type, objectReader), jsonOptions)
                                : new AssetStudio.Texture2D(objectReader);
                            break;
                        case ClassIDType.Texture2DArray:
                            obj = objectReader.serializedType?.m_Type != null && LoadViaTypeTree
                                ? new AssetStudio.Texture2DArray(objectReader, TypeTreeHelper.ReadTypeByteArray(objectReader.serializedType.m_Type, objectReader), jsonOptions)
                                : new AssetStudio.Texture2DArray(objectReader);
                            break;
                        case ClassIDType.Transform:
                            obj = new AssetStudio.Transform(objectReader);
                            break;
                        case ClassIDType.VideoClip:
                            obj = new VideoClip(objectReader);
                            break;
                        case ClassIDType.ResourceManager:
                            obj = new ResourceManager(objectReader);
                            break;
                        default:
                            obj = new AssetStudio.Object(objectReader);
                            break;
                    }
                    if (obj != null)
                    {
                        assetsFile.AddObject(obj);
                    }
                }
                catch (Exception e)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Unable to load object")
                        .AppendLine($"Assets {assetsFile.fileName}")
                        .AppendLine($"Path {assetsFile.originalPath}")
                        .AppendLine($"Type {objectReader.type}")
                        .AppendLine($"PathID {objectInfo.m_PathID}")
                        .Append(e);
                    AssetStudioLogger.Warn(sb.ToString());
                }
            }
        }
    }

    private void ProcessAssets()
    {
        AssetStudioLogger.Log("[AssetStudioBundle] Process assets...", true);

        foreach (var assetsFile in AssetsFileList)
        {
            foreach (var obj in assetsFile.Objects)
            {
                if (obj is AssetStudio.GameObject m_GameObject)
                {
                    foreach (var pptr in m_GameObject.m_Components)
                    {
                        if (pptr.TryGet(out var m_Component))
                        {
                            switch (m_Component)
                            {
                                case AssetStudio.Transform m_Transform:
                                    m_GameObject.m_Transform = m_Transform;
                                    break;
                                case AssetStudio.MeshRenderer m_MeshRenderer:
                                    m_GameObject.m_MeshRenderer = m_MeshRenderer;
                                    break;
                                case AssetStudio.MeshFilter m_MeshFilter:
                                    m_GameObject.m_MeshFilter = m_MeshFilter;
                                    break;
                                case AssetStudio.SkinnedMeshRenderer m_SkinnedMeshRenderer:
                                    m_GameObject.m_SkinnedMeshRenderer = m_SkinnedMeshRenderer;
                                    break;
                                case AssetStudio.Animator m_Animator:
                                    m_GameObject.m_Animator = m_Animator;
                                    break;
                                case AssetStudio.Animation m_Animation:
                                    m_GameObject.m_Animation = m_Animation;
                                    break;
                                case AssetStudio.MonoBehaviour m_MonoBehaviour:
                                    if (m_MonoBehaviour.m_Script.TryGet(out var m_Script))
                                    {
                                        switch (m_Script.m_ClassName)
                                        {
                                            case "CubismModel":
                                                if (m_GameObject.m_Transform == null)
                                                    break;
                                                m_GameObject.CubismModel = new CubismModel(m_GameObject)
                                                {
                                                    CubismModelMono = m_MonoBehaviour
                                                };
                                                break;
                                            case "CubismPhysicsController":
                                                if (m_GameObject.CubismModel != null)
                                                    m_GameObject.CubismModel.PhysicsController = m_MonoBehaviour;
                                                break;
                                            case "CubismFadeController":
                                                if (m_GameObject.CubismModel != null)
                                                    m_GameObject.CubismModel.FadeController = m_MonoBehaviour;
                                                break;
                                            case "CubismExpressionController":
                                                if (m_GameObject.CubismModel != null)
                                                    m_GameObject.CubismModel.ExpressionController = m_MonoBehaviour;
                                                break;
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
                else if (obj is AssetStudio.SpriteAtlas m_SpriteAtlas)
                {
                    foreach (var m_PackedSprite in m_SpriteAtlas.m_PackedSprites)
                    {
                        if (m_PackedSprite.TryGet(out var m_Sprite))
                        {
                            if (m_Sprite.m_SpriteAtlas.IsNull)
                            {
                                m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                            }
                            else if (m_Sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlasOld))
                            {
                                if (m_SpriteAtlasOld.m_IsVariant)
                                {
                                    m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                                }
                            }
                            else
                            {
                                AssetStudioLogger.Log($"\"{m_Sprite.m_Name}\": The actual SpriteAtlas PathID \"{m_SpriteAtlas.m_PathID}\" does not match the specified one \"{m_Sprite.m_SpriteAtlas.m_PathID}\".", true);
                                m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Return true if the AssetStudioBundle contains Unity Scene files
    /// </summary>
    public bool isStreamedSceneAssetBundle()
    {
        // TODO
        return false;
    }

    /// <summary>
    /// Check if an AssetStudioBundle contains a specific object.
    /// </summary>
    public bool Contains(string name)
    {
        // TODO
        return false;
    }

    /// <summary>
    /// Synchronously loads an Asset from the AssetBundle.
    /// </summary>
    public UnityEngine.Object LoadAsset(string name)
    {
        return LoadAsset(name, typeof(UnityEngine.Object));
    }

    public T LoadAsset<T>(string name) where T : UnityEngine.Object
    {
        return (T)LoadAsset(name, typeof(T));
    }

    #region LoadAsset UNSAFE

    /// <summary>
    /// Synchronously direct load an MonoBehaviour from the AssetBundle.
    /// </summary>
    /// <remarks>
    /// UNSAFE: expected only 1 object of the class, use only for simple and small bundles
    /// </remarks>
    public T LoadAsset<T>() where T : UnityEngine.Object
    {
        return (T)LoadAssetUnsafe(typeof(T));
    }

    private UnityEngine.Object LoadAssetUnsafe(Type type)
    {
        if (type == null)
        {
            throw new NullReferenceException("The input type cannot be null.");
        }

        bool direct = false;
        if (AssetsFileList != null)
        {
            List<byte[]> testarray = AssetStudioExporter.ExportAssets(AssetsFileList[0].Objects, AssetStudioCLI.WorkMode.Export);

            foreach (var cltype in Enum.GetNames(typeof(ClassIDType)))
            {
                if (type.Name == cltype)
                {
                    direct = true;
                    break;
                }
            }
        }

        if (direct)
        {
            switch (type)
            {
                case Type t when t == typeof(UnityEngine.TextAsset):
                    return LoadAssetUnsafeTextData(type);

                case Type t when t == typeof(UnityEngine.Texture2D):
                    return LoadAssetUnsafeTexture(type);

                default: return null; // NotImplementedException
            }
        } else
        {
            return LoadAssetUnsafeMono(type);
        }
    }

    private UnityEngine.Object LoadAssetUnsafeTextData(Type type)
    {
        foreach (var obj in AssetsFileList[0].Objects)
        {
            if (obj.type.ToString() == ClassIDType.TextAsset.ToString())
            {
                var m_TextAsset = (AssetStudio.TextAsset)obj;
                AssetStudioLogger.Log($"[AssetStudioBundle:LoadAssetUnsafeTextData] m_Name: {m_TextAsset.m_Name}", true);
                string s_data = Encoding.UTF8.GetString(m_TextAsset.m_Script);
                UnityEngine.TextAsset newAsset = new UnityEngine.TextAsset(s_data);
                return newAsset;
            }
        }
        return null;
    }

    private UnityEngine.Object LoadAssetUnsafeTexture(Type type)
    {
        foreach (var obj in AssetsFileList[0].Objects)
        {
            if (obj.type.ToString() == ClassIDType.Texture2D.ToString())
            {
                var m_Texture2D = (AssetStudio.Texture2D)obj;

                var sb = new StringBuilder();
                sb.AppendLine($"[AssetStudioBundle:LoadAssetUnsafeTexture] Converting {m_Texture2D.type} \"{m_Texture2D.m_Name}\" to {type}..");
                sb.AppendLine($"Width: {m_Texture2D.m_Width}");
                sb.AppendLine($"Height: {m_Texture2D.m_Height}");
                sb.AppendLine($"Format: {m_Texture2D.m_TextureFormat}");
                switch (m_Texture2D.m_TextureSettings.m_FilterMode)
                {
                    case 0: sb.AppendLine("Filter Mode: Point "); break;
                    case 1: sb.AppendLine("Filter Mode: Bilinear "); break;
                    case 2: sb.AppendLine("Filter Mode: Trilinear "); break;
                }
                sb.AppendLine($"Anisotropic level: {m_Texture2D.m_TextureSettings.m_Aniso}");
                sb.AppendLine($"Mip map bias: {m_Texture2D.m_TextureSettings.m_MipBias}");
                switch (m_Texture2D.m_TextureSettings.m_WrapMode)
                {
                    case 0: sb.AppendLine($"Wrap mode: Repeat"); break;
                    case 1: sb.AppendLine($"Wrap mode: Clamp"); break;
                }
                AssetStudioLogger.Log(sb.ToString(), true);

                var textureData = Texture2DConverter.ConvertTexture2DToUnityTexture(m_Texture2D);

                if (textureData == null)
                {
                    AssetStudioLogger.Error($"Export error. Failed to convert texture \"{m_Texture2D.m_Name}\" into image");
                }
                else
                {
                    return textureData;
                }
            }
        }

        return null;
    }

    private UnityEngine.Object LoadAssetUnsafeMono(Type type)
    {
        long m_PathID = 0;
        foreach (var obj in AssetsFileList[0].Objects) // step1: found MonoScript and install path
        {
            if (obj.type.ToString() == ClassIDType.MonoScript.ToString())
            {
                var m_MonoScript = (AssetStudio.MonoScript)obj;
                AssetStudioLogger.Log("[AssetStudioBundle:LoadAssetUnsafeMono] m_Name: " + m_MonoScript.m_Name, true);
                AssetStudioLogger.Log("[AssetStudioBundle:LoadAssetUnsafeMono] t_Name: " + type.Name, true);
                if(type.Name == m_MonoScript.m_Name)
                {
                    m_PathID = m_MonoScript.m_PathID;
                    AssetStudioLogger.Log("m_PathID: " + m_PathID, true);
                    break;
                }
            }
        }

        string s_data = null;
        foreach (var obj in AssetsFileList[0].Objects) // step2: found MonoBeh and make export
        {
            if (obj.type.ToString() == type.BaseType.Name)
            {
                if (type.BaseType == typeof(UnityEngine.MonoBehaviour))
                {
                    var m_MonoBehaviour = (AssetStudio.MonoBehaviour)obj;
                    AssetStudioLogger.Log("m_Script.m_PathID: " + m_MonoBehaviour.m_Script.m_PathID, true);
                    if (m_MonoBehaviour.m_Script.m_PathID == m_PathID)
                    {
                        var b_data = AssetStudioExporter.ExportAsset(obj, AssetStudioCLI.WorkMode.Export);
                        s_data = Encoding.UTF8.GetString(b_data);
                        break;
                    }
                }
            }
        }

        if (s_data == null) return null;

        // step3: deserialization
        try
        {
            object final = JsonConvert.DeserializeObject(s_data, type);
            AssetStudioLogger.Log($"{type.Name}: loaded success", true);
            return (UnityEngine.Object)final;
        }
        catch (Exception ex)
        {
            AssetStudioLogger.Error($"Deserialization failed: {ex.Message}");
            return null;
        }
    }

    #endregion

    public UnityEngine.Object LoadAsset(string name, Type type)
    {
        if (name == null)
        {
            throw new NullReferenceException("The input asset name cannot be null.");
        }

        if (name.Length == 0)
        {
            throw new ArgumentException("The input asset name cannot be empty.");
        }

        if (type == null)
        {
            throw new NullReferenceException("The input type cannot be null.");
        }

        if (AssetsFileList != null)
        {
            List<byte[]> testarray = AssetStudioExporter.ExportAssets(AssetsFileList[0].Objects, AssetStudioCLI.WorkMode.Export);

            bool direct = false;
            foreach(var cltype in Enum.GetNames(typeof(ClassIDType)))
            {
                if(type.Name == cltype)
                {
                    direct = true;
                    break;
                }
            }

            if (direct)
            { // direct load
                foreach (var obj in AssetsFileList[0].Objects)
                {
                    if (obj.type.ToString() == type.Name)
                    {
                        // TODO
                    }
                }
            } else
            {
                if (type.BaseType == typeof(UnityEngine.MonoBehaviour))
                {
                    // TODO
                }
            }
        }
        return null;
    }

    public UnityEngine.Object LoadAsset(string name, Type type, Type component)
    {
        // TODO
        return null;
    }

    /// <summary>
    /// Loads all Assets contained in the AssetStudioBundle synchronously.
    /// </summary>
    public UnityEngine.Object[] LoadAssetWithSubAssets(string name)
    {
        return LoadAssetWithSubAssets(name, typeof(UnityEngine.Object));
    }

    internal static T[] ConvertObjects<T>(UnityEngine.Object[] rawObjects) where T : UnityEngine.Object
    {
        if (rawObjects == null)
        {
            return null;
        }

        T[] array = new T[rawObjects.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = (T)rawObjects[i];
        }

        return array;
    }

    public T[] LoadAssetWithSubAssets<T>(string name) where T : UnityEngine.Object
    {
        return ConvertObjects<T>(LoadAssetWithSubAssets(name, typeof(T)));
    }

    /// <summary>
    /// Loads all Assets contained in the AssetStudioBundle synchronously.
    /// </summary>
    public UnityEngine.Object[] LoadAssetWithSubAssets(string name, Type type)
    {
        if (name == null)
        {
            throw new NullReferenceException("The input asset name cannot be null.");
        }

        if (name.Length == 0)
        {
            throw new ArgumentException("The input asset name cannot be empty.");
        }

        if (type == null)
        {
            throw new NullReferenceException("The input type cannot be null.");
        }

        //TODO

        return null;
    }

    /// <summary>
    /// Loads all Assets contained in the AssetStudioBundle synchronously.
    /// </summary>
    public UnityEngine.Object[] LoadAllAssets()
    {
        return LoadAllAssets(typeof(UnityEngine.Object));
    }

    public T[] LoadAllAssets<T>() where T : UnityEngine.Object
    {
        return ConvertObjects<T>(LoadAllAssets(typeof(T)));
    }

    /// <summary>
    /// Loads all Assets contained in the AssetStudioBundle synchronously.
    /// </summary>
    public UnityEngine.Object[] LoadAllAssets(Type type)
    {
        if (type == null)
        {
            throw new NullReferenceException("The input type cannot be null.");
        }

        //TODO

        return null;
    }

    /// <summary>
    /// Return all Asset names in the AssetStudioBundle.
    /// </summary>
    public string[] GetAllAssetNames()
    {
        //TODO
        return null;
    }

    /// <summary>
    /// Return all the names of Scenes in the AssetStudioBundle.
    /// </summary>
    /// <remarks>
    /// Unsafe: Scenes may not load reliably, recommended to use AssetBundle.
    /// </remarks>
    public string[] GetAllScenePaths()
    {
        //TODO
        return null;
    }

    /// <summary>
    /// Debug all objects in the AssetStudioBundle.
    /// </summary>
    public void DebugAllObjects()
    {
        if (AssetsFileList != null)
        {
            int i = 0;
            foreach (var asset in AssetsFileList)
            {
                AssetStudioLogger.Log($"General info AssetsFileList[{i}]: fullName:{asset.fullName ?? null} | fileName:{asset.fileName ?? null} | fileSizeH:{asset.header.m_FileSize} | m_Objects:{asset.m_Objects.Count} | m_Types:{asset.m_Types.Count} | Objects: {asset.Objects.Count}", true);

                if (asset.m_Objects != null)
                {
                    for (int o = 0; o < asset.m_Objects.Count; o++)
                    {
                        AssetStudioLogger.Log($"m_Objects[{o}]: typeID:{asset.m_Objects[o]} | classID:{asset.m_Objects[o].typeID} | bytes: {asset.m_Objects[o].byteSize}", true);
                            byte[] data = asset.Objects[o].GetRawData();
                            string utf8String = Encoding.UTF8.GetString(data);
                            AssetStudioLogger.Log($"RAWDATA: {utf8String}", true);
                            string dumped = asset.Objects[o].Dump();
                            AssetStudioLogger.Log($"DUMPED: {dumped}", true);
                    }
                }
            }
        }
        else
        {
            AssetStudioLogger.Warn("AssetsFileList null");
        }
    }

    /// <summary>
    /// Return cache_key in the AssetStudioBundle or null.
    /// </summary>
    public string GetCacheKey()
    {
        return cache_key;
    }

    /// <summary>
    /// Unload AssetStudioBundle if cached.
    /// </summary>
    public void Unload()
    {
        if(cache_key != null) AssetStudioLoader.Unload(cache_key);
    }
}