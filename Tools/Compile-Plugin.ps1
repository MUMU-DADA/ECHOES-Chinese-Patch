param(
    [Parameter(Mandatory = $true)]
    [string]$BepInExCorePath,
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Patch\BepInEx\plugins\EchoesChinese\EchoesChinese.dll')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$managedPath = Join-Path $GameRoot 'ECHOES_Data\Managed'
if (-not (Test-Path $managedPath)) {
    $parentGameRoot = Split-Path -Parent $GameRoot
    if (Test-Path (Join-Path $parentGameRoot 'ECHOES_Data\Managed')) {
        $GameRoot = $parentGameRoot
        $managedPath = Join-Path $GameRoot 'ECHOES_Data\Managed'
    }
}
$managed = Join-Path $GameRoot 'ECHOES_Data\Managed'
$compiler = Get-ChildItem (Join-Path $env:ProgramFiles 'dotnet\sdk\*\Roslyn\bincore\csc.dll') |
    Sort-Object { [version]$_.Directory.Parent.Parent.Name } -Descending |
    Select-Object -First 1
if (-not $compiler) {
    throw 'Could not locate the Roslyn C# compiler from the installed .NET SDK.'
}

$references = @(
    (Join-Path $managed 'mscorlib.dll'),
    (Join-Path $managed 'System.dll'),
    (Join-Path $managed 'System.Core.dll'),
    (Join-Path $managed 'netstandard.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $managed 'UnityEngine.UI.dll'),
    (Join-Path $managed 'Newtonsoft.Json.dll'),
    (Join-Path $BepInExCorePath 'BepInEx.dll'),
    (Join-Path $BepInExCorePath '0Harmony.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path $reference)) {
        throw "Compiler reference is missing: $reference"
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$arguments = @(
    $compiler.FullName,
    '/noconfig',
    '/nostdlib+',
    '/target:library',
    '/optimize+',
    '/deterministic+',
    '/langversion:latest',
    '/nullable:disable',
    "/out:$OutputPath"
)
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += Join-Path $projectRoot 'PatchSource\EchoesChinese\EchoesChinesePlugin.cs'

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Plugin compilation failed with exit code $LASTEXITCODE."
}
Write-Host "Compiled plugin: $OutputPath"
