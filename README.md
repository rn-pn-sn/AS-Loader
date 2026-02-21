# AssetStudio-Loader (AS-Loader)

[![Download latest release](https://img.shields.io/badge/Download_latest_release-blue)](https://github.com/rn-pn-sn/AS-Loader/releases/latest)

**AssetStudio-Loader** - this plugin is designed to force reading of asset bundles in Unity runtime.

The plugin is extremely useful if you need to read data directly from legacy/unsupported platform asset bundles, when default AssetBundle lib is powerless.

It is already being used in a large WebGL-based project (CSR2-HUB), which required processing over 2,000 assets previously developed for the Android platform.

**Neither the repository, nor the tool, nor the author of the tool, nor the author of the modification is affiliated with, sponsored, or authorized by Unity Technologies or its affiliates.**

## AS-Loader Features

- Support bundles Unity version:
  - 1.7 - 6000.2
- Support asset types:
  - **Texture2D**
  - **Sprite**
  - **TextAsset**
  - **MonoBehaviour**
- Not-tested support types:
  - **Texture2DArray**
  - **AudioClip**
  - **Font**
  - **Mesh**
  - **Shader**
  - **MovieTexture**
  - **VideoClip**
  - **Animator**

## Requirements

- Unity Engine
- Your brains

### Import to Unity project

- [Download latest release](https://github.com/rn-pn-sn/AS-Loader/releases/latest)
- Unpack archive content to root Assets project folder

### Learn: project structure

Scripts are not MonoBehavior and do not require placement on the scene.
- AssetStudioBundle.cs: class created with maximum identity as an AssetBundle.
<br>NOTE: But for greater stability, loading occurs through a separate class, so for load AssetStudioBundle use AssetStudioLoader!
- AssetStudioLoader.cs: manager class that manages all AssetStudioBundle objects.
- AssetStudioExporter.cs: proxy class that accesses the exporter code.
- AssetStudioLogger.cs: just log class (you can manage logging level via public bools).
- All other files and folders are AssetStudioMod files, adapted for the plugin's work.

### Learn: basic usages

- Loading and unloading AssetStudioBundle
```cs
    AssetBundle bundle = AssetBundle.LoadFromFile("path/to/assetbundle");
    if (bundle == null) // example for checking success default loading and using AS-Loader fallback
    {
        // loading from file
        AssetStudioBundle abs = AssetStudioLoader.LoadFromFile("path/to/assetbundle");
        // loading from file without caching in memory
        AssetStudioBundle abs = AssetStudioLoader.LoadFromFile("path/to/assetbundle", false);
        
        // other option for loading from byte memory
        byte[] data;
        AssetStudioBundle abs = AssetStudioLoader.LoadFromMemory(data);
        AssetStudioBundle abs = AssetStudioLoader.LoadFromMemory(data, false);

        // other option for loading from stream
        Stream dataStream;
        AssetStudioBundle abs = AssetStudioLoader.LoadFromStream(dataStream);
        AssetStudioBundle abs = AssetStudioLoader.LoadFromStream(dataStream, false);
        
        // direct function for loading from cache
        AssetStudioBundle abs = AssetStudioLoader.LoadFromCache("cache_key");
        // for getting cache_key
        abs.GetCacheKey();
        // unload bundle from cache via AssetStudioLoader
        AssetStudioLoader.Unload("cache_key");

        // or directly via AssetStudioBundle
        abs.Unload();
        
        // unload all bundles from cache
        AssetStudioLoader.UnloadAllBundles();
    }
```

- Example getting Texture2D and Sprite
```cpp
    Sprite sprite = null;
    AssetStudioBundle abs = AssetStudioLoader.LoadFromMemory(data);
    if (abs != null) { 
      sprite = GTfABS(abs);
      abs.Unload();
    }

    private static Sprite GTfABS(AssetStudioBundle bundle)
    {
        // UNSAFE: expected only 1 object of the class, use only for simple and small bundles, else return first element
        // WARN: some compress formats may not be supported
        Texture2D texture2D = bundle.LoadAsset<Texture2D>();
        if(texture2D != null)
        {
            return Sprite.Create(texture2D, new Rect(0.0f, 0.0f, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f), 100.0f);
        }

        return null;
    }
    
    ///AssetStudioBundle.cs
    public UnityEngine.Object LoadAsset(string name, Type type) {...}
    public UnityEngine.Object LoadAsset(string name, Type type, Type component) {...}
    // If you have multiple objects, you should use these functions. However, they are not ready at the moment, as I have not had a need to make them.
```

- Example getting TextAsset
```cpp
    AssetStudioBundle abs = AssetStudioLoader.LoadFromFile("path/to/bundle"), false);
    // UNSAFE: expected only 1 object of the class, use only for simple and small bundles, else return first element
    TextAsset vv = abs.LoadAsset<TextAsset>();
    
    ///AssetStudioBundle.cs
    public UnityEngine.Object LoadAsset(string name, Type type) {...}
    public UnityEngine.Object LoadAsset(string name, Type type, Type component) {...}
    // If you have multiple objects, you should use these functions. However, they are not ready at the moment, as I have not had a need to make them.
```

- Example getting MonoBehaviour
```cpp
    AssetStudioBundle abs = AssetStudioLoader.LoadFromFile($"{HUB_gitboxV2.cachedManifest.branch}/CVD/{Data.aCRDB}", false);
    if (abs != null)
    {
      ExampleMono exData = null;
      // UNSAFE: expected only 1 object of the class, use only for simple and small bundles, else return first element
      exData = abs.LoadAsset<ExampleMono>();
    }

public class exData : MonoBehaviour
{
    public string data;
}
    
    ///AssetStudioBundle.cs
    public UnityEngine.Object LoadAsset(string name, Type type) {...}
    public UnityEngine.Object LoadAsset(string name, Type type, Type component) {...}
    // If you have multiple objects, you should use these functions. However, they are not ready at the moment, as I have not had a need to make them.
```

- Example getting Texture2DArray
```
    Missing info, need tests and implementation
```

- Example getting AudioClip
```
    Missing info, need tests and implementation
```

- Example getting Font
```
    Missing info, need tests and implementation
```

- Example getting Mesh
```
    Missing info, need tests and implementation
```

- Example getting Shader
```
    Missing info, need tests and implementation
```

- Example getting MovieTexture
```
    Missing info, need tests and implementation
```

- Example getting Animator
```
    Missing info, need tests and implementation
```

- Example getting Texture2DArray
```
    Missing info, need tests and implementation
```

I will update the project only if necessary, any assistance from your side is welcome!
