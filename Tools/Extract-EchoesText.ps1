param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Translation\source-catalog.json')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata

function Test-JapaneseText {
    param([string]$Text)

    return $Text -match '[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]' -and
        $Text -notmatch '[\x00-\x08\x0B\x0C\x0E-\x1F]'
}

function Get-SerializedStrings {
    param([string]$Path)

    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $result = [Collections.Generic.List[object]]::new()

    # Unity serialized strings use an aligned little-endian byte length followed by UTF-8.
    for ($offset = 0; $offset -le $bytes.Length - 8; $offset += 4) {
        $length = [BitConverter]::ToInt32($bytes, $offset)
        if ($length -lt 2 -or $length -gt 16384 -or $offset + 4 + $length -gt $bytes.Length) {
            continue
        }

        try {
            $text = $strictUtf8.GetString($bytes, $offset + 4, $length)
        }
        catch {
            continue
        }

        if (Test-JapaneseText $text) {
            $result.Add([pscustomobject]@{
                source = [IO.Path]::GetFileName($Path)
                offset = $offset
                ownerType = $null
                ownerMethod = $null
                text = ($text -replace "`r`n", "`n")
            })
        }
    }

    return $result
}

function Get-DllStrings {
    param([string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $result = [Collections.Generic.List[object]]::new()

    try {
        foreach ($typeHandle in $reader.TypeDefinitions) {
            $type = $reader.GetTypeDefinition($typeHandle)
            $typeName = $reader.GetString($type.Name)
            $namespace = $reader.GetString($type.Namespace)
            if ($namespace) {
                $typeName = "$namespace.$typeName"
            }

            foreach ($methodHandle in $type.GetMethods()) {
                $method = $reader.GetMethodDefinition($methodHandle)
                if ($method.RelativeVirtualAddress -eq 0) {
                    continue
                }

                $methodName = $reader.GetString($method.Name)
                $il = [Reflection.Metadata.PEReaderExtensions]::GetMethodBody(
                    $pe,
                    $method.RelativeVirtualAddress
                ).GetILBytes()

                for ($index = 0; $index -le $il.Length - 5; $index++) {
                    if ($il[$index] -ne 0x72) {
                        continue
                    }

                    $token = [BitConverter]::ToInt32($il, $index + 1)
                    if (($token -band 0xFF000000) -ne 0x70000000) {
                        continue
                    }

                    try {
                        $handle = [Reflection.Metadata.Ecma335.MetadataTokens]::UserStringHandle(
                            $token -band 0x00FFFFFF
                        )
                        $text = $reader.GetUserString($handle)
                    }
                    catch {
                        continue
                    }

                    if (Test-JapaneseText $text) {
                        $result.Add([pscustomobject]@{
                            source = 'Assembly-CSharp.dll'
                            offset = $token -band 0x00FFFFFF
                            ownerType = $typeName
                            ownerMethod = $methodName
                            text = ($text -replace "`r`n", "`n")
                        })
                    }
                }
            }
        }
    }
    finally {
        $pe.Dispose()
        $stream.Dispose()
    }

    return $result
}

$occurrences = [Collections.Generic.List[object]]::new()
foreach ($relativePath in @(
    'ECHOES_Data\sharedassets0.assets',
    'ECHOES_Data\level0'
)) {
    $path = Join-Path $GameRoot $relativePath
    foreach ($item in Get-SerializedStrings $path) {
        $occurrences.Add($item)
    }
}

$assemblyPath = Join-Path $GameRoot 'ECHOES_Data\Managed\Assembly-CSharp.dll'
foreach ($item in Get-DllStrings $assemblyPath) {
    $occurrences.Add($item)
}

$byText = [ordered]@{}
foreach ($item in $occurrences) {
    if (-not $byText.Contains($item.text)) {
        $byText[$item.text] = [Collections.Generic.List[object]]::new()
    }
    $byText[$item.text].Add([ordered]@{
        source = $item.source
        offset = $item.offset
        ownerType = $item.ownerType
        ownerMethod = $item.ownerMethod
    })
}

$entries = [Collections.Generic.List[object]]::new()
$index = 1
foreach ($pair in $byText.GetEnumerator()) {
    $entries.Add([ordered]@{
        id = 'E{0:D4}' -f $index
        original = $pair.Key
        translation = ''
        note = ''
        occurrences = $pair.Value
    })
    $index++
}

$catalog = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    game = 'ECHOES'
    unityVersion = '6000.2.6f2'
    entryCount = $entries.Count
    entries = $entries
}

$outputDirectory = Split-Path -Parent $OutputPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Wrote $($entries.Count) unique Japanese strings to $OutputPath"
