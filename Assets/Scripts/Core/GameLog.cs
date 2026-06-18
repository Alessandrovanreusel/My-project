using UnityEngine;
using Object = UnityEngine.Object;

namespace CameraGame.Core
{
    /// <summary>Thin categorized wrapper over Debug.Log*. Keeps the console clean and greppable.</summary>
    public static class GameLog
    {
        public static void Info(string cat, string msg)  => Debug.Log($"[{cat}] {msg}");
        public static void Warn(string cat, string msg)  => Debug.LogWarning($"[{cat}] {msg}");
        public static void Error(string cat, string msg, Object ctx = null) => Debug.LogError($"[{cat}] {msg}", ctx);

        // Stripped from release builds — only compiles in Editor / development builds.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Debug_(string cat, string msg) => Debug.Log($"[{cat}:DBG] {msg}");
    }
}
