using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EchoesChinese
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class EchoesChinesePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.echoes.zh_hans";
        public const string PluginName = "ECHOES Simplified Chinese";
        public const string PluginVersion = "1.0.0";

        private Harmony _harmony;

        private void Awake()
        {
            PatchLog.Logger = Logger;
            var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            TranslationDatabase.Load(Path.Combine(pluginDirectory, "translations.json"));
            ChineseFont.Load(Path.Combine(pluginDirectory, "fonts", "fusion-pixel-12px-zh_hans.ttf"));

            _harmony = new Harmony(PluginGuid);
            PatchMethods(_harmony);
            UiTextPatch.ApplyToLoadedTexts();
            Logger.LogInfo($"Loaded {TranslationDatabase.Count} Simplified Chinese translations.");
        }

        private static void PatchMethods(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.PropertySetter(typeof(Text), nameof(Text.text)),
                prefix: new HarmonyMethod(typeof(UiTextPatch), nameof(UiTextPatch.SetTextPrefix))
            );
            harmony.Patch(
                AccessTools.PropertySetter(typeof(Text), nameof(Text.font)),
                prefix: new HarmonyMethod(typeof(UiTextPatch), nameof(UiTextPatch.SetFontPrefix))
            );

            var onEnable = AccessTools.Method(typeof(Text), "OnEnable");
            if (onEnable != null)
            {
                harmony.Patch(
                    onEnable,
                    postfix: new HarmonyMethod(typeof(UiTextPatch), nameof(UiTextPatch.OnEnablePostfix))
                );
            }

            var controllerType = AccessTools.TypeByName("Echoes.Runtime.EchoesGameUIController");
            var setDialogueText = AccessTools.Method(controllerType, "SetDialogueText");
            if (setDialogueText == null)
            {
                PatchLog.Logger.LogError("Could not find EchoesGameUIController.SetDialogueText.");
                return;
            }

            harmony.Patch(
                setDialogueText,
                prefix: new HarmonyMethod(typeof(DialoguePatch), nameof(DialoguePatch.Prefix))
            );

            var resolveChoiceText = AccessTools.Method(controllerType, "ResolveLineChoiceGlyphDisplayText");
            if (resolveChoiceText != null)
            {
                harmony.Patch(
                    resolveChoiceText,
                    postfix: new HarmonyMethod(typeof(DialoguePatch), nameof(DialoguePatch.ChoicePostfix))
                );
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            ChineseFont.Unload();
        }
    }

    internal static class PatchLog
    {
        internal static ManualLogSource Logger;
    }

    internal static class DialoguePatch
    {
        internal static void Prefix(object __instance, ref string __0, ref string __1)
        {
            __0 = TranslationDatabase.Translate(__0);
            __1 = TranslationDatabase.TranslateDialogue(__instance, __1);
        }

        internal static void ChoicePostfix(object __instance, string __0, ref string __result)
        {
            if (!EchoKnowledge.AreAllKnown(__instance, __0) ||
                !TranslationDatabase.TryGetDecodedEcho(__0, out var translation))
            {
                return;
            }

            __result += "\n" + translation;
        }
    }

    internal static class UiTextPatch
    {
        internal static void ApplyToLoadedTexts()
        {
            if (ChineseFont.Font == null)
            {
                return;
            }

            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null)
                {
                    continue;
                }

                var translated = TranslationDatabase.TranslateUi(text.text);
                if (!string.Equals(text.text, translated, StringComparison.Ordinal))
                {
                    text.text = translated;
                }
                if (text.font != ChineseFont.Font)
                {
                    text.font = ChineseFont.Font;
                }
            }
        }

        internal static void SetTextPrefix(ref string __0)
        {
            __0 = TranslationDatabase.TranslateUi(__0);
        }

        internal static void SetFontPrefix(ref Font __0)
        {
            if (ChineseFont.Font != null && __0 != ChineseFont.Font)
            {
                __0 = ChineseFont.Font;
            }
        }

        internal static void OnEnablePostfix(Text __instance)
        {
            var translated = TranslationDatabase.TranslateUi(__instance.text);
            if (!string.Equals(__instance.text, translated, StringComparison.Ordinal))
            {
                __instance.text = translated;
            }
            if (ChineseFont.Font != null && __instance.font != ChineseFont.Font)
            {
                __instance.font = ChineseFont.Font;
            }
        }
    }

    internal sealed class TranslationEntry
    {
        internal string Original;
        internal string Translation;
        internal Regex TemplateRegex;
    }

    internal static class TranslationDatabase
    {
        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> DecodedEcho = new Dictionary<string, string>();
        private static readonly List<TranslationEntry> Templates = new List<TranslationEntry>();
        private static readonly Dictionary<string, string> Romaji = BuildRomaji();

        internal static int Count => Exact.Count + Templates.Count;

        internal static void Load(string path)
        {
            Exact.Clear();
            DecodedEcho.Clear();
            Templates.Clear();

            if (!File.Exists(path))
            {
                PatchLog.Logger.LogError($"Translation file not found: {path}");
                return;
            }

            var root = JObject.Parse(File.ReadAllText(path));
            foreach (var token in root["entries"] ?? new JArray())
            {
                var original = Normalize((string)token["original"]);
                var translation = Normalize((string)token["translation"]);
                if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(translation) || original == translation)
                {
                    continue;
                }

                if (string.Equals((string)token["mode"], "decodedEcho", StringComparison.Ordinal))
                {
                    DecodedEcho[original] = translation;
                    continue;
                }

                if (original.IndexOf('{') >= 0 && TryCreateTemplate(original, translation, out var template))
                {
                    Templates.Add(template);
                }
                else
                {
                    Exact[original] = translation;
                }
            }
        }

        internal static string Translate(string value)
        {
            return TranslateCore(value, false);
        }

        internal static string TranslateUi(string value)
        {
            if (value != null && value.IndexOf("{echo:", StringComparison.Ordinal) >= 0)
            {
                return value;
            }
            if (value != null && Romaji.TryGetValue(value, out var romanized))
            {
                return romanized;
            }
            return TranslateCore(value, true);
        }

        internal static string TranslateDialogue(object controller, string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.IndexOf("{echo:", StringComparison.Ordinal) < 0)
            {
                return Translate(value);
            }

            if (EchoKnowledge.AreAllKnown(controller, value) && TryGetDecodedEcho(value, out var translation))
            {
                return value + "\n" + translation;
            }
            return value;
        }

        internal static bool TryGetDecodedEcho(string value, out string translation)
        {
            return DecodedEcho.TryGetValue(Normalize(value), out translation);
        }

        private static string TranslateCore(string value, bool allowTemplates)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var normalized = Normalize(value);
            if (Exact.TryGetValue(normalized, out var translated))
            {
                return RestoreNewlines(value, translated);
            }

            if (!allowTemplates)
            {
                return value;
            }

            foreach (var template in Templates)
            {
                var match = template.TemplateRegex.Match(normalized);
                if (!match.Success)
                {
                    continue;
                }

                var result = template.Translation;
                for (var index = 1; index < match.Groups.Count; index++)
                {
                    result = result.Replace("{" + (index - 1) + "}", match.Groups[index].Value);
                }
                return RestoreNewlines(value, result);
            }

            return value;
        }

        private static bool TryCreateTemplate(string original, string translation, out TranslationEntry entry)
        {
            var placeholders = Regex.Matches(original, @"\{\d+(?:[^}]*)\}");
            entry = null;
            if (placeholders.Count == 0)
            {
                return false;
            }

            var pattern = new StringBuilder();
            var offset = 0;
            foreach (Match placeholder in placeholders)
            {
                pattern.Append(Regex.Escape(original.Substring(offset, placeholder.Index - offset)));
                pattern.Append("(.+?)");
                offset = placeholder.Index + placeholder.Length;
            }
            pattern.Append(Regex.Escape(original.Substring(offset)));

            entry = new TranslationEntry
            {
                Original = original,
                Translation = translation,
                TemplateRegex = new Regex("^" + pattern + "$", RegexOptions.CultureInvariant)
            };
            return true;
        }

        private static string Normalize(string value)
        {
            return value?.Replace("\r\n", "\n");
        }

        private static string RestoreNewlines(string source, string translated)
        {
            return source.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? translated.Replace("\n", "\r\n")
                : translated;
        }

        private static Dictionary<string, string> BuildRomaji()
        {
            return new Dictionary<string, string>
            {
                ["あ"] = "a", ["い"] = "i", ["う"] = "u", ["え"] = "e", ["お"] = "o",
                ["か"] = "ka", ["き"] = "ki", ["く"] = "ku", ["け"] = "ke", ["こ"] = "ko",
                ["さ"] = "sa", ["し"] = "shi", ["す"] = "su", ["せ"] = "se", ["そ"] = "so",
                ["た"] = "ta", ["ち"] = "chi", ["つ"] = "tsu", ["て"] = "te", ["と"] = "to",
                ["な"] = "na", ["に"] = "ni", ["ぬ"] = "nu", ["ね"] = "ne", ["の"] = "no",
                ["は"] = "ha", ["ひ"] = "hi", ["ふ"] = "fu", ["へ"] = "he", ["ほ"] = "ho",
                ["ま"] = "ma", ["み"] = "mi", ["む"] = "mu", ["め"] = "me", ["も"] = "mo",
                ["や"] = "ya", ["ゆ"] = "yu", ["よ"] = "yo",
                ["ら"] = "ra", ["り"] = "ri", ["る"] = "ru", ["れ"] = "re", ["ろ"] = "ro",
                ["わ"] = "wa", ["を"] = "o", ["ん"] = "n",
                ["が"] = "ga", ["ぎ"] = "gi", ["ぐ"] = "gu", ["げ"] = "ge", ["ご"] = "go",
                ["ざ"] = "za", ["じ"] = "ji", ["ず"] = "zu", ["ぜ"] = "ze", ["ぞ"] = "zo",
                ["だ"] = "da", ["ぢ"] = "ji", ["づ"] = "zu", ["で"] = "de", ["ど"] = "do",
                ["ば"] = "ba", ["び"] = "bi", ["ぶ"] = "bu", ["べ"] = "be", ["ぼ"] = "bo",
                ["ぱ"] = "pa", ["ぴ"] = "pi", ["ぷ"] = "pu", ["ぺ"] = "pe", ["ぽ"] = "po",
                ["ー"] = "-"
            };
        }
    }

    internal static class EchoKnowledge
    {
        private static readonly Regex EchoBody = new Regex(@"\{echo:([^}]*)\}", RegexOptions.CultureInvariant);
        private static MethodInfo _buildKnownSet;
        private static MethodInfo _buildTokens;

        internal static bool AreAllKnown(object controller, string source)
        {
            if (controller == null || string.IsNullOrEmpty(source))
            {
                return false;
            }

            var match = EchoBody.Match(source);
            var body = match.Success ? match.Groups[1].Value : source;

            try
            {
                _buildKnownSet = _buildKnownSet ?? AccessTools.Method(controller.GetType(), "BuildKnownSoundTokenSet");
                var tokenizerType = AccessTools.TypeByName("Echoes.Runtime.EchoesOperationGuessKanaTokenizer");
                _buildTokens = _buildTokens ?? AccessTools.Method(tokenizerType, "BuildTokens");
                if (_buildKnownSet == null || _buildTokens == null)
                {
                    return false;
                }

                var known = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in (IEnumerable)_buildKnownSet.Invoke(controller, null))
                {
                    known.Add((string)token);
                }

                var required = (IEnumerable)_buildTokens.Invoke(null, new object[] { body });
                var foundAny = false;
                foreach (var token in required)
                {
                    foundAny = true;
                    if (!known.Contains((string)token))
                    {
                        return false;
                    }
                }
                return foundAny;
            }
            catch (Exception exception)
            {
                PatchLog.Logger.LogWarning($"Could not resolve decoded echo state: {exception.Message}");
                return false;
            }
        }
    }

    internal static class ChineseFont
    {
        private const uint FrPrivate = 0x10;
        private const string BundledFaceName = "FusionPixel12ZhHans";
        private static string _privateFontPath;

        internal static Font Font { get; private set; }

        internal static void Load(string path)
        {
            var candidates = new List<string>();
            if (File.Exists(path) && AddFontResourceEx(path, FrPrivate, IntPtr.Zero) > 0)
            {
                _privateFontPath = path;
                candidates.Add(BundledFaceName);
            }

            candidates.Add("Microsoft YaHei UI");
            candidates.Add("Microsoft YaHei");
            candidates.Add("SimHei");

            foreach (var face in candidates)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(face, 16);
                    if (font == null)
                    {
                        continue;
                    }

                    font.name = "ECHOES Chinese - " + face;
                    Font = font;
                    Font.textureRebuilt += HandleTextureRebuilt;
                    ApplyPointFiltering(font);
                    PatchLog.Logger.LogInfo($"Using Chinese font: {face}");
                    return;
                }
                catch (Exception exception)
                {
                    PatchLog.Logger.LogWarning($"Could not load font '{face}': {exception.Message}");
                }
            }

            PatchLog.Logger.LogError("No usable Simplified Chinese font was found.");
        }

        internal static void Unload()
        {
            if (Font != null)
            {
                Font.textureRebuilt -= HandleTextureRebuilt;
            }
            if (_privateFontPath != null)
            {
                RemoveFontResourceEx(_privateFontPath, FrPrivate, IntPtr.Zero);
            }
        }

        private static void HandleTextureRebuilt(Font font)
        {
            ApplyPointFiltering(font);
        }

        private static void ApplyPointFiltering(Font font)
        {
            var texture = font?.material?.mainTexture;
            if (texture != null)
            {
                texture.filterMode = FilterMode.Point;
            }
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);
    }
}
