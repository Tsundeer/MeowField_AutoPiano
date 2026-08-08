# Windows 发布与更新

MeowField_AutoPiano 使用 self-contained `win-x64` 发布，普通用户不需要另外安装 .NET Runtime。

发布脚本会生成普通多文件应用目录并交给 Velopack 打包。首次安装使用 `MeowField_AutoPiano-win-Setup.exe`；安装后程序由 Velopack 管理版本目录、快捷方式和更新流程。

设置页的“检查更新”会从 GitHub Releases 检查新版本。下载时 Velopack 优先使用 delta 包，只有差分包不可用时才回退到完整包。便携版不会自动覆盖自身，必须使用 Setup 安装版才能启用自动更新。

构建：

```powershell
.\scripts\publish-win-x64.ps1
```

脚本会自动安装本地 `vpk` 工具，并在 `artifacts\velopack` 生成 Setup、完整包、更新索引和便携包。`bin/`、`obj/` 和 `artifacts/` 不提交到 GitHub。
