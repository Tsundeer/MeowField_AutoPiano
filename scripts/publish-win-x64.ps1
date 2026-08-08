param(
    [string]$Configuration = "Release",
    [switch]$LegacyInnoInstaller
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\MeowField.App\MeowField.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$installerDirectory = Join-Path $repositoryRoot "artifacts\installer"
$installerScript = Join-Path $repositoryRoot "installer\MeowField_AutoPiano.iss"
$wixSource = Join-Path $repositoryRoot "installer\MeowField_AutoPiano.wxs"

[xml]$projectFile = Get-Content -LiteralPath $project
$version = @($projectFile.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "The application project must define a Version property."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "artifacts") -File -Filter "*.zip" -ErrorAction SilentlyContinue | Remove-Item -Force

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishDirectory -File -Filter "*.dylib" | Remove-Item -Force

if ($LegacyInnoInstaller) {
    $isccCandidates = @(
        (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $iscc = $isccCandidates | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 was not found." }
    & $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDirectory" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
}

if (Test-Path -LiteralPath $installerDirectory) {
    Remove-Item -LiteralPath $installerDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null
$wix = Join-Path $repositoryRoot "artifacts\tools\wix.exe"
if (-not (Test-Path -LiteralPath $wix)) {
    Write-Host "WiX CLI not found; installing wix 5.0.2 into artifacts\tools..."
    dotnet tool install --tool-path (Join-Path $repositoryRoot "artifacts\tools") wix --version 5.0.2
    if ($LASTEXITCODE -ne 0) { throw "WiX CLI installation failed with exit code $LASTEXITCODE." }
}
$msiPath = Join-Path $installerDirectory "MeowField_AutoPiano-$version-win-x64.msi"
& $wix build $wixSource -arch x64 -d "PublishDir=$publishDirectory" -d "Version=$version" -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "WiX MSI packaging failed with exit code $LASTEXITCODE." }

Write-Host "Published:     $publishDirectory"
Write-Host "MSI installer: $msiPath"
