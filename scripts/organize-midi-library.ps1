param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
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
    finally { $stream.Dispose() }
}

function Get-Category([string]$title) {
    $value = $title.ToLowerInvariant()
    if ($value -match 'drum|drums|\bbpm\b|ezdrummer') { return 'drums-rhythm' }
    if ($value -match 'miku|vocaloid|\butau\b') { return 'virtual-singers' }
    if ($value -match 'ff\d*|final fantasy|deemo|rabi-ribi|minecraft|undertale|\bgame\b') { return 'game-music' }
    if ($value -match 'anime|eva|jojo') { return 'anime-acg' }
    if ($value -match 'beethoven|mozart|bach|chopin|debussy|classical') { return 'classical-light' }
    $hasAsianScript = $title.ToCharArray() | Where-Object {
        $code = [int][char]$_
        ($code -ge 0x3040 -and $code -le 0x30FF) -or ($code -ge 0x3400 -and $code -le 0x9FFF)
    } | Select-Object -First 1
    if ($null -ne $hasAsianScript) { return 'asian-pop' }
    if ($value -match '[a-z]') { return 'world-pop' }
    return 'uncategorized'
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) { throw "Source directory was not found: $SourceDirectory" }
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path.TrimEnd('\')
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
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
    $displayName = Get-SafeName([IO.Path]::GetFileNameWithoutExtension($file.Name))
    if ($displayName -match '^\d+$') { $displayName = "Track #$displayName" }
    $category = Get-Category $displayName
    if ($knownHashes.ContainsKey($hash)) {
        $duplicates.Add([PSCustomObject]@{ source = $relativePath; canonical = $knownHashes[$hash] })
        continue
    }

    $root = if ($valid) { $midiRoot } else { $quarantineRoot }
    $destinationDirectory = Join-Path $root $category
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    $destination = Join-Path $destinationDirectory ("{0}--{1}.mid" -f $displayName, $hash.Substring(0, 8))
    try { New-Item -ItemType HardLink -Path $destination -Target $file.FullName | Out-Null }
    catch { Copy-Item -LiteralPath $file.FullName -Destination $destination }

    $libraryPath = $destination.Substring($output.Length).TrimStart('\').Replace('\', '/')
    $knownHashes[$hash] = $libraryPath
    if ($valid) {
        $tracks.Add([PSCustomObject]@{ id = $hash.Substring(0, 16); name = $displayName; category = $category; path = $libraryPath; sha256 = $hash; bytes = $file.Length })
    }
}

$catalog = [PSCustomObject]@{ schemaVersion = 1; generatedAt = [DateTimeOffset]::UtcNow.ToString("O"); tracks = @($tracks | Sort-Object category, name) }
$catalog | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath (Join-Path $output "catalog.json") -Encoding utf8
$duplicates | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath (Join-Path $output "duplicates.json") -Encoding utf8
Write-Host "Source MIDI files: $($files.Count)"
Write-Host "Catalog tracks:    $($tracks.Count)"
Write-Host "Exact duplicates:  $($duplicates.Count)"
Write-Host "Output:            $output"
