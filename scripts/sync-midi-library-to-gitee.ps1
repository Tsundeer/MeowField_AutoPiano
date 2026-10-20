<#
.SYNOPSIS
    把 MeowField_MidiLibrary 从 GitHub 镜像到 Gitee，并生成压缩目录 catalog.json.gz。

.DESCRIPTION
    客户端按 Gitee → GitHub 的顺序取源，
    目录优先请求 catalog.json.gz，缺失时自动回落到 catalog.json。

    首次运行会完整传输约 240 MB；之后只推变更的对象。
    本地镜像目录按只读镜像使用，每次都会硬重置到上游分支，其中的本地改动会被丢弃。

.PARAMETER WorkDir
    本地镜像工作目录，默认在临时目录下。

.PARAMETER GiteeRemote
    Gitee 侧仓库地址。仓库需先在 Gitee 建好（空仓库即可）。

.PARAMETER Branch
    推送到的分支名，必须与客户端 LibrarySources 中的分支一致，默认 main。

.EXAMPLE
    .\sync-midi-library-to-gitee.ps1 -WhatIf

.EXAMPLE
    .\sync-midi-library-to-gitee.ps1 -Branch main
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'meowfield-midilibrary'),
    [string]$GitHubRemote = 'https://github.com/Tsundeer/MeowField_MidiLibrary.git',
    [string]$GiteeRemote = 'https://gitee.com/qinqinding/MeowField_MidiLibrary.git',
    [string]$Branch = 'main',
    [switch]$SkipGzipCatalog
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$GitArgs)

    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') 失败，退出码 $LASTEXITCODE"
    }
}

function Compress-Catalog {
    param([string]$Source, [string]$Target)

    $input = [System.IO.File]::OpenRead($Source)
    try {
        $output = [System.IO.File]::Create($Target)
        try {
            # .NET 的 GZipStream 不写原始时间戳，相同输入产出相同字节，
            # 上游目录没变时重新压缩不会产生多余提交。
            $gzip = New-Object System.IO.Compression.GZipStream($output, [System.IO.Compression.CompressionLevel]::Optimal)
            try { $input.CopyTo($gzip) } finally { $gzip.Dispose() }
        }
        finally { $output.Dispose() }
    }
    finally { $input.Dispose() }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw '未找到 git，请先安装 Git for Windows。'
}

Write-Host "镜像目录：$WorkDir"
Write-Host "上游：$GitHubRemote ($Branch)"
Write-Host "目标：$GiteeRemote ($Branch)"

if (-not (Test-Path (Join-Path $WorkDir '.git'))) {
    if ($PSCmdlet.ShouldProcess($WorkDir, '克隆上游仓库')) {
        $parent = Split-Path -Parent $WorkDir
        if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        Invoke-Git @('clone', '--branch', $Branch, $GitHubRemote, $WorkDir)
    }
}
else {
    Push-Location $WorkDir
    try {
        Write-Host '拉取上游…'
        Invoke-Git @('fetch', $GitHubRemote, $Branch)
        Write-Host '重置到上游最新（镜像目录的本地改动会被丢弃）…'
        Invoke-Git @('reset', '--hard', 'FETCH_HEAD')
    }
    finally { Pop-Location }
}

if (-not (Test-Path (Join-Path $WorkDir '.git'))) {
    # -WhatIf 时克隆未执行，后续步骤没有工作目录可用，直接收工。
    Write-Host '预览模式：未克隆仓库，后续步骤已跳过。'
    return
}

Push-Location $WorkDir
try {
    $catalogPath = Join-Path $WorkDir 'catalog.json'
    if (-not (Test-Path $catalogPath)) {
        throw "上游缺少 catalog.json，请确认 $GitHubRemote 的结构未变。"
    }

    if (-not $SkipGzipCatalog) {
        $gzipPath = Join-Path $WorkDir 'catalog.json.gz'
        Write-Host '生成 catalog.json.gz…'
        Compress-Catalog -Source $catalogPath -Target $gzipPath
        $rawSize = (Get-Item $catalogPath).Length
        $gzipSize = (Get-Item $gzipPath).Length
        $ratio = [math]::Round($gzipSize / $rawSize * 100, 1)
        Write-Host ("  目录 {0:N0} B → {1:N0} B（{2}%）" -f $rawSize, $gzipSize, $ratio)
    }

    $midiDirectory = Join-Path $WorkDir 'midi'
    if (Test-Path $midiDirectory) {
        $trackCount = (Get-ChildItem -Path $midiDirectory -Filter *.mid -Recurse -File).Count
        Write-Host "曲目文件：$trackCount 个"
    }

    Invoke-Git @('add', '--all')
    $staged = @(& git diff --cached --name-only)
    if ($staged.Count -gt 0) {
        Write-Host "待提交：$($staged.Count) 个文件"
        Invoke-Git @(
            '-c', 'user.name=MeowField Mirror',
            '-c', 'user.email=mirror@local',
            'commit', '-m', 'chore: sync from upstream and refresh compressed catalog'
        )
    }
    else {
        Write-Host '没有需要提交的变更。'
    }

    $remotes = @(& git remote)
    if ($remotes -notcontains 'gitee') {
        Invoke-Git @('remote', 'add', 'gitee', $GiteeRemote)
    }
    else {
        Invoke-Git @('remote', 'set-url', 'gitee', $GiteeRemote)
    }

    if ($PSCmdlet.ShouldProcess($GiteeRemote, "强制推送 $Branch")) {
        Write-Host '推送到 Gitee（首次约 240 MB，请耐心等待）…'
        Invoke-Git @('push', 'gitee', "HEAD:$Branch", '--force')
        Write-Host ''
        Write-Host "完成。若 Gitee 仓库的默认分支不是 $Branch，请到仓库设置里改过来，或换用 -Branch 与之一致。"
    }
}
finally { Pop-Location }
