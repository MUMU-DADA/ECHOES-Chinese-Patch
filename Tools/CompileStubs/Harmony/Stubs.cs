using System;
using System.Reflection;

namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public HarmonyMethod(Type type, string name) { }
    }

    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null) { }
        public void UnpatchSelf() { }
    }

    public static class AccessTools
    {
        public static MethodInfo PropertySetter(Type type, string name) => null;
        public static MethodInfo Method(Type type, string name) => null;
        public static Type TypeByName(string name) => null;
    }
}
