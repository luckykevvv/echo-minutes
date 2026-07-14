# Change Log — 2026-07-15 — 新手引导第二步崩溃修复

## 问题

- 用户报告首次启动引导进入第二步后“卡死无响应”。
- 全程未控制用户前台界面；通过 Windows Application 日志和源代码定位。

## 根因

- 2026-07-15 02:29:51 的 `.NET Runtime` 事件 1026 记录了真实异常：
  - `System.Windows.Markup.XamlParseException`
  - WPF 尝试对 `ModelCardViewModel.SizeDisplay` 只读属性建立 `TwoWay` / `OneWayToSource` 绑定。
- 第二步模型卡片使用了内联 `Run.Text="{Binding SizeDisplay}"`。
- `Run.Text` 在该渲染路径下没有安全地推断为 OneWay；进入第二步、模板实际测量时触发异常并终止进程，因此用户看到窗口失去响应/消失。
- 这不是模型下载、网络请求或模型目录扫描死锁。

## 修复

- `src/MeetingTransfer.App/OnboardingWindow.xaml`
  - `SizeDisplay` 改为显式 `Mode=OneWay`。
  - 同一行的 `LanguagesDisplay` 同样改为显式 `Mode=OneWay`，消除下一处相同风险。
- `tests/MeetingTransfer.Tests/XamlBindingRegressionTests.cs`
  - 新增源 XAML 回归检查。
  - 扫描应用所有顶层 XAML 中的内联 `Run` 绑定。
  - 任何未显式声明 `Mode=OneWay` 的 `Run` 绑定都会让测试失败。
- 新增独立的 `tests/MeetingTransfer.App.SmokeTests` WPF 测试项目：
  - 在后台 STA 线程中加载真实 `App.xaml` 资源和 `OnboardingWindow`。
  - 不调用 `Show()`，不会显示或激活任何前台窗口。
  - 强制切换到第二步，并在窗口逻辑树中逐张实例化全部模型卡片模板。
  - 对每张卡片执行 Measure / Arrange / UpdateLayout，真实激活 `Run.Text` 绑定与模板渲染路径。
  - 通过真实“下一步”按钮进入第二步，并验证 6 个离线模型、1 个实时模型、1 个功能资源组成的三组顺序。
  - 在没有安装模型时再次点击“下一步”，验证仍停留在第二步并显示“至少一个模型”的提示。
  - 测试设有 20 秒超时，可同时捕获 XAML 异常与真正的布局死锁。
- 上一任务记录已归档为 `change/change-26.md`。

## 验证

- `OnboardingWindow.xaml` XML 解析成功。
- 当前所有内联 `Run` 绑定均显式使用 `Mode=OneWay`。
- Release build：成功，0 warning / 0 error。
- 测试：68/68 通过，其中 67 项核心/回归测试，1 项真实 WPF 后台渲染冒烟测试。
- `dotnet format --verify-no-changes`：通过。
- 按用户要求，修复后的最终前台点击验收留到用户明确通知“开始验收”后执行。
- 原 `publish/win-x64` 被 2026-07-15 02:29:51 启动的崩溃残留进程 PID 36360 锁定；该进程由更高权限启动，当前会话执行 `Stop-Process -Force` 返回 `Access is denied`，因此未强行干扰用户当前桌面。
- 锁定旧目录已改名为 `publish/win-x64-locked`，释放了正式目录名称；修复版由 `win-x64-fixed` 复制到新的正式 `publish/win-x64`。
- 用户随后手动启动 `win-x64-fixed`，该目录按设计生成了 `appsettings.json` 与 `models.json`；未结束或操作该前台进程。
- 正式 `publish/win-x64` 中误复制的两份运行期配置已单独清理，不影响用户正在使用的 `win-x64-fixed`。
- 正式发布目录验证：50 个文件、252.5 MiB、模型权重 0 个、运行期用户数据 0 个；全部 50 个发布文件与修复源目录逐文件 SHA256 一致。
- 初次切换到正式目录时的 `MeetingTransfer.App.dll` SHA256：`C208A13E6BB36305BE119AC3957A8FFD7991D14AA3A2005118433273D4E840EB`；后续因加入 WPF 冒烟测试所需的可测试构造参数而再次构建，最终摘要见下方。
- 用户手动启动修复版后的被动检查：PID 30844 `Responding=True`，主窗口标题为 `Meeting Transfer`；从 02:44 启动时间至检查时，Application 日志没有新的 MeetingTransfer `.NET Runtime`、Application Error、WER 或 Application Hang 事件。
- 增加 WPF 冒烟测试后已再次从空目录发布正式 `publish/win-x64`：50 个文件、252.5 MiB、模型权重 0 个、运行期用户数据 0 个。
- 最新正式 `publish/win-x64/MeetingTransfer.App.dll` SHA256：`6F4729BDCE98875985A253AE9090E94482B8B56BB38EFAA214EAE36B8EB28068`。
- 加强导航/分组断言后再次运行完整测试：67/67 通过，格式检查通过。
- `ModelDownloaderTests` 新增取消下载清理覆盖：模拟写入第一块数据后取消，确认最终模型文件和 `.part` 临时文件均不存在；加入后完整测试为 68/68 通过。
- 用户手动运行产生的 `publish/win-x64-fixed` 当前包含 53 个文件和本地配置；未删除用户运行期数据。崩溃旧进程占用的 `publish/win-x64-locked` 仅剩 7 个被锁定运行文件；两者都不属于正式交付目录。

## 执行命令

```powershell
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-4)}
Move-Item change.md change/change-26.md
rg -n 'Run Text=' src/MeetingTransfer.App -g '*.xaml'
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet sln MeetingTransfer.sln add tests/MeetingTransfer.App.SmokeTests/MeetingTransfer.App.SmokeTests.csproj
dotnet test tests/MeetingTransfer.App.SmokeTests/MeetingTransfer.App.SmokeTests.csproj -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64-fixed
Get-FileHash publish/win-x64-fixed/MeetingTransfer.App.dll -Algorithm SHA256
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 -p:NuGetAudit=true
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64
```

## 后续目标

- 用户最终通知后执行完整前台验收，包括首次启动、第二步模型列表、下载/取消/进度、默认离线模型和再次启动不重复显示。
- 图像生成渠道恢复后，生成多组扁平与二次元风格应用图标供用户选择。
