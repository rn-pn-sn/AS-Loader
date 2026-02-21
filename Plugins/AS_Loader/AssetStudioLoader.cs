using AssetStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

public class AssetStudioLoader
{
    private static Dictionary<string, AssetStudioBundle> bundleFiles = new Dictionary<string, AssetStudioBundle>();
    private static string TempCachePath = Application.persistentDataPath + "/temp_abs_ASloader";

    /// <summary>
    /// Synchronously loads an AssetStudioBundle from a file on disk. Caching by default.
    /// </summary>
    /// <param name="path">Path of the file on disk.</param>
    /// <param name="cache">Caching in memory.</param>
    /// <returns>Loaded AssetStudioBundle object or null if failed.</returns>
    public static AssetStudioBundle LoadFromFile(string path, bool cache = true)
    {
        AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromFile] Loading {Path.GetFileName(path)} bundle | caching: {cache}", false);

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            AssetStudioLogger.Warn($"[AssetStudioLoader:LoadFromFile] File not found: {path}");
            return null;
        }

        string cache_key = null;
        if (cache) cache_key = path;

        if (cache_key != null && bundleFiles.ContainsKey(cache_key))
        {
            AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromFile] Loading from cache: {Path.GetFileName(path)}", false);
            return bundleFiles[cache_key];
        }

        try
        {
            using (var reader = new FileReader(path))
            {
                var bundleFile = new AssetStudioBundle(reader, cache_key);

                if (bundleFile != null && cache_key != null)
                {
                    bundleFiles.Add(cache_key, bundleFile);
                    AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromFile] Successfully loaded and cached: {Path.GetFileName(path)}", false);
                }
                else if (bundleFile != null)
                {
                    AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromFile] Successfully loaded: {Path.GetFileName(path)}", false);
                }

                return bundleFile;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            AssetStudioLogger.Error($"[AssetStudioLoader:LoadFromFile] Access denied: {path}. Error: {ex.Message}");
        }
        catch (IOException ex)
        {
            AssetStudioLogger.Error($"[AssetStudioLoader:LoadFromFile] IO error while reading: {path}. Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            AssetStudioLogger.Error($"[AssetStudioLoader:LoadFromFile] Unexpected error loading: {path}. Error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Synchronously load an AssetStudioBundle from a memory region. Caching by default.
    /// </summary>
    /// <param name="binary">Array of bytes with the AssetBundle data.</param>
    /// <param name="cache">Caching in memory.</param>
    /// <returns>Loaded AssetStudioBundle object or null if failed.</returns>
    public static AssetStudioBundle LoadFromMemory(byte[] binary, bool cache = true)
    {
        string path;
        string cache_key = null;
        using (var sha256 = SHA256.Create())
        {
            if (!Directory.Exists(TempCachePath)) Directory.CreateDirectory(TempCachePath);

            byte[] hash = sha256.ComputeHash(binary, binary.Length / 2, Math.Min(10, binary.Length / 2));
            string safeFileName = BitConverter.ToString(hash)
                .Replace("-", "")
                .ToLowerInvariant()
                .Substring(0, 16);

            path = Path.Combine(TempCachePath, safeFileName);
        }
        if (cache) cache_key = path;

        if (cache_key != null && bundleFiles.ContainsKey(cache_key))
        {
            AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromMemory] Loading from cache: {Path.GetFileName(path)}", false);
            return bundleFiles[cache_key];
        }

        AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromMemory] Loading {Path.GetFileName(path)} bundle | caching: {cache_key: false}", false);
        if(!File.Exists(path)) File.WriteAllBytes(path, binary);

        AssetStudioBundle bundleFile = null;
        var reader = new FileReader(path);
        bundleFile = new AssetStudioBundle(reader, cache_key);
        if (bundleFile != null && cache_key != null) bundleFiles.Add(cache_key, bundleFile); else File.Delete(cache_key);

        reader.Dispose();
        return bundleFile;
    }

    /// <summary>
    /// Synchronously loads an AssetStudioBundle from a managed Stream. Caching by default.
    /// </summary>
    /// <param name="stream">The managed Stream object. Unity calls Read(), Seek() and the Length property on this object to load the AssetBundle data.</param>
    /// <param name="cache">Caching in memory.</param>
    /// <returns>Loaded AssetStudioBundle object or null if failed.</returns>
    public static AssetStudioBundle LoadFromStream(Stream stream, bool cache = true)
    {
        AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromStream] Loading bundle | caching: {cache}", false);

        if(!ValidateLoadFromStream(stream)) return null;
        
        MemoryStream memStream = new MemoryStream();
        stream.CopyTo(memStream);

        AssetStudioBundle bundle = LoadFromMemory(memStream.ToArray(), cache);

        memStream.Close();
        return bundle;
    }

    /// <summary>
    /// Fast load an AssetStudioBundle from a bundleFiles cache.
    /// </summary>
    /// <param name="cache_key">Key name for caching fast access.</param>
    /// <returns>Loaded AssetStudioBundle object or null if failed.</returns>
    public static AssetStudioBundle LoadFromCache(string cache_key)
    {
        AssetStudioLogger.Log($"[AssetStudioLoader:LoadFromCache] Loading bundle | cache_key: {cache_key}", false);

        if (isCached(cache_key)) return bundleFiles[cache_key];

        else return null;
    }

    /// <summary>
    /// Remove an AssetStudioBundle from a bundleFiles cache.
    /// </summary>
    /// <param name="cache_key">Key name for caching fast access.</param>
    public static void Unload(string cache_key)
    {
        bundleFiles.Remove(cache_key);
    }

    /// <summary>
    /// When reading files from memory, we are forced to save them to a temporary folder. This function clears it.
    /// </summary>
    private static void ClearTempCacheFolder()
    {
        try
        {
            DirectoryInfo directory = new DirectoryInfo(TempCachePath);

            if (directory.Exists)
            {
                foreach (FileInfo file in directory.GetFiles())
                {
                    file.Delete();
                }

                foreach (DirectoryInfo subDirectory in directory.GetDirectories())
                {
                    subDirectory.Delete(true);
                }
            }
            else
            {
                directory.Create();
            }
        }
        catch (Exception ex)
        {
            AssetStudioLogger.Error($"[AssetStudioLoader] Error ClearTempCacheFolder in {TempCachePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear a bundleFiles cache.
    /// </summary>
    /// <param name="cache_key">Key name for caching fast access.</param>
    public static void UnloadAllBundles()
    {
        bundleFiles = new Dictionary<string, AssetStudioBundle>();
        ClearTempCacheFolder();
    }

    /// <summary>
    /// Check AssetStudioBundle in bundleFiles cache.
    /// </summary>
    /// <param name="cache_key">Key name for caching fast access.</param>
    /// <returns>True if cached, otherwise false.</returns>
    public static bool isCached(string cache_key)
    {
        return bundleFiles.ContainsKey(cache_key);
    }

    internal static bool ValidateLoadFromStream(Stream stream)
    {
        if (stream == null)
        {
            AssetStudioLogger.Warn("[ValidateLoadFromStream] ManagedStream object must be non-null");
            return false;
        }

        if (!stream.CanRead)
        {
            AssetStudioLogger.Warn("[ValidateLoadFromStream] ManagedStream object must be readable (stream.CanRead must return true)");
            return false;
        }

        if (!stream.CanSeek)
        {
            AssetStudioLogger.Warn("[ValidateLoadFromStream] ManagedStream object must be seekable (stream.CanSeek must return true)");
            return false;
        }

        return true;
    }

    public static IEnumerable<string> GetAllCachedAssetBundlesNames()
    {
        return bundleFiles.Keys;
    }

    public static IEnumerable<AssetStudioBundle> GetAllCachedAssetBundles()
    {
        return bundleFiles.Values;
    }
}