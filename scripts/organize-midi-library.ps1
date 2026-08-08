param(
    [string]$SourceDirectory = "E:\开放空间自动演奏测试用例\歌单",
    [string]$OutputDirectory = "E:\开放空间自动演奏测试用例\歌单-整理"
)

$ErrorActionPreference = "Stop"

function Get-SafeName([string]$name) {
    $trimmed = ($name -replace '\s+', ' ').Trim()
    $invalid = [Regex]::Escape((-join [IO.Path]::GetInvalidFileNameChars()))
    return [Regex]::Replace($trimmed, "[$invalid]", "_")
}

function Test-StandardMidi([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    try {
        $header = New-Object byte[] 4
        return $stream.Read($header, 0, 4) -eq 4 -and [Text.Encoding]::ASCII.GetString($header) -eq "MThd"
    }
    finally {
        $stream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Source directory was not found: $SourceDirectory"
}

$source = (Resolve-Path -LiteralPath $SourceDirectory).Path.TrimEnd('\')
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($source + '\\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output directory must not be inside the source directory."
}

$midiRoot = Join-Path $output "midi"
$quarantineRoot = Join-Path $output "quarantine"
New-Item -ItemType Directory -Force -Path $midiRoot, $quarantineRoot | Out-Null

$files = Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Extension -in '.mid', '.midi' }
$knownHashes = @{}
$tracks = [System.Collections.Generic.List[object]]::new()
$duplicates = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($source.Length).TrimStart('\')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $valid = Test-StandardMidi $file.FullName
    $category = $relativePath.Split('\')[0]
    if ($relativePath -notmatch '\\') { $category = "Uncategorized" }
    $category = Get-SafeName $category
    $displayName = Get-SafeName([IO.Path]::GetFileNameWithoutExtension($file.Name))

    if ($knownHashes.ContainsKey($hash)) {
        $duplicates.Add([PSCustomObject]@{ source = $relativePath; canonical = $knownHashes[$hash] })
        continue
    }

    $root = if ($valid) { $midiRoot } else { $quarantineRoot }
    $destinationDirectory = Join-Path $root $category
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    $destinationName = "{0}--{1}.mid" -f $displayName, $hash.Substring(0, 8)
    $destination = Join-Path $destinationDirectory $destinationName
    if (-not (Test-Path -LiteralPath $destination)) {
        try {
            New-Item -ItemType HardLink -Path $destination -Target $file.FullName | Out-Null
        }
        catch {
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }

    $libraryPath = $destination.Substring($output.Length).TrimStart('\').Replace('\', '/')
    $knownHashes[$hash] = $libraryPath
    if ($valid) {
        $tracks.Add([PSCustomObject]@{
            id = $hash.Substring(0, 16)
            name = $displayName
            category = $category
            path = $libraryPath
            sha256 = $hash
            bytes = $file.Length
            source = $relativePath
        })
    }
}

$catalog = [PSCustomObject]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    tracks = @($tracks | Sort-Object category, name)
}
$catalog | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath (Join-Path $output "catalog.json") -Encoding utf8
$duplicates | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath (Join-Path $output "duplicates.json") -Encoding utf8
@"
# MeowField MIDI Library

Generated from the local source library without modifying it.

- `midi/`: standard MIDI files, organized by source category
- `quarantine/`: files without a standard `MThd` header; excluded from the online catalog
- `catalog.json`: searchable index with SHA-256 checksums
- `duplicates.json`: exact-content duplicate source records
"@ | Set-Content -LiteralPath (Join-Path $output "README.md") -Encoding utf8

Write-Host "Source MIDI files: $($files.Count)"
Write-Host "Catalog tracks:    $($tracks.Count)"
Write-Host "Exact duplicates:  $($duplicates.Count)"
Write-Host "Output:            $output"
