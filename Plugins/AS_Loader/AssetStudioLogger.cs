using UnityEngine;

public class AssetStudioLogger
{
    public static bool logs = true;
    public static bool debug = false;

    public static void Log(string message, bool detailed)
    {
        if(logs && (!detailed || detailed & debug)) Debug.Log(message);
    }

    public static void Warn(string message)
    {
        if (logs) Debug.LogWarning(message);
    }

    public static void Error(string message)
    {
        if (logs) Debug.LogError(message);
    }
}
