param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$RequirePlugin
)

$ErrorActionPreference = 'Stop'
$translationPath = Join-Path $GameRoot 'Patch\BepInEx\plugins\EchoesChinese\translations.json'
$fontPath = Join-Path $GameRoot 'Patch\BepInEx\plugins\EchoesChinese\fonts\fusion-pixel-12px-zh_hans.ttf'
$pluginPath = Join-Path $GameRoot 'Patch\BepInEx\plugins\EchoesChinese\EchoesChinese.dll'
$catalog = Get-Content (Join-Path $GameRoot 'Translation\source-catalog.json') -Raw | ConvertFrom-Json
$translations = Get-Content $translationPath -Raw | ConvertFrom-Json

$catalogById = @{}
$catalog.entries | ForEach-Object { $catalogById[$_.id] = $_ }
$syntheticIds = @('S0001', 'S0002', 'S0003')
$translatedById = @{}
foreach ($entry in $translations.entries) {
    if ($entry.id -notin $syntheticIds -and -not $catalogById.ContainsKey($entry.id)) {
        throw "Unknown translation ID: $($entry.id)"
    }
    if ($translatedById.ContainsKey($entry.id)) {
        throw "Duplicate translation ID: $($entry.id)"
    }
    if ([string]::IsNullOrWhiteSpace($entry.translation)) {
        throw "Empty translation: $($entry.id)"
    }
    $translatedById[$entry.id] = $entry

    $sourcePlaceholders = @([regex]::Matches($entry.original, '\{\d+(?:[^}]*)\}') | ForEach-Object Value)
    $targetPlaceholders = @([regex]::Matches($entry.translation, '\{\d+(?:[^}]*)\}') | ForEach-Object Value)
    if (($sourcePlaceholders -join '|') -ne ($targetPlaceholders -join '|')) {
        throw "Placeholder mismatch in $($entry.id): '$($entry.original)' -> '$($entry.translation)'"
    }
}

foreach ($number in 138..414) {
    $id = 'E{0:D4}' -f $number
    if (-not $translatedById.ContainsKey($id)) {
        throw "Missing story translation: $id"
    }
}

$requiredPreStory = @(9, 40, 52, 53) + @(127..137) | ForEach-Object { 'E{0:D4}' -f $_ }
foreach ($id in $requiredPreStory) {
    if (-not $translatedById.ContainsKey($id)) {
        throw "Missing visible pre-story translation: $id"
    }
}

$protectedGameplayData = @(
    416, 418, 420, 422, 424, 425, 427, 429, 431, 433, 435, 437, 439,
    441, 443, 445, 447, 449, 451, 453, 455, 457, 459, 461, 463, 465, 527
) | ForEach-Object { 'E{0:D4}' -f $_ }
foreach ($number in 415..551) {
    $id = 'E{0:D4}' -f $number
    if (-not $translatedById.ContainsKey($id) -and $id -notin $protectedGameplayData) {
        throw "Missing UI translation: $id"
    }
}

$protectedGameplayData += @(54..126) | ForEach-Object { 'E{0:D4}' -f $_ }
$protectedGameplayData += @(552..566) | ForEach-Object { 'E{0:D4}' -f $_ }
foreach ($id in $protectedGameplayData) {
    if ($translatedById.ContainsKey($id)) {
        throw "Protected kana answer or internal route value must remain untranslated: $id"
    }
}

foreach ($id in $syntheticIds) {
    if (-not $translatedById.ContainsKey($id)) {
        throw "Missing synthetic dynamic translation: $id"
    }
}

$expectedDecodedEchoIds = @(
    'E0143', 'E0145', 'E0170', 'E0222', 'E0227', 'E0234', 'E0266', 'E0267',
    'E0297', 'E0298', 'E0302', 'E0323', 'E0324', 'E0392', 'E0395'
)
$decodedEchoIds = @($translations.entries | Where-Object mode -eq 'decodedEcho' | ForEach-Object id)
$actualDecodedEchoSet = ($decodedEchoIds | Sort-Object) -join '|'
$expectedDecodedEchoSet = ($expectedDecodedEchoIds | Sort-Object) -join '|'
if ($actualDecodedEchoSet -ne $expectedDecodedEchoSet) {
    throw "Decoded echo translation set mismatch: $($decodedEchoIds -join ', ')"
}

if (-not (Test-Path $fontPath)) {
    throw "Bundled font is missing: $fontPath"
}
Add-Type -AssemblyName System.Drawing.Common
$fontCollection = [Drawing.Text.PrivateFontCollection]::new()
try {
    $fontCollection.AddFontFile($fontPath)
    if ($fontCollection.Families.Name -notcontains 'FusionPixel12ZhHans') {
        throw 'The generated TTF does not expose the expected family name.'
    }
}
finally {
    $fontCollection.Dispose()
}

if ($RequirePlugin -and -not (Test-Path $pluginPath)) {
    throw "Final plugin has not been built: $pluginPath"
}
if ($RequirePlugin) {
    foreach ($releaseFile in @(
        'winhttp.dll',
        'doorstop_config.ini',
        'BepInEx\core\BepInEx.dll',
        'BepInEx\core\0Harmony.dll'
    )) {
        $path = Join-Path (Join-Path $GameRoot 'Patch') $releaseFile
        if (-not (Test-Path $path)) {
            throw "Release loader file is missing: $path"
        }
    }

    $patchRoot = Join-Path $GameRoot 'Patch'
    $forbiddenReleaseFiles = @(Get-ChildItem $patchRoot -Recurse -Force -File | Where-Object {
        $relativePath = [IO.Path]::GetRelativePath($patchRoot, $_.FullName).Replace('\', '/')
        $relativePath -match '(?i)(^|/)(ExtractedMedia|CompileStubs)(/|$)' -or
        $_.Extension -match '(?i)^\.(mp4|webm|mov|avi|mkv)$' -or
        $_.Name -match '(?i)\.verify\.dll$'
    })
    if ($forbiddenReleaseFiles.Count -gt 0) {
        throw "Release contains forbidden media or development files: $($forbiddenReleaseFiles.FullName -join ', ')"
    }

    $loaderPath = Join-Path $patchRoot 'winhttp.dll'
    $loaderBytes = [IO.File]::ReadAllBytes($loaderPath)
    $peOffset = [BitConverter]::ToInt32($loaderBytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($loaderBytes, $peOffset + 4)
    if ($machine -ne 0x8664) {
        throw ('Expected an x64 winhttp.dll loader, found PE machine 0x{0:X4}.' -f $machine)
    }

    $bepInExVersion = [Reflection.AssemblyName]::GetAssemblyName(
        (Join-Path $patchRoot 'BepInEx\core\BepInEx.dll')
    ).Version
    if ($bepInExVersion -ne [version]'5.4.23.5') {
        throw "Unexpected BepInEx version: $bepInExVersion"
    }
}

Write-Host "Translation coverage: PASS ($($translations.entries.Count) entries)"
Write-Host 'Placeholder preservation: PASS'
Write-Host 'Protected gameplay values and decoded-echo set: PASS'
Write-Host 'Bundled font load: PASS'
if (Test-Path $pluginPath) {
    Write-Host 'Final plugin presence: PASS'
}
if ($RequirePlugin) {
    Write-Host 'Release scope (no video/dev files): PASS'
    Write-Host 'BepInEx loader architecture/version: PASS (x64, 5.4.23.5)'
}
