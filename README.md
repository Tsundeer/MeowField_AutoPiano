# MeowField_AutoPiano

MeowField_AutoPiano 是一款面向 Windows 的 MIDI 自动演奏软件，适配游戏《开放空间》

当前版本：**2.1.1**（2.0 为正式版基线）

项目主页：<https://github.com/Tsundeer/MeowField_AutoPiano>

## 软件能做什么

- 自动播放 MIDI 文件
- 支持钢琴、架子鼓、麦克风模式
- 支持目标窗口绑定和游戏键位映射
- 支持 SendInput、窗口消息两种输入方式
- 支持播放队列、曲库、播放预设和定时播放
- 支持速度、移调、复音数、链路补偿等参数调整
- 支持浅色/深色主题和中英文界面
- 支持在设置页检查新版本

## 使用方法

1. 启动 `MeowField_AutoPiano.exe`。
2. 在“播放”页面选择目标游戏窗口。
3. 点击“打开 MIDI”，或将 `.mid` / `.midi` 文件拖入窗口。
4. 选择乐器、输入方式和键位映射。
5. 点击播放，或使用设置中显示的全局播放快捷键。

使用 `SendInput` 时，目标游戏必须位于前台。窗口失去前台后，软件会停止发送并显示提示，不会继续向其他程序输入按键。

## 架子鼓模式

架子鼓使用 General MIDI 鼓组音符，并将标准鼓音归并到游戏可用的鼓位。未被游戏单独提供的鼓音会映射到最接近的鼓位，不会被静默跳过。

## 设置与更新

在左侧“设置”页面可以：

- 切换主题和语言
- 查看播放安全提示
- 检查 GitHub 最新版本
- 打开版本发布页
- 查看软件版本、作者署名和项目地址

更新检查只读取 GitHub Releases 的版本信息，不会后台覆盖本地程序。发现新版本后，请从发布页下载新的 Windows ZIP 包并解压替换。

## Windows 要求

- Windows 10 或更高版本
- 64 位系统

正式发布包为自包含版本，普通用户不需要安装 .NET SDK、Python 或 Node.js。

## 数据和日志

软件设置、曲库索引、播放列表和预设保存在 Windows 用户本地应用数据目录。日志可以在“日志与诊断”页面导出为诊断包。

## 开源与构建

本仓库只提交源代码、测试和构建脚本，不提交 `bin/`、`obj/`、`artifacts/` 或其他编译产物。普通用户请直接下载 GitHub Releases 中的 ZIP；开发者可以使用 .NET 10 SDK 构建：

```powershell
dotnet build MeowField.sln
dotnet test MeowField.sln --no-build
```

生成 Windows 发布包：

```powershell
.\scripts\publish-win-x64.ps1
```

构建输出仅用于本地发布，不应提交到 GitHub。

## 许可

本项目采用 GPL-3.0 开源许可，详见 [`LICENSE`](LICENSE)。
