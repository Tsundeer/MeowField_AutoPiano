param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller,
    [switch]$SkipVelopack
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\MeowField.App\MeowField.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$installerScript = Join-Path $repositoryRoot "installer\MeowField_AutoPiano.iss"
$velopackDirectory = Join-Path $repositoryRoot "artifacts\velopack"

[xml]$projectFile = Get-Content -LiteralPath $project
$version = @($projectFile.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "The application project must define a Version property."
}

$portableArchive = Join-Path $repositoryRoot "artifacts\MeowField_AutoPiano-$version-win-x64-portable.zip"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE. Close any running copy from the publish directory and retry."
}

Get-ChildItem -LiteralPath $publishDirectory -File -Filter "*.dylib" | Remove-Item -Force

if (Test-Path -LiteralPath $portableArchive) {
    Remove-Item -LiteralPath $portableArchive -Force
}
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $portableArchive -CompressionLevel Optimal

if (-not $SkipInstaller) {
    $isccCandidates = @(
        (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    $iscc = $isccCandidates | Select-Object -First 1
    if (-not $iscc) {
        throw "Inno Setup 6 was not found. Install it or use -SkipInstaller for a portable build only."
    }

    & $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDirectory" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipVelopack) {
    $vpk = Join-Path $repositoryRoot "artifacts\tools\vpk.exe"
    if (-not (Test-Path -LiteralPath $vpk)) {
        Write-Host "Velopack CLI not found; installing vpk 1.2.0 into artifacts\tools..."
        dotnet tool install --tool-path (Join-Path $repositoryRoot "artifacts\tools") vpk --version 1.2.0
        if ($LASTEXITCODE -ne 0) {
            throw "Velopack CLI installation failed with exit code $LASTEXITCODE."
        }
    }
    if (Test-Path -LiteralPath $velopackDirectory) {
        Remove-Item -LiteralPath $velopackDirectory -Recurse -Force
    }
    & $vpk pack `
        --packId "MeowField_AutoPiano" `
        --packVersion $version `
        --packDir $publishDirectory `
        --mainExe "MeowField_AutoPiano.exe" `
        --packTitle "MeowField_AutoPiano" `
        --packAuthors "薮猫" `
        --icon (Join-Path $repositoryRoot "src\MeowField.App\Assets\MeowField_AutoPiano.ico") `
        --runtime "win-x64" `
        --outputDir $velopackDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Velopack packaging failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Published:        $publishDirectory"
Write-Host "Portable archive: $portableArchive"
if (-not $SkipInstaller) {
    Write-Host "Installer:        $(Join-Path $repositoryRoot "artifacts\installer")"
}
if (-not $SkipVelopack) {
    Write-Host "Velopack feed:    $velopackDirectory"
}
