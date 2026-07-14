# Change Log

## 2026-07-09 - GUI 设置与 Windows exe 发布

### 任务目标
- 将应用发布成可直接运行的 Windows `.exe`。
- 在 GUI 内提供设置入口，允许修改 STT 引擎、ffmpeg 路径、sherpa-onnx 可执行文件与模型路径。
- 继续保持 `dotnet build` 与 `dotnet test` 可通过。

### 已执行指令
```powershell
Get-Content -Raw C:\Users\C3EZ\.codex\skills\front-end-design\SKILL.md
git status --short
Get-ChildItem -Force; rg --files
New-Item -ItemType Directory -Force -Path change | Out-Null
Move-Item -LiteralPath change.md -Destination change\change-01.md
dotnet build MeetingTransfer.sln
dotnet test MeetingTransfer.sln --no-build
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-ChildItem publish\win-x64 | Select-Object Name,Length,LastWriteTime
Test-Path publish\win-x64\MeetingTransfer.App.exe
Get-Item publish\win-x64\MeetingTransfer.App.exe | Select-Object FullName,Length,LastWriteTime
```

### 变更记录
- 已将上一项任务记录归档到 `change/change-01.md`。
- 新增 GUI 设置窗口：
  - 可切换 STT 引擎：`Mock` / `SherpaOnnx`。
  - 可设置 `ffmpeg.exe` 路径。
  - 可设置 sherpa-onnx online/offline executable。
  - 可设置 `tokens`、`encoder`、`decoder`、`joiner` 模型路径。
  - 可编辑 online/offline arguments template。
- 主界面左侧新增 `Settings` 按钮。
- 新增 `SettingsFileService`，首次运行时会在 exe 同目录生成可写的 `appsettings.json` 和 `models.json`。
- 将配置模型属性改成可写，支持 GUI 保存。
- 移除 App 项目中不再使用的 `Microsoft.Extensions.Configuration.*` 依赖。
- 更新 `README.md`，加入发布 exe 与 GUI 设置说明。

### 验证结果
- `dotnet build MeetingTransfer.sln` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln --no-build` 通过：
  - `4` passed
  - `0` failed
- `dotnet publish ... -o publish\win-x64` 通过。
- 已生成 exe：
  - `C:\Users\C3EZ\Documents\Meeting_Transfer\publish\win-x64\MeetingTransfer.App.exe`
  - 大小：`151552` bytes

### 说明
- 当前发布是 framework-dependent Windows exe，依赖本机已安装的 .NET 8 Desktop Runtime；本机已经安装。
- `publish/` 已在 `.gitignore` 中忽略，不会进入 Git 跟踪。
