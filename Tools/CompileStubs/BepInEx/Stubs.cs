using System;
using BepInEx.Logging;
using UnityEngine;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version) { }
    }

    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        protected ManualLogSource Logger { get; } = new ManualLogSource();
    }

    public static class Paths
    {
        public static string PluginPath => string.Empty;
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public void LogInfo(object value) { }
        public void LogWarning(object value) { }
        public void LogError(object value) { }
    }
}
