using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: PluginChecks <plugin.dll> <stub-dir> <managed-dir> <translations.json>");
    return 2;
}

var pluginPath = Path.GetFullPath(args[0]);
var searchDirectories = new[]
{
    Path.GetDirectoryName(pluginPath)!,
    Path.GetFullPath(args[1]),
    Path.GetFullPath(args[2])
};
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in searchDirectories)
    {
        var candidate = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(candidate))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }
    }
    return null;
};

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
var type = assembly.GetType("EchoesChinese.TranslationDatabase", true)!;
var load = type.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
var translateUi = type.GetMethod("TranslateUi", BindingFlags.Static | BindingFlags.NonPublic)!;
var decoded = type.GetMethod("TryGetDecodedEcho", BindingFlags.Static | BindingFlags.NonPublic)!;
load.Invoke(null, new object[] { Path.GetFullPath(args[3]) });

Check("exact", "必须逃——", Translate("逃げなければ──"));
Check(
    "formatted",
    "要从上次的进度（Day 3）继续吗？\n请选择起点。",
    Translate("前回の続き（Day 3）から始めますか？\n開始地点を選んでください。")
);
Check(
    "composite percentage",
    "要重置“已辨之音”吗？（已解读 75%）",
    Translate("「分かった音」をリセットしますか？（75%解読済み）")
);
Check(
    "dynamic missing end roll",
    "未找到 Route ID“Silent”对应的片尾。",
    Translate("Route Id 'Silent' のエンドロールが見つかりません。")
);
Check(
    "dynamic end roll without lines",
    "Route ID“Silent”未设置台词。",
    Translate("Route Id 'Silent' にlineが設定されていません。")
);
Check("romaji", "shi", Translate("し"));
Check("echo guard", "{echo:ありがとう}", Translate("{echo:ありがとう}"));

var decodedArguments = new object?[] { "{echo:ありがとう}", null };
var found = (bool)decoded.Invoke(null, decodedArguments)!;
if (!found || (string?)decodedArguments[1] != "谢谢你")
{
    throw new InvalidOperationException("Decoded echo lookup failed.");
}

Console.WriteLine("Plugin translation checks: PASS");
return 0;

string Translate(string source) => (string)translateUi.Invoke(null, new object[] { source })!;

static void Check(string name, string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
    }
}
