using AssetStudio;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AssetStudioCLI
{
    internal enum HelpGroups
    {
        General,
        Convert,
        Logger,
        FBX,
        Filter,
        Advanced,
    }

    internal enum WorkMode
    {
        Extract,
        Export,
        ExportRaw,
        Dump,
        Info,
        SplitObjects,
        Animator,
    }

    internal static class Exporter
    {
        #region TRY EXPORT
        public static string FixFileName(string str)
        {
            return str.Length >= 260
                ? Path.GetRandomFileName()
                : Path.GetInvalidFileNameChars().Aggregate(str, (current, c) => current.Replace(c, '_'));
        }
        private static bool CanWrite(string fullPath, string dir, bool canOverwrite)
        {
            if (!canOverwrite && File.Exists(fullPath))
                return false;
            Directory.CreateDirectory(dir);
            return true;
        }

        private static bool TryExportFile(string dir, AssetItem item, string extension, out string fullPath, string mode = "Export")
        {
            var fileName = FixFileName(item.Text);
            fileName = $"{fileName} @{item.m_PathID}";

            fullPath = Path.Combine(dir, fileName + extension);

            fullPath = Path.Combine(dir, fileName + item.UniqueID + extension);
            if (CanWrite(fullPath, dir, true))
            {
                return true;
            }

            Debug.LogError($"{mode} error. File \"{fullPath}\" already exist");
            return false;
        }
        #endregion

        #region EXPORT VideoClip
        private static bool ExportVideoClip(AssetItem item, string exportPath)
        {
            var m_VideoClip = (AssetStudio.VideoClip)item.Asset;
            if (m_VideoClip.m_ExternalResources.m_Size > 0)
            {
                if (!TryExportFile(exportPath, item, Path.GetExtension(m_VideoClip.m_OriginalPath), out var exportFullPath))
                    return false;

                var sb = new StringBuilder();
                sb.AppendLine($"VideoClip format: {m_VideoClip.m_Format}");
                sb.AppendLine($"VideoClip width: {m_VideoClip.Width}");
                sb.AppendLine($"VideoClip height: {m_VideoClip.Height}");
                sb.AppendLine($"VideoClip frame rate: {m_VideoClip.m_FrameRate:.0##}");
                sb.AppendLine($"VideoClip split alpha: {m_VideoClip.m_HasSplitAlpha}");
                Debug.Log(sb.ToString());

                m_VideoClip.m_VideoData.WriteData(exportFullPath);
                Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
                return true;
            }
            return false;
        }

        private static byte[] ExportVideoClip(AssetStudio.Object item)
        {
            byte[] exported = null;
            var m_VideoClip = (AssetStudio.VideoClip)item;
            if (m_VideoClip.m_ExternalResources.m_Size > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"VideoClip format: {m_VideoClip.m_Format}");
                sb.AppendLine($"VideoClip width: {m_VideoClip.Width}");
                sb.AppendLine($"VideoClip height: {m_VideoClip.Height}");
                sb.AppendLine($"VideoClip frame rate: {m_VideoClip.m_FrameRate:.0##}");
                sb.AppendLine($"VideoClip split alpha: {m_VideoClip.m_HasSplitAlpha}");
                Debug.Log(sb.ToString());

                exported = m_VideoClip.m_VideoData.GetData();
            }

            Debug.Log($"{item.type} ({item.m_PathID}) export status: {exported != null}");
            return exported;
        }
        #endregion

        #region EXPORT MovieTexture
        private static bool ExportMovieTexture(AssetItem item, string exportPath)
        {
            var m_MovieTexture = (AssetStudio.MovieTexture)item.Asset;
            if (!TryExportFile(exportPath, item, ".ogv", out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, m_MovieTexture.m_MovieData);

            Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
            return true;
        }

        private static byte[] ExportMovieTexture(AssetStudio.Object item)
        {
            byte[] exported = null;

            var m_MovieTexture = (AssetStudio.MovieTexture)item;

            exported = m_MovieTexture.m_MovieData;

            Debug.Log($"{item.type} ({item.m_PathID}) export status: {exported != null}");
            return exported;
        }
        #endregion

        #region EXPORT Shader
        private static bool ExportShader(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".shader", out var exportFullPath))
                return false;
            var m_Shader = (AssetStudio.Shader)item.Asset;
            var str = m_Shader.Convert();
            File.WriteAllText(exportFullPath, str);

            Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
            return true;
        }

        private static byte[] ExportShader(AssetStudio.Object item)
        {
            byte[] utf8Bytes = null;

            var m_Shader = (AssetStudio.Shader)item;
            var str = m_Shader.Convert();
            utf8Bytes = Encoding.UTF8.GetBytes(str);

            Debug.Log($"{item.type} ({item.m_PathID}) export status: {utf8Bytes != null}");
            
            return utf8Bytes;
        }

        #endregion

        #region EXPORT TextAsset
        private static bool ExportTextAsset(AssetItem item, string exportPath)
        {
            var m_TextAsset = (AssetStudio.TextAsset)item.Asset;
            var extension = ".txt";

            if (Path.HasExtension(m_TextAsset.m_Name))
            {
                extension = "";
            }
            else
            {
                var extFromContainer = Path.GetExtension(item.Container);
                if (!string.IsNullOrEmpty(extFromContainer))
                {
                    extension = extFromContainer;
                }
            }

            if (!TryExportFile(exportPath, item, extension, out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, m_TextAsset.m_Script);

            Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
            return true;
        }

        private static byte[] ExportTextAsset(AssetStudio.Object item)
        {
            byte[] exported = null;

            var m_TextAsset = (AssetStudio.TextAsset)item;

            exported = m_TextAsset.m_Script;

            Debug.Log($"{item.type} ({item.m_PathID}) export status: {exported != null}");
            return exported;
        }
        #endregion

        #region EXPORT MonoBehaviour
        private static bool ExportMonoBehaviour(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".json", out var exportFullPath))
                return false;
            var m_MonoBehaviour = (AssetStudio.MonoBehaviour)item.Asset;
            var type = m_MonoBehaviour.ToType();
            if (type == null)
            {
                var m_Type = m_MonoBehaviour.ConvertToTypeTree();
                type = m_MonoBehaviour.ToType(m_Type);
            }
            if (type != null)
            {
                var str = JsonConvert.SerializeObject(type, Formatting.Indented);
                File.WriteAllText(exportFullPath, str);

                Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
                return true;
            }
            return false;
        }

        private static byte[] ExportMonoBehaviour(AssetStudio.Object item)
        {
            byte[] exported = null;
            var m_MonoBehaviour = (AssetStudio.MonoBehaviour)item;
            var type = m_MonoBehaviour.ToType();

            if (type == null)
            {
                var m_Type = m_MonoBehaviour.ConvertToTypeTree();
                type = m_MonoBehaviour.ToType(m_Type);
            }

            if (type != null)
            {
                var str = JsonConvert.SerializeObject(type, Formatting.Indented);
                exported = Encoding.UTF8.GetBytes(str);
            }

            Debug.Log($"{item.type} ({item.m_PathID}) export status: {exported != null}");
            return exported;
        }
        #endregion

        #region EXPORT Font
        private static bool ExportFont(AssetItem item, string exportPath)
        {
            var m_Font = (AssetStudio.Font)item.Asset;
            if (m_Font.m_FontData != null)
            {
                var extension = ".ttf";
                if (m_Font.m_FontData[0] == 79 && m_Font.m_FontData[1] == 84 && m_Font.m_FontData[2] == 84 && m_Font.m_FontData[3] == 79)
                {
                    extension = ".otf";
                }
                if (!TryExportFile(exportPath, item, extension, out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_Font.m_FontData);

                Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
                return true;
            }
            return false;
        }

        private static byte[] ExportFont(AssetStudio.Object item)
        {
            byte[] exported = null;
            var m_Font = (AssetStudio.Font)item;

            if (m_Font.m_FontData != null)
            {
                exported = m_Font.m_FontData;
            }

            Debug.Log($"{item.type} ({item.m_PathID}) export status: {exported != null}");
            return exported;
        }
        #endregion

        #region EXPORT Mesh
        private static bool ExportMesh(AssetItem item, string exportPath)
        {
            var m_Mesh = (AssetStudio.Mesh)item.Asset;
            m_Mesh.ProcessData();

            if (m_Mesh.m_VertexCount <= 0)
                return false;
            if (!TryExportFile(exportPath, item, ".obj", out var exportFullPath))
                return false;

            var sb = new StringBuilder();
            sb.AppendLine("g " + m_Mesh.m_Name);

            #region Vertices
            if (m_Mesh.m_Vertices == null || m_Mesh.m_Vertices.Length == 0)
            {
                return false;
            }

            int c = 3;
            if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
            {
                c = 4;
            }

            for (int v = 0; v < m_Mesh.m_VertexCount; v++)
            {
                sb.Append($"v {-m_Mesh.m_Vertices[v * c]} {m_Mesh.m_Vertices[v * c + 1]} {m_Mesh.m_Vertices[v * c + 2]}\r\n");
            }
            #endregion

            #region UV
            if (m_Mesh.m_UV0?.Length > 0)
            {
                c = 4;
                if (m_Mesh.m_UV0.Length == m_Mesh.m_VertexCount * 2)
                {
                    c = 2;
                }
                else if (m_Mesh.m_UV0.Length == m_Mesh.m_VertexCount * 3)
                {
                    c = 3;
                }

                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vt {0} {1}\r\n", m_Mesh.m_UV0[v * c], m_Mesh.m_UV0[v * c + 1]);
                }
            }
            #endregion

            #region Normals
            if (m_Mesh.m_Normals?.Length > 0)
            {
                if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
                {
                    c = 3;
                }
                else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
                {
                    c = 4;
                }

                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vn {0} {1} {2}\r\n", -m_Mesh.m_Normals[v * c], m_Mesh.m_Normals[v * c + 1], m_Mesh.m_Normals[v * c + 2]);
                }
            }
            #endregion

            #region Face
            int sum = 0;
            for (var i = 0; i < m_Mesh.m_SubMeshes.Count; i++)
            {
                sb.AppendLine($"g {m_Mesh.m_Name}_{i}");
                int indexCount = (int)m_Mesh.m_SubMeshes[i].indexCount;
                var end = sum + indexCount / 3;
                for (int f = sum; f < end; f++)
                {
                    sb.AppendFormat("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\r\n", m_Mesh.m_Indices[f * 3 + 2] + 1, m_Mesh.m_Indices[f * 3 + 1] + 1, m_Mesh.m_Indices[f * 3] + 1);
                }

                sum = end;
            }
            #endregion

            sb.Replace("NaN", "0");
            File.WriteAllText(exportFullPath, sb.ToString());

            sb.Clear();
            Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
            return true;
        }

        private static byte[] ExportMesh(AssetStudio.Object item)
        {
            byte[] exported = null;

            var m_Mesh = (AssetStudio.Mesh)item;
            m_Mesh.ProcessData();

            if (m_Mesh.m_VertexCount <= 0)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("g " + m_Mesh.m_Name);

            #region Vertices
            if (m_Mesh.m_Vertices == null || m_Mesh.m_Vertices.Length == 0)
            {
                return null;
            }

            int c = 3;
            if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
            {
                c = 4;
            }

            for (int v = 0; v < m_Mesh.m_VertexCount; v++)
            {
                sb.Append($"v {-m_Mesh.m_Vertices[v * c]} {m_Mesh.m_Vertices[v * c + 1]} {m_Mesh.m_Vertices[v * c + 2]}\r\n");
            }
            #endregion

            #region UV
            if (m_Mesh.m_UV0?.Length > 0)
            {
                c = 4;
                if (m_Mesh.m_UV0.Length == m_Mesh.m_VertexCount * 2)
                {
                    c = 2;
                }
                else if (m_Mesh.m_UV0.Length == m_Mesh.m_VertexCount * 3)
                {
                    c = 3;
                }

                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vt {0} {1}\r\n", m_Mesh.m_UV0[v * c], m_Mesh.m_UV0[v * c + 1]);
                }
            }
            #endregion

            #region Normals
            if (m_Mesh.m_Normals?.Length > 0)
            {
                if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
                {
                    c = 3;
                }
                else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
                {
                    c = 4;
                }

                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vn {0} {1} {2}\r\n", -m_Mesh.m_Normals[v * c], m_Mesh.m_Normals[v * c + 1], m_Mesh.m_Normals[v * c + 2]);
                }
            }
            #endregion

            #region Face
            int sum = 0;
            for (var i = 0; i < m_Mesh.m_SubMeshes.Count; i++)
            {
                sb.AppendLine($"g {m_Mesh.m_Name}_{i}");
                int indexCount = (int)m_Mesh.m_SubMeshes[i].indexCount;
                var end = sum + indexCount / 3;
                for (int f = sum; f < end; f++)
                {
                    sb.AppendFormat("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\r\n", m_Mesh.m_Indices[f * 3 + 2] + 1, m_Mesh.m_Indices[f * 3 + 1] + 1, m_Mesh.m_Indices[f * 3] + 1);
                }

                sum = end;
            }
            #endregion

            sb.Replace("NaN", "0");

            var str = sb.ToString();
            exported = Encoding.UTF8.GetBytes(str);

            sb.Clear();
            Debug.Log($"{item.type} ({item.m_PathID}) export status: {exported != null}");
            return exported;
        }
        #endregion

        #region EXPORT RAW
        public static bool ExportRawFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".dat", out var exportFullPath, mode: "ExportRaw"))
                return false;

            switch (item.Asset)
            {
                case AssetStudio.Texture2D m_Texture2D:
                    if (!string.IsNullOrEmpty(m_Texture2D.m_StreamData?.path))
                    {
                        m_Texture2D.image_data.WriteData(exportFullPath.Replace(".dat", "_data.dat"));
                    }
                    break;
                case AssetStudio.AudioClip m_AudioClip:
                    if (!string.IsNullOrEmpty(m_AudioClip.m_Source))
                    {
                        m_AudioClip.m_AudioData.WriteData(exportFullPath.Replace(".dat", "_data.dat"));
                    }
                    break;
                case AssetStudio.VideoClip m_VideoClip:
                    if (!string.IsNullOrEmpty(m_VideoClip.m_ExternalResources.m_Source))
                    {
                        m_VideoClip.m_VideoData.WriteData(exportFullPath.Replace(".dat", "_data.dat"));
                    }
                    break;
                case AssetStudio.MonoBehaviour m_MonoBehaviour:
                    var reader = m_MonoBehaviour.reader;
                    reader.Reset();
                    var assetData = reader.ReadBytes(28); //PPtr<GameObject> m_GameObject, m_Enabled, PPtr<MonoScript>
                    var assetNameLen = reader.ReadInt32();
                    reader.Position -= 4;
                    var assetNameBytes = reader.ReadBytes(assetNameLen + 4);
                    if (assetNameLen > 0)
                        reader.AlignStream();
                    var arrayLen = reader.ReadInt32();
                    if (arrayLen <= 0 || arrayLen > reader.Remaining)
                        break;
                    using (var outStream = new FileStream(exportFullPath.Replace(".dat", "_extracted.dat"), FileMode.Create))
                    {
                        reader.BaseStream.CopyTo(outStream, size: arrayLen);
                    }
                    using (var outStream = new FileStream(exportFullPath, FileMode.Create))
                    {
                        outStream.Write(assetData, 0, assetData.Length);
                        outStream.Write(assetNameBytes, 0, assetNameBytes.Length);
                        if (reader.Remaining > 0)
                            reader.BaseStream.CopyTo(outStream, size: reader.Remaining);
                    }
                    Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
                    return true;
            }
            File.WriteAllBytes(exportFullPath, item.Asset.GetRawData());

            Debug.Log($"{item.TypeString} \"{item.Text}\" exported to \"{exportFullPath}\"");
            return true;
        }

        public static byte[] ExportRawFile(AssetStudio.Object item)
        {
            switch (item)
            {
                case AssetStudio.Texture2D m_Texture2D:
                    if (!string.IsNullOrEmpty(m_Texture2D.m_StreamData?.path))
                    {
                        string utf8String = Encoding.UTF8.GetString(m_Texture2D.image_data.GetData());
                        // Debug.Log($"Founded m_Texture2D: {utf8String}");
                    }
                    break;
                case AssetStudio.AudioClip m_AudioClip:
                    if (!string.IsNullOrEmpty(m_AudioClip.m_Source))
                    {
                        string utf8String = Encoding.UTF8.GetString(m_AudioClip.m_AudioData.GetData());
                        // Debug.Log($"Founded m_AudioClip: {utf8String}");
                    }
                    break;
                case AssetStudio.VideoClip m_VideoClip:
                    if (!string.IsNullOrEmpty(m_VideoClip.m_ExternalResources.m_Source))
                    {
                        string utf8String = Encoding.UTF8.GetString(m_VideoClip.m_VideoData.GetData());
                        // Debug.Log($"Founded m_VideoClip: {utf8String}");
                    }
                    break;
                case AssetStudio.MonoBehaviour m_MonoBehaviour:
                    var reader = m_MonoBehaviour.reader;
                    reader.Reset();
                    var assetData = reader.ReadBytes(28);
                    var assetNameLen = reader.ReadInt32();
                    reader.Position -= 4;
                    var assetNameBytes = reader.ReadBytes(assetNameLen + 4);
                    if (assetNameLen > 0) reader.AlignStream();
                    var arrayLen = reader.ReadInt32();
                    if (arrayLen <= 0 || arrayLen > reader.Remaining) break;

                    using (var outStream = new MemoryStream())
                    {
                        reader.BaseStream.CopyTo(outStream, size: arrayLen);
                        string utf8String = Encoding.UTF8.GetString(outStream.ToArray());
                        Debug.Log($"Founded m_MonoBehaviour, extracted: {utf8String}");
                    }

                    using (var outStream = new MemoryStream())
                    {
                        outStream.Write(assetData, 0, assetData.Length);
                        outStream.Write(assetNameBytes, 0, assetNameBytes.Length);
                        if (reader.Remaining > 0) reader.BaseStream.CopyTo(outStream, size: reader.Remaining);
                        string utf8String = Encoding.UTF8.GetString(outStream.ToArray());
                        Debug.Log($"Founded m_MonoBehaviour, final: {utf8String}");
                    }
                    return assetData;
            }

            return item.GetRawData();
        }
        #endregion

        #region EXPORT DUMP
        public static bool ExportDumpFile(AssetItem item, string exportPath = null)
        {
            if (!TryExportFile(exportPath, item, ".txt", out var exportFullPath, mode: "Dump"))
                return false;

            string str = null;

            str = item.Asset.Dump();

            if (string.IsNullOrEmpty(str) && item.Asset is AssetStudio.MonoBehaviour m_MonoBehaviour)
            {
                var m_Type = m_MonoBehaviour.ConvertToTypeTree();
                str = m_MonoBehaviour.Dump(m_Type);
            }

            if (string.IsNullOrEmpty(str))
            {
                str = item.Asset.DumpObject();
            }

            if (!string.IsNullOrEmpty(str))
            {
                File.WriteAllText(exportFullPath, str);
                Debug.Log($"{item.TypeString} \"{item.Text}\" saved to \"{exportFullPath}\"");
                return true;
            }

            return false;
        }

        public static byte[] ExportDumpFile(AssetStudio.Object item)
        {
            byte[] utf8Bytes = null;
            Debug.Log($"Default dumping...");
            var str = item.Dump();
            if (str == null && item is AssetStudio.MonoBehaviour m_MonoBehaviour)
            {
                Debug.Log($"MonoBehaviour refactoring...");
                var m_Type = m_MonoBehaviour.ConvertToTypeTree();
                str = m_MonoBehaviour.Dump(m_Type);
            }
            if (string.IsNullOrEmpty(str))
            {
                Debug.Log($"Missing data, dumping object...");
                str = item.DumpObject();
            }
            if (str == null)
            {
                Debug.LogWarning($"ExportDumpFile null...");
            }

            utf8Bytes = Encoding.UTF8.GetBytes(str);
            return utf8Bytes;
        }
        #endregion

        #region MAGIC EXPORT
        public static bool ExportConvertFile(AssetItem item, string exportPath = null)
        {
            switch (item.Type)
            {
                case ClassIDType.Texture2D:
                case ClassIDType.Texture2DArray:
                case ClassIDType.Sprite:
                case ClassIDType.AudioClip:
                    throw new System.NotImplementedException();
                case ClassIDType.VideoClip:
                    return ExportVideoClip(item, exportPath);
                case ClassIDType.MovieTexture:
                    return ExportMovieTexture(item, exportPath);
                case ClassIDType.Shader:
                    return ExportShader(item, exportPath);
                case ClassIDType.TextAsset:
                    return ExportTextAsset(item, exportPath);
                case ClassIDType.MonoBehaviour:
                    return ExportMonoBehaviour(item, exportPath);
                case ClassIDType.Font:
                    return ExportFont(item, exportPath);
                case ClassIDType.Mesh:
                    return ExportMesh(item, exportPath);
                default:
                    return ExportRawFile(item, exportPath);
            }
        }

        public static byte[] ExportConvertFile(AssetStudio.Object item)
        {
            switch (item.type)
            {
                case ClassIDType.VideoClip:
                    return ExportVideoClip(item);
                case ClassIDType.MovieTexture:
                    return ExportMovieTexture(item);
                case ClassIDType.Shader:
                    return ExportShader(item);
                case ClassIDType.TextAsset:
                    return ExportTextAsset(item);
                case ClassIDType.MonoBehaviour:
                    return ExportMonoBehaviour(item);
                case ClassIDType.Font:
                    return ExportFont(item);
                case ClassIDType.Mesh:
                    return ExportMesh(item);

                // NotImplementedException
                case ClassIDType.Texture2D:
                    return ExportRawFile(item);

                case ClassIDType.Texture2DArray:
                case ClassIDType.Sprite:
                case ClassIDType.AudioClip:
                // UnknownTypeException
                default:
                    return null; 
            }
        }
        #endregion

        #region UNACTIVE ZONE

        #region EXPORT GameObject
        /*
        public static void ExportGameObject(AssetStudio.GameObject gameObject, string exportPath, List<AssetItem> animationList = null)
        {
            var convert = animationList != null
                ? new ModelConverter(gameObject, CLIOptions.o_imageFormat.Value, animationList.Select(x => (AssetStudio.AnimationClip)x.Asset).ToList())
                : new ModelConverter(gameObject, CLIOptions.o_imageFormat.Value);
            var modelName = FixFileName(gameObject.m_Name);
            var exportFullPath = Path.Combine(exportPath, "FBX_GameObjects", modelName, modelName + ".fbx");
            if (File.Exists(exportFullPath))
            {
                exportFullPath = Path.Combine(exportPath, $"{modelName}_{gameObject.GetHashCode():X}", modelName + ".fbx");
            }
            ExportFbx(convert, exportFullPath);
        }
        */
        #endregion

        #region EXPORT Animator
        /*
        public static bool ExportAnimator(AssetItem item, string exportPath, List<AssetItem> animationList = null)
        {
            var exportFullPath = Path.Combine(exportPath, "FBX_Animator", item.Text, item.Text + ".fbx");
            if (File.Exists(exportFullPath))
            {
                exportFullPath = Path.Combine(exportPath, item.Text + item.UniqueID, item.Text + ".fbx");
            }
            var m_Animator = (AssetStudio.Animator)item.Asset;
            var convert = animationList != null
                ? new ModelConverter(m_Animator, CLIOptions.o_imageFormat.Value, animationList.Select(x => (AssetStudio.AnimationClip)x.Asset).ToList())
                : new ModelConverter(m_Animator, CLIOptions.o_imageFormat.Value);
            ExportFbx(convert, exportFullPath);
            return true;
        }
        */
        #endregion

        #region EXPORT FBIx
        /*
        private static void ExportFbx(IImported convert, string exportPath)
        {
            var fbxSettings = new Fbx.Settings
            {
                BoneSize = CLIOptions.o_fbxBoneSize.Value,
                ScaleFactor = CLIOptions.o_fbxScaleFactor.Value,
                ExportAllUvsAsDiffuseMaps = CLIOptions.f_fbxUvsAsDiffuseMaps.Value,
                ExportAnimations = CLIOptions.o_fbxAnimMode.Value != AnimationExportMode.Skip,
            };
            ModelExporter.ExportFbx(exportPath, convert, fbxSettings);
        }
        */
        #endregion

        #endregion
    }
}