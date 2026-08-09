param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\MeowField.App\MeowField.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$installerDirectory = Join-Path $repositoryRoot "artifacts\installer"
$installerScript = Join-Path $repositoryRoot "installer\MeowField_AutoPiano.iss"

[xml]$projectFile = Get-Content -LiteralPath $project
$version = @($projectFile.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($version)) { throw "The application project must define a Version property." }

if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "artifacts") -File -Filter "*.zip" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $installerDirectory -File -ErrorAction SilentlyContinue | Remove-Item -Force

dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $publishDirectory -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
Get-ChildItem -LiteralPath $publishDirectory -File -Filter "*.dylib" | Remove-Item -Force

$isccCandidates = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 was not found." }
& $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDirectory" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

Write-Host "Published: $publishDirectory"
Write-Host "Installer: $(Join-Path $installerDirectory "MeowField_AutoPiano-$version-win-x64-Setup.exe")"
