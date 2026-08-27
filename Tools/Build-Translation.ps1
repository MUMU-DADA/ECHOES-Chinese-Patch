param(
    [string]$CatalogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Translation\source-catalog.json'),
    [string]$MapPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Translation\zh-Hans.psd1'),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Patch\BepInEx\plugins\EchoesChinese\translations.json')
)

$ErrorActionPreference = 'Stop'
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
$map = Import-PowerShellDataFile -LiteralPath $MapPath
$entriesById = @{}
foreach ($entry in $catalog.entries) {
    $entriesById[$entry.id] = $entry
}

$unknownIds = @($map.Keys | Where-Object { -not $entriesById.ContainsKey($_) })
if ($unknownIds.Count -gt 0) {
    throw "Translation map contains unknown IDs: $($unknownIds -join ', ')"
}

$entries = foreach ($entry in $catalog.entries) {
    if (-not $map.ContainsKey($entry.id)) {
        continue
    }

    $translation = [string]$map[$entry.id]
    $translation = $translation.Replace('\n', "`n")
    [ordered]@{
        id = $entry.id
        original = $entry.original
        translation = $translation
    }
}

$entries += [ordered]@{
    id = 'S0001'
    original = '「分かった音」をリセットしますか？（{0}%解読済み）'
    translation = '要重置“已辨之音”吗？（已解读 {0}%）'
}
$entries += [ordered]@{
    id = 'S0002'
    original = "Route Id '{0}' のエンドロールが見つかりません。"
    translation = '未找到 Route ID“{0}”对应的片尾。'
}
$entries += [ordered]@{
    id = 'S0003'
    original = "Route Id '{0}' にlineが設定されていません。"
    translation = 'Route ID“{0}”未设置台词。'
}

$decodedEcho = [ordered]@{
    E0143 = '已经没事了'
    E0145 = '没事了，别怕'
    E0170 = '谢谢你'
    E0222 = '救救我——'
    E0227 = '救救我'
    E0234 = '找到口粮了'
    E0266 = '请原谅我'
    E0267 = '对不起'
    E0297 = '发生什么了'
    E0298 = '你没事吧'
    E0302 = '它们要来了'
    E0323 = '一起逃吧'
    E0324 = '一起战斗吧'
    E0392 = '不要'
    E0395 = '求你了'
}
foreach ($pair in $decodedEcho.GetEnumerator()) {
    $source = $entriesById[$pair.Key]
    $entries += [ordered]@{
        id = $pair.Key
        original = $source.original
        translation = $pair.Value
        mode = 'decodedEcho'
    }
}

$output = [ordered]@{
    schemaVersion = 1
    language = 'zh-Hans'
    gameBuildGuid = '3582fd50e292455aa54dd78f69aa0d2b'
    entries = @($entries)
}

$directory = Split-Path -Parent $OutputPath
[IO.Directory]::CreateDirectory($directory) | Out-Null
$output | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Wrote $(@($entries).Count) translations to $OutputPath"
