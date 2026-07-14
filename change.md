# Change Log — 2026-07-15 — EchoMinutes 仓库初始化与 GitHub Release 自动更新

## 目标

- 将项目首次推送到 `https://github.com/luckykevvv/echo-minutes.git`。
- 从 GitHub Releases 检查新版本与更新说明。
- 启动后发现新版本时弹窗；设置页支持手动检查并安装。
- 更新时保留用户配置、已下载模型、录音、导出和数据库。

## 更新检查与下载

- 新增 `MeetingTransfer.Core/Updates/GitHubReleaseClient.cs`：
  - 查询 `luckykevvv/echo-minutes` 的 latest release。
  - 对比当前程序集版本与 `vX.Y.Z` tag。
  - 要求 Release 同时包含 `echo-minutes-win-x64.zip` 和 `.sha256`。
  - 只接受 GitHub HTTPS 资产，限制压缩包不超过 1 GiB。
  - 流式下载并报告进度，安装前强制校验 SHA256。
- 启动完成或新手引导结束后异步检查；网络失败不打断启动。
- 新增独立 `UpdateWindow`，展示 Release 标题、版本、更新时间、更新说明、包大小、下载进度和稍后/更新操作。

## 设置页

- 新增 `Updates` 标签：显示当前版本、GitHub Release 来源、状态和“Check for updates”按钮。
- 修复设置窗口原有 TabControl 模板只有一行的问题。此前标签栏与内容重叠，`Speech models / Tools / Updates` 会被模型列表盖住；现改为自动高度标签栏 + 剩余内容区。
- 实机视觉验收确认三个标签和 Updates 卡片均正常显示。
- 手动检查会进入加载状态；成功、无更新和失败均会恢复按钮并给出明确结果。

## 独立更新器

- 新增 `MeetingTransfer.Updater`，发布时自动放入 `Updater/`。
- 主程序完成下载后将更新器复制到临时目录并启动，然后正常关闭。
- 更新器等待主进程退出，安全解压、备份被覆盖文件、复制新版本并自动重启。
- 复制失败时恢复已备份文件。
- 永不覆盖 `appsettings.json`、`models.json`、`data/`、`recordings/`、`exports/`。
- 发布包不包含模型权重，因此用户 `models/` 下的已下载模型不会被删除或覆盖。

## Release 自动化

- 新增 `.github/workflows/release.yml`。
- 推送 `v*` tag 后自动 restore、build、test、win-x64 publish。
- 工作流阻止模型权重和用户数据进入 Release。
- 第三方运行时 DLL/EXE 使用 Git LFS；Actions checkout 启用 `lfs: true`。
- 自动生成固定名称 ZIP、SHA256 文件、GitHub Release 和更新说明。
- README 已补充应用更新机制与发布 tag 命令。

## 验证

- Release build：0 warning / 0 error。
- 测试：74/74 通过，其中新增版本 tag 解析、Release JSON、SHA256 通过/拒绝测试及 WPF 更新页面渲染覆盖。
- `dotnet format --verify-no-changes`：通过。
- 独立更新器端到端探针：程序 EXE/DLL 和新运行时更新成功；配置、模型配置、SQLite 数据、用户模型均保持不变；ExitCode 0。
- 正式包：55 个文件，265,011,162 bytes。
- 模型权重：0；运行期用户数据：0；`Updater/EchoMinutes.Updater.exe` 存在。
- `MeetingTransfer.App.dll` SHA256：`5655D2CB9BCD5EDC9A5E6F43BD5857C20D822AD0A43CDEFCB4785CBB210A232A`。
- 本机对 GitHub 的应用内实网检查遇到 SSL 连接错误；失败状态和提示已验证，不影响启动。核心 Release 解析、下载、哈希与安装流程由自动化和端到端本地探针验证。

## 执行命令

```powershell
dotnet restore MeetingTransfer.sln -p:NuGetAudit=false
dotnet restore src\MeetingTransfer.App\MeetingTransfer.App.csproj -r win-x64 -p:NuGetAudit=false
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish\win-x64
git -c http.proxy= -c https.proxy= ls-remote https://github.com/luckykevvv/echo-minutes.git
```

正式发布前均先解析并检查目标绝对路径位于工作区，再清理旧 `publish/win-x64`。更新器探针在 `%TEMP%` 的唯一目录中运行，并在校验后安全清理。
