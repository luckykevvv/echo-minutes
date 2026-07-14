# Change Log — 2026-07-15 — 首次启动新手引导

## 任务范围

- 第一次启动 Meeting Transfer 时显示新手引导。
- 解释本地数据、模型下载方式以及实时/离线两条工作流。
- 完成或跳过后持久化状态，后续启动不重复弹出。
- 在引导内直接选择、下载并启用模型，不要求先跳转设置页。

## 修改内容

### 1. 三步首次启动引导

- 新增 `src/MeetingTransfer.App/OnboardingWindow.xaml` 与 `OnboardingWindow.xaml.cs`。
- 第一步“开始之前”：说明录音、转写和导出默认保存在本机，发布包不含模型。
- 第二步“准备模型”已改为可操作的模型库：
  - 按“离线转写 / 实时转写 / 功能资源”分组显示全部模型。
  - 每张模型卡直接提供下载、取消、重试和“设为默认”操作。
  - 下载过程显示进度与状态；首个离线模型下载完成后自动设为默认。
  - 下载仍在进行时阻止进入下一步，并提示等待或取消。
  - 至少准备一个模型后才能正常进入第三步；不准备模型时仍可明确选择“跳过引导”。
  - 关闭或跳过引导会取消仍在进行的下载，避免后台残留任务。
- 第三步“选择工作流”：说明实时录音和离线导入的操作入口。
- 支持下一步、上一步、跳过、窗口关闭与 Escape。
- 最后一步主按钮为“开始使用”，完成后直接回到已刷新模型状态的主工作台。
- 视觉沿用现有深色工业界面、蓝色强调色、细边框和紧凑信息层级，并加入 160ms 步骤淡入动画。

### 2. 首次启动状态与配置迁移

- `src/MeetingTransfer.Core/Config/AppOptions.cs`
  - 新增 `UiOptions.OnboardingCompleted`，默认 `false`。
- `src/MeetingTransfer.App/Configuration/SettingsFileService.cs`
  - 兼容旧版没有 `Ui` 节点的配置，并为显式空节点恢复安全默认值。
- `src/MeetingTransfer.App/MainWindow.xaml` / `MainWindow.xaml.cs`
  - 主窗口第一次 `Loaded` 时检查状态并以模态窗口显示引导。
  - 增加单次检查保护，防止同一主窗口重复显示。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 完成或跳过引导后写入 `OnboardingCompleted=true`。
  - 引导内下载完成后重新加载模型配置，并立即刷新主界面的引擎和缺模状态。
  - 保存失败时在状态栏显示错误，但不让 UI 崩溃。
- `appsettings.example.json`
  - 增加默认 `Ui.OnboardingCompleted=false`。

### 3. 文档与测试

- `README.md`
  - 快速开始补充三步首次启动引导和模型设置入口。
- 新增 `tests/MeetingTransfer.Tests/AppOptionsTests.cs`：
  - 新安装默认需要显示引导。
  - 旧配置缺少 `Ui` 节点时仍获得安全默认值。
- 上一任务的 `change.md` 已归档为 `change/change-25.md`。

## 验证

- Release build：成功，0 warning / 0 error。
- 测试：65/65 通过。
- `dotnet format --verify-no-changes`：通过。
- 真实 WPF 首次启动验证：
  - 使用无 `appsettings.json` / `models.json` 的隔离发布目录启动。
  - 应用首先显示 `Welcome to Meeting Transfer`，而不是直接进入主窗口。
  - 首屏布局、中文文案、步骤导航、跳过与下一步按钮显示正常。
  - 辅助功能树正确暴露步骤和按钮名称，默认焦点位于“下一步”。
- 引导内模型下载页的最终前台视觉与点击验收按用户要求暂停；用户明确通知“开始验收”后再执行，不在用户使用电脑期间控制前台。
- 不重复显示验证：
  - 将生成配置中的 `Ui.OnboardingCompleted` 设为 `true` 后重新启动。
  - 窗口列表只出现 `Meeting Transfer` 主窗口，没有再次出现引导。
- 隔离验证目录 `publish/onboarding-test` 已删除，无测试配置残留。
- 最新的“引导内直接下载模型”版本已通过后台验证：Release build 0 warning / 0 error、65/65 测试、格式检查与 XAML XML 解析通过。
- 最终 `publish/win-x64` 已从空目录重建：50 个文件、264,797,016 bytes（252.5 MiB）。
- 最终发布扫描：模型权重 0 个、运行期用户数据 0 个。
- `MeetingTransfer.App.dll` SHA256：`7341D82CCE237502ADCB287243421B6D429F0B3F67F699039400A199F9766798`。

## 执行命令

```powershell
git status --short
Move-Item change.md change/change-25.md
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/onboarding-test
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath publish/onboarding-test -Recurse -Force
Remove-Item -LiteralPath publish/win-x64 -Recurse -Force
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64
Get-FileHash publish/win-x64/MeetingTransfer.App.dll -Algorithm SHA256
```

## 发布要求

- 最终 `publish/win-x64` 必须从空目录重新生成。
- 发布包继续只包含程序与必要运行时，不包含 `.onnx`、模型 `.bin`、`.pt`、`.safetensors` 等模型权重。
- 最终发布目录不得包含首次启动生成的 `appsettings.json`、`models.json`、数据库、录音或导出文件。
