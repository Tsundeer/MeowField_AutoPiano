param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\MeowField.App\MeowField.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$archivePath = Join-Path $repositoryRoot "artifacts\MeowField-AutoPlay-Lite-win-x64.zip"

if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublish = (Resolve-Path -LiteralPath $publishDirectory).Path
    $expectedPublish = [System.IO.Path]::GetFullPath($publishDirectory)
    if (-not $resolvedPublish.Equals($expectedPublish, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected publish directory: $resolvedPublish"
    }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE. Close any running copy from the publish directory and retry."
}

Get-ChildItem -LiteralPath $publishDirectory -File -Filter "*.dylib" | Remove-Item -Force

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "Published: $publishDirectory"
Write-Host "Archive:   $archivePath"
