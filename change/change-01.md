# Change Log

## 2026-07-08 - 初始化 Windows 本地会议转写 GUI 应用

### 任务目标
- 初始化 `Meeting_Transfer` 仓库为 Windows 优先、完全本地运行的会议录音/录像转写 GUI 应用。
- 建立 `.NET 8 WPF + NAudio + SQLite + sherpa-onnx` 的项目骨架。
- 支持实时音频捕获、STT 抽象、sherpa-onnx 适配层、说话人/转写数据模型、导出、文件导入和测试骨架。

### 已执行指令
```powershell
Get-Content -Raw C:\Users\C3EZ\.codex\skills\front-end-design\SKILL.md
git status --short --branch
dotnet --info
Get-ChildItem -Force
Get-ChildItem -Force .agents; Get-ChildItem -Force .codex
if (Test-Path change.md) { Get-Content -Raw change.md } else { 'NO_CHANGE_MD' }
git ls-files -v | Select-String '^S'
where.exe msbuild
where.exe dotnet
rg -n "using var capture|Core\.Transcripts|PackageReference|TargetFramework|TODO|throw new NotImplemented"
rg -n "\bLock\b|using var capture|Core\.Transcripts|NotImplemented|TODO"
Get-ChildItem -Recurse -File | Select-Object FullName
rg --files | sort
Get-ChildItem -Recurse -Include *.csproj,*.xaml,*.props,*.json | ForEach-Object { try { if ($_.Extension -eq '.json') { Get-Content -Raw $_.FullName | ConvertFrom-Json | Out-Null } else { [xml](Get-Content -Raw $_.FullName) | Out-Null }; "OK $($_.FullName)" } catch { "FAIL $($_.FullName): $($_.Exception.Message)" } }
dotnet build MeetingTransfer.sln
git status --short
```

### 环境观察
- 当前仓库尚无提交，工作区原始内容只有 `.git` 以及空的 `.agents`、`.codex` 目录。
- 当前机器只有 .NET runtime，没有 .NET SDK；因此本次无法使用 `dotnet new`、`dotnet build` 或 `dotnet test` 做实际编译验证。
- 当前仓库没有 skip-worktree 文件。

### 涉及文件
- 新增解决方案与项目骨架：`MeetingTransfer.sln`、`Directory.Build.props`、`src/MeetingTransfer.*`、`tests/MeetingTransfer.Tests`。
- 新增配置与忽略规则：`.gitignore`、`appsettings.example.json`、`models.example.json`。
- 新增说明文档：`README.md`。
- 新增核心能力：
  - transcript/session/speaker/word timing 模型。
  - txt/md/srt/vtt/json 导出。
  - SQLite transcript store。
  - ffmpeg 媒体导入音频抽取。
  - WASAPI system audio + microphone capture。
  - 每个 source 的 WAV 原始录音保存。
  - STT 抽象、mock engine、sherpa-onnx 本地可执行文件适配层。
  - WPF 三栏工作台 UI。
  - xUnit 测试骨架。

### 验证记录
- `*.csproj`、`*.xaml`、`*.props` XML 解析通过。
- `appsettings.example.json`、`models.example.json` JSON 解析通过。
- `rg` 静态检查未发现 `TODO`、`NotImplemented`、错误的 `using var capture` 或 .NET 9 `Lock` 类型残留。
- `dotnet build MeetingTransfer.sln` 已执行，但失败原因为本机没有 .NET SDK：
  - `No .NET SDKs were found.`
  - `The application 'build' does not exist.`
- `git status --short` 显示本次所有项目文件均为新增未跟踪文件。

## 2026-07-08 - 安装 .NET 8 SDK 并验证项目

### 任务目标
- 为当前 Windows 本地开发环境安装 .NET 8 SDK。
- 安装后运行 `dotnet --info`、`dotnet build` 和 `dotnet test` 验证会议转写应用。

### 计划执行指令
```powershell
winget --version
winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-source-agreements --accept-package-agreements
dotnet --info
dotnet build MeetingTransfer.sln
dotnet test MeetingTransfer.sln
```

### 实际执行指令
```powershell
winget --version
winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-source-agreements --accept-package-agreements
Get-Process | Where-Object { $_.ProcessName -match 'dotnet|sdk|msiexec|winget|WindowsPackageManager|setup' } | Select-Object ProcessName,Id,CPU,StartTime
Get-Process dotnet-sdk-8.0.422-win-x64,winget -ErrorAction SilentlyContinue | Select-Object ProcessName,Id,CPU,Responding,MainWindowTitle,StartTime,Path
dotnet --list-sdks; dotnet --info
$installer = (Get-Process dotnet-sdk-8.0.422-win-x64 -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Path)
Get-Process dotnet-sdk-8.0.422-win-x64,winget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$log = Join-Path $env:TEMP 'dotnet-sdk-8.0.422-install.log'
$p = Start-Process -FilePath $installer -ArgumentList @('/install','/quiet','/norestart','/log', $log) -Wait -PassThru
dotnet --info
dotnet --list-sdks
dotnet build MeetingTransfer.sln
dotnet nuget list source
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
dotnet build MeetingTransfer.sln
dotnet test MeetingTransfer.sln --no-build
```

### 安装结果
- `winget` 安装流程启动了 GUI 安装器并长时间等待。
- 已停止等待中的 GUI 安装器，改用同一个安装包静默安装。
- .NET SDK 安装成功：
  - SDK: `8.0.422`
  - MSBuild: `17.11.48`
  - Runtime: `Microsoft.NETCore.App 8.0.28`
  - Windows Desktop Runtime: `Microsoft.WindowsDesktop.App 8.0.28`
- 安装日志：`C:\Users\C3EZ\AppData\Local\Temp\dotnet-sdk-8.0.422-install.log`

### 额外修复
- 添加 NuGet 官方源：`https://api.nuget.org/v3/index.json`，因为本机原先 `dotnet nuget list source` 显示没有任何源。
- 修复 `SqliteTranscriptStore` 中 async transaction 返回 `DbTransaction` 导致的编译错误。
- 给测试项目添加 xUnit global using，修复 `[Fact]` 无法解析的编译错误。

### 验证结果
- `dotnet build MeetingTransfer.sln` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln --no-build` 通过：
  - `4` passed
  - `0` failed
  - `0` skipped
