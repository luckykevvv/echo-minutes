# Change Log

## 2026-07-13 - WPF Codex 风格暗色界面重构

### 任务目标
- 保留 WPF、MVVM、音频和转写后端，不迁移 Electron/WebView。
- 参考 Codex/Hermes 的工具型桌面布局，把主窗口和设置窗口统一重构为仅暗色模式。
- 真实启动检查窗口，而不只验证 XAML 能否编译。

### 设计方向
- 克制的工具型深色工作台：`#0D0D0F` 主背景、低对比分层表面、hairline 边框、单一蓝色强调色。
- 集成无边框标题栏，减少传统 Windows/WPF chrome 的割裂感。
- 保留三栏信息架构，但重新组织为左侧控制轨、中央 transcript timeline、右侧 speaker inspector。
- 避免大面积彩色卡片；GPU/CPU 等状态只用小型语义 badge。

### 变更记录
- `src/MeetingTransfer.App/App.xaml`
  - 建立全局暗色设计 token：背景、表面、文字、边框、accent、成功和警告色。
  - 重写 Button、TextBox、ComboBox、ComboBoxItem、ListBox、ProgressBar、Separator、ScrollBar 模板。
  - ComboBox 和滚动条不再泄漏系统白色默认主题。
- `src/MeetingTransfer.App/MainWindow.xaml`
  - 新增集成标题栏和窗口控制按钮。
  - 左栏改为设备卡片、主录音操作和底部进度状态。
  - 中央改为轻量 transcript timeline，时间、speaker 和正文层级更清晰。
  - 右栏改为 speaker inspector；移除 `local:false` 调试式文案。
  - 最小窗口宽度从 1180 调整为 1040，三栏宽度更紧凑。
- `src/MeetingTransfer.App/MainWindow.xaml.cs`
  - 新增最小化、最大化/恢复、关闭事件。
- `src/MeetingTransfer.App/SettingsWindow.xaml`
  - 重写为同一暗色设计系统，包含自定义 Tab、模型列表、详情 inspector、GPU/CPU badge 和 Tools 表单。
  - 保留原有模型下载、激活、删除及设置保存逻辑和控件名称。
- `src/MeetingTransfer.App/SettingsWindow.xaml.cs`
  - 新增自定义标题栏窗口控制事件。
- 上一任务的 `change.md` 已归档为 `change/change-16.md`。

### 已执行指令
```powershell
git status --short
git ls-files -v | Select-String '^S'
Get-Content src/MeetingTransfer.App/App.xaml
Get-Content src/MeetingTransfer.App/MainWindow.xaml
Get-Content src/MeetingTransfer.App/SettingsWindow.xaml
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process src/MeetingTransfer.App/bin/Release/net8.0-windows/MeetingTransfer.App.exe
Move-Item change.md change/change-16.md
```

### 真实界面验证
- 使用 Windows 窗口捕获检查主窗口和 Settings 窗口。
- 首轮检查发现设备 ComboBox 仍为系统白底、窗口 glyph 不准确，已改为自定义暗色模板和稳定符号。
- 第二轮检查确认：主窗口全暗色、标题栏按钮正常、设备下拉框为暗色。
- Settings 检查确认：模型列表、详情面板、badge、底部操作和 Tools tab 均保持暗色；随后修复白色系统滚动条。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release`：0 warnings，0 errors。
- `dotnet test MeetingTransfer.sln -c Release --no-build`：46 passed，0 failed。
- Release publish 成功：`publish/win-x64/MeetingTransfer.App.exe` 与 DLL 时间戳更新至 2026-07-13 19:59。
- 主窗口与 Settings 均成功启动和渲染，无 XAML parse crash。

### 保留说明
- 本轮没有引入第三方 UI 包，使用纯 WPF ControlTemplate，避免增加依赖和破坏发布流程。
- 中英文运行时切换、进度反馈、设备选择、导入、导出、speaker rename/merge、模型下载/激活逻辑均保留。
- 当前仍只有暗色资源，不提供亮色主题或主题切换入口，符合用户要求。
