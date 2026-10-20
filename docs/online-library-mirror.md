# 在线曲库的镜像与取源

## 为什么要镜像

在线曲库原先只从 `raw.githubusercontent.com` 取目录和曲目。这个域名在国内基本不可达：
实测直连 12 秒收不到一个字节，走代理同样超时。结果是「在线曲库」页面对国内用户完全不可用。

现在客户端按固定顺序尝试多个镜像，任一成功即返回：

| 顺序 | 源 | 地址 |
| --- | --- | --- |
| 1 | Gitee | `https://gitee.com/qinqinding/MeowField_MidiLibrary/raw/main/` |
| 2 | Gitee（master 分支） | `https://gitee.com/qinqinding/MeowField_MidiLibrary/raw/master/` |
| 3 | GitHub | `https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/` |

注意 Gitee 与 GitHub 是**两个不同账号**：镜像托管在 Gitee 的 `qinqinding` 下，
上游仓库仍是 GitHub 的 `Tsundeer`。因此 `LibrarySources` 里分别用
`GiteeOwner` / `GitHubOwner` 两个常量，不能共用一个。

顺序定义在 `src/MeowField.Infrastructure.Network/LibrarySource.cs` 的 `LibrarySources.Default`。

国内实测（2026-10-20，本机直连）：

| 指标 | 结果 |
| --- | --- |
| Gitee 往返延迟 | 11 ms |
| `catalog.json.gz`（1.48 MB） | **1.07 s**，约 1.4 MB/s |
| `catalog.json`（4.58 MB） | 1.44 s |
| 单曲（17 KB，含中文文件名） | 0.46 s，`sha256` 与目录一致 |
| `raw.githubusercontent.com` | 12 秒收不到一个字节 |

## 当前状态

镜像已建好并完成首次同步，仓库地址 `https://gitee.com/qinqinding/MeowField_MidiLibrary`，
公开、默认分支 `main`，19,197 首曲目 + `catalog.json` + `catalog.json.gz`。

## 日常同步

上游更新后重新跑脚本即可：

```powershell
.\scripts\sync-midi-library-to-gitee.ps1
```

脚本做的事：

1. 克隆或更新本地镜像目录（默认 `%TEMP%\meowfield-midilibrary`），并硬重置到上游分支；
2. 由 `catalog.json` 生成 `catalog.json.gz`（4.58 MB → 1.48 MB）；
3. 有变更才提交；
4. 强制推送到 Gitee 的 `main` 分支。

> 镜像目录按只读镜像管理，本地改动每次都会被丢弃。要改曲库请改上游仓库。

## 建镜像时踩到的三个坑

1. **Gitee 拒绝浅克隆的推送**：`remote rejected ... (shallow update not allowed)`。
   用 `git clone --depth 1` 拉上游再推会失败，必须先 `git fetch --unshallow`。
   同步脚本用的是完整克隆，所以不受影响；手工操作时要注意。
2. **空仓库不能设为公开**：API 返回 `空仓库不支持设置为公开仓库`。顺序必须是
   先推内容、再 `PATCH {"private":false}` 转公开。
3. **PATCH 仓库接口必须带 `name` 字段**，否则报 `name is missing`；
   而空仓库带 `default_branch` 又会报 `The repository does not have the branch`。

仓库配额（Gitee 社区版）：单仓库 500 MB、单文件 50 MB、账号总容量 5 GB。
当前 `.git` 打包后 58 MB，工作区 287 MB，余量充足。

## 客户端行为

- **目录优先要 `catalog.json.gz`**：镜像没有这个文件时自动回落到 `catalog.json`，
  并在本次会话记住该源不再尝试 `.gz`，不会反复 404。
- **目录落盘缓存**：成功拉取后写到 `%LOCALAPPDATA%\MeowField\cache\online-catalog.json`。
  下次启动先读缓存，切换回「在线曲库」页是瞬时的；只有点「刷新曲库」才重新走网络。
- **全部镜像失败时用缓存兜底**：状态栏显示「已加载 N 首（离线缓存，点「刷新曲库」可重试）」，
  而不是给一个空白页。
- **单曲下载逐源重试**，并且每一首都校验 `sha256`。校验不通过就换下一个源，
  损坏或被替换的文件不会交给播放器——这也是敢用第三方镜像的前提。
- **错误提示是中文的**：不再把 `An error occurred while sending the request` 直接丢给用户。

## 上游更新后

重新跑一次脚本即可。上游 `catalog.json` 变化时 `.gz` 会同步重新生成；
`.NET` 的 `GZipStream` 不写时间戳，目录没变时不会产生多余提交。

## 已知限制

- 单次 HTTP 请求 30 秒超时。Gitee 侧正常，但 GitHub 兜底在国内通常就是超时失败，
  因此没有 Gitee 镜像时首次进入页面会等待约 30–60 秒才给出失败提示。
- Gitee 的 raw 会 302 跳转到带签名的 `raw.giteeusercontent.com`，客户端依赖重定向，
  不要关闭 `HttpClient` 的自动重定向。
- 镜像仓库里 `midi/` 目录有 19,197 个文件，Gitee 网页端浏览该目录会很慢，属正常现象。
