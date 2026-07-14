# Change Log

## 2026-07-14 - 根据当前模型显示真实引擎，并在模型卡标注引擎

### 用户要求
- 主窗口的引擎显示应根据当前实际使用的模型变化，不应固定显示 SherpaOnnx。
- 每张模型卡片需要明确显示该模型使用的执行引擎。

### 变更记录
- `src/MeetingTransfer.Core/Models/ModelDescriptor.cs`
  - 新增可序列化 `engine` 字段。
  - 新增 `EngineDisplay`：新 catalog 优先使用明确字段；旧 catalog 可根据 executable 路径回退判断 `whisper.cpp` / `sherpa-onnx`。
- `src/MeetingTransfer.Core/Models/catalog.json`
  - 4 个 Whisper GGML 模型标记 `engine: whisper.cpp`。
  - SenseVoice、Paraformer、Qwen3-ASR、实时 Paraformer 标记 `engine: sherpa-onnx`。
  - 保留独立的 `backend` 字段：引擎与 GPU/CPU 不再混为同一概念。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - `EngineStatus` 不再读取固定的 `AppOptions.Speech.Engine`。
  - 根据 `ActiveModelId` 查询 catalog，显示 `导入引擎: engine · backend`。
  - Settings 中切换默认模型并关闭窗口后会重新加载 options，并刷新主窗口引擎文字。
- `src/MeetingTransfer.App/ViewModels/ModelsListViewModel.cs`
  - 模型卡新增 `Engine` 与 `EngineLabel`。
- `src/MeetingTransfer.App/SettingsWindow.xaml`
  - 模型列表卡片和右侧详情均新增 `Engine · whisper.cpp` / `Engine · sherpa-onnx` badge。
  - GPU/CPU badge 继续独立显示。
- `tests/MeetingTransfer.Tests/ModelCatalogTests.cs`
  - 新增明确 engine 字段与旧 executable 回退规则测试。
- 上一不同任务的 `change.md` 已归档为 `change/change-20.md`。

### 当前模型映射
- whisper-tiny.en、whisper-base、whisper-small、whisper-large-v3-turbo：`whisper.cpp · GPU`。
- sense-voice-small、paraformer-large-zh、qwen3-asr-0.6b、qwen3-asr-1.7b：`sherpa-onnx · CPU`。
- streaming-paraformer-bilingual：`sherpa-onnx · CPU`。

### 已执行指令
```powershell
ConvertFrom-Json src/MeetingTransfer.Core/Models/catalog.json
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe
Move-Item change.md change/change-20.md
```

### 验证结果
- catalog JSON 解析成功，9 个模型均有 engine 和 backend。
- build：0 warnings、0 errors。
- tests：52 passed、0 failed。
- publish 成功，发布版启动正常。
- 实际窗口捕获确认：
  - 当前激活 `whisper-large-v3-turbo` 时，主窗口显示 `导入引擎: whisper.cpp · GPU`。
  - Whisper 模型卡显示 `Engine · whisper.cpp` 与 GPU。
  - Sherpa 模型卡显示 `Engine · sherpa-onnx` 与 CPU。
