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

# 清理旧产物。这里刻意不用 `Get-ChildItem | Remove-Item` 管道：空结果会以空管线
# 调用 Remove-Item 而报 "missing path operand"，某些安全策略还会让管道绑定失败
# （InputObjectNotBound）。
#
# 只读的交付目录不能因为一个被占用的文件就中断打包：删不掉就移到 _stale 隔离，
# 连移动都失败（句柄未释放）就换一个带时间戳的新目录重新发布，绝不覆盖半成品。
$staleRoot = Join-Path $repositoryRoot "artifacts\_stale"

function Reset-PublishDirectory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $Path }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        return $Path
    }
    catch {
        New-Item -ItemType Directory -Path $staleRoot -Force | Out-Null
        $quarantine = Join-Path $staleRoot ((Split-Path -Leaf $Path) + "-" + (Get-Date -Format "yyyyMMddHHmmss"))
        try {
            Move-Item -LiteralPath $Path -Destination $quarantine -Force -ErrorAction Stop
            Write-Warning "无法删除 $Path，已移到 $quarantine。"
            return $Path
        }
        catch {
            $fresh = "$Path-" + (Get-Date -Format "yyyyMMddHHmmss")
            Write-Warning "无法清空 $Path（文件被占用），改用新目录 $fresh。"
            return $fresh
        }
    }
}

function Remove-EachFile {
    param([string]$Directory, [string]$Filter = "*")
    if (-not (Test-Path -LiteralPath $Directory)) { return }
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -File -Filter $Filter -ErrorAction SilentlyContinue)) {
        try {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
        }
        catch {
            try {
                New-Item -ItemType Directory -Path $staleRoot -Force | Out-Null
                Move-Item -LiteralPath $file.FullName -Destination (Join-Path $staleRoot $file.Name) -Force -ErrorAction Stop
                Write-Warning "无法删除 $($file.FullName)，已移到 $staleRoot。"
            }
            catch {
                # 删不掉也移不走（句柄未释放）时不能让打包失败，留它在包里便是。
                Write-Warning "无法删除或移动 $($file.FullName)，已跳过。"
            }
        }
    }
}

$publishDirectory = Reset-PublishDirectory $publishDirectory
Remove-EachFile -Directory (Join-Path $repositoryRoot "artifacts") -Filter "*.zip"
Remove-EachFile -Directory $installerDirectory

dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $publishDirectory -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
Remove-EachFile -Directory $publishDirectory -Filter "*.dylib"

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
