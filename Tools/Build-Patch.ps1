param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BepInExPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Tools\BepInEx')
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = (Join-Path $GameRoot 'Tools\.dotnet-home')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$pluginDirectory = Join-Path $GameRoot 'Patch\BepInEx\plugins\EchoesChinese'

& (Join-Path $GameRoot 'Tools\Build-Translation.ps1')
dotnet run --project (Join-Path $GameRoot 'Tools\FontBuilder\FontBuilder.csproj') -c Release -- `
    (Join-Path $GameRoot 'Tools\FusionPixel12\fusion-pixel-12px-proportional-zh_hans.bdf') `
    (Join-Path $pluginDirectory 'translations.json') `
    (Join-Path $pluginDirectory 'fonts\fusion-pixel-12px-zh_hans.ttf')
if ($LASTEXITCODE -ne 0) {
    throw 'Font build failed.'
}

$corePath = Join-Path $BepInExPath 'BepInEx\core'
if (-not (Test-Path (Join-Path $corePath 'BepInEx.dll')) -or
    -not (Test-Path (Join-Path $corePath '0Harmony.dll'))) {
    throw "Real BepInEx 5 files are missing under $BepInExPath. Compile stubs are never accepted for release builds."
}
$bepInExAssembly = Get-Item (Join-Path $corePath 'BepInEx.dll')
$harmonyAssembly = Get-Item (Join-Path $corePath '0Harmony.dll')
if ($bepInExAssembly.Length -lt 50000 -or $harmonyAssembly.Length -lt 50000) {
    throw 'BepInEx or Harmony is implausibly small. Refusing to build a release from compile stubs.'
}

& (Join-Path $GameRoot 'Tools\Compile-Plugin.ps1') `
    -BepInExCorePath $corePath `
    -OutputPath (Join-Path $pluginDirectory 'EchoesChinese.dll')
Copy-Item (Join-Path $GameRoot 'Tools\FusionPixel12\OFL.txt') (Join-Path $pluginDirectory 'fonts\OFL.txt') -Force
$fontLicenseDestination = Join-Path $pluginDirectory 'fonts\LICENSES'
if (Test-Path $fontLicenseDestination) {
    Remove-Item -LiteralPath $fontLicenseDestination -Recurse -Force
}
Copy-Item `
    (Join-Path $GameRoot 'Tools\FusionPixel12\LICENSES') `
    $fontLicenseDestination `
    -Recurse -Force

# Overlay only the BepInEx loader/runtime. Existing plugin content is retained.
$releaseCorePath = Join-Path $GameRoot 'Patch\BepInEx\core'
if (Test-Path $releaseCorePath) {
    Remove-Item -LiteralPath $releaseCorePath -Recurse -Force
}
Copy-Item (Join-Path $BepInExPath '*') (Join-Path $GameRoot 'Patch') -Recurse -Force
& (Join-Path $GameRoot 'Tools\Validate-Patch.ps1') -RequirePlugin

$manifestPath = Join-Path $GameRoot 'Patch\release-manifest.json'
$manifestEntries = Get-ChildItem (Join-Path $GameRoot 'Patch') -Recurse -Force -File |
    Where-Object FullName -ne $manifestPath |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath((Join-Path $GameRoot 'Patch'), $_.FullName).Replace('\', '/')
            size = $_.Length
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        }
    }
[ordered]@{
    patchVersion = '1.0.0'
    gameBuildGuid = '3582fd50e292455aa54dd78f69aa0d2b'
    loader = [ordered]@{
        name = 'BepInEx'
        version = [Reflection.AssemblyName]::GetAssemblyName(
            (Join-Path $GameRoot 'Patch\BepInEx\core\BepInEx.dll')
        ).Version.ToString()
        architecture = 'x64'
    }
    localizedVideo = $false
    generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    files = @($manifestEntries)
} | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding utf8NoBOM

$releaseDirectory = Join-Path $GameRoot '发布成品'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
$archivePath = Join-Path $releaseDirectory 'ECHOES_简体中文补丁_v1.0.0.zip'
$releaseFiles = Get-ChildItem (Join-Path $GameRoot 'Patch') -Force | ForEach-Object FullName
Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archivePath -Force
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$checksumPath = Join-Path $releaseDirectory 'ECHOES_简体中文补丁_v1.0.0.sha256.txt'
"$archiveHash  ECHOES_简体中文补丁_v1.0.0.zip" |
    Set-Content -LiteralPath $checksumPath -Encoding utf8NoBOM
Write-Host "Built release archive: $archivePath"
Write-Host "Wrote checksum: $checksumPath"
