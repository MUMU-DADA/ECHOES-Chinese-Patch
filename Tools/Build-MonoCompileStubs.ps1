param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Tools\CompileStubs\mono')
)

$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameRoot 'ECHOES_Data\Managed'
$compiler = Get-ChildItem (Join-Path $env:ProgramFiles 'dotnet\sdk\*\Roslyn\bincore\csc.dll') |
    Sort-Object { [version]$_.Directory.Parent.Parent.Name } -Descending |
    Select-Object -First 1
[IO.Directory]::CreateDirectory($OutputPath) | Out-Null
$baseReferences = @(
    (Join-Path $managed 'mscorlib.dll'),
    (Join-Path $managed 'System.dll'),
    (Join-Path $managed 'System.Core.dll'),
    (Join-Path $managed 'netstandard.dll')
)

function Invoke-StubCompiler {
    param([string]$AssemblyName, [string]$Source, [string[]]$ExtraReferences)

    $arguments = @(
        $compiler.FullName, '/noconfig', '/nostdlib+', '/target:library', '/optimize+',
        '/langversion:latest', '/nullable:disable',
        "/out:$(Join-Path $OutputPath ($AssemblyName + '.dll'))"
    )
    $arguments += ($baseReferences + $ExtraReferences) | ForEach-Object { "/reference:$_" }
    $arguments += $Source
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile $AssemblyName stub."
    }
}

Invoke-StubCompiler `
    -AssemblyName 'BepInEx' `
    -Source (Join-Path $GameRoot 'Tools\CompileStubs\BepInEx\Stubs.cs') `
    -ExtraReferences @((Join-Path $managed 'UnityEngine.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll'))
Invoke-StubCompiler `
    -AssemblyName '0Harmony' `
    -Source (Join-Path $GameRoot 'Tools\CompileStubs\Harmony\Stubs.cs') `
    -ExtraReferences @()
Write-Host "Compiled Mono-compatible verification stubs: $OutputPath"
