# Change Log

## 2026-07-09 - Modern UI, progress feedback, zh/en toggle, and sherpa output parsing fix

### 任务目标
- UI 更现代，保持工作台形态，不做营销页。
- 增加进度条和状态反馈，让开始录音、导入、转写、导出有用户感知。
- 增加中文 / English 界面切换。
- 修复识别结果明显不正确的问题，优先处理确定的输出解析 bug。

### 已执行指令
```powershell
git status --short
dotnet build MeetingTransfer.sln -c Release
Get-Content -Raw src\MeetingTransfer.App\ViewModels\MainWindowViewModel.cs
Get-Content -Raw src\MeetingTransfer.App\MainWindow.xaml
Get-Content -Raw src\MeetingTransfer.App\App.xaml
Get-Content -Raw src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs
publish\win-x64\models\sherpa-onnx\bin\sherpa-onnx-vad-with-online-asr.exe --help
publish\win-x64\models\sherpa-onnx\bin\sherpa-onnx-vad-with-online-asr.exe --silero-vad-model=... --tokens=... --paraformer-encoder=... --paraformer-decoder=... test_wavs\0.wav
dotnet test MeetingTransfer.sln -c Release
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Start-Process -FilePath .\publish\win-x64\MeetingTransfer.App.exe
Start-Sleep -Seconds 4
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
rg -n "num-threads|blank-penalty" publish\win-x64\models.json models.example.json src\MeetingTransfer.App\Configuration\SettingsFileService.cs
Move-Item -LiteralPath change.md -Destination change\change-05.md
```

### 变更记录
- `src/MeetingTransfer.App\App.xaml`
  - 重做全局颜色、字体、按钮、输入控件样式。
  - 采用浅色工作区 + 深色侧边栏 + teal accent 的工具型视觉。
- `src/MeetingTransfer.App\MainWindow.xaml`
  - 重做三栏工作台布局。
  - 左侧增加状态卡、进度条、当前进度百分比、语言切换按钮。
  - 中间 transcript 列表改成更清晰的时间 / speaker / 文本三列。
  - 右侧 speaker/session 区域重新排版。
- `src/MeetingTransfer.App\ViewModels\MainWindowViewModel.cs`
  - 新增运行时中文 / English 切换：设备、按钮、speaker、session、进度文案随按钮切换。
  - 新增 `IsBusy`、`IsRecording`、`IsProgressIndeterminate`、`OperationProgress`。
  - 开始录音、停止保存、导入音视频、导出文件时更新进度条和状态文本。
  - 导入流程拆成抽取音频、加载模型、转写、保存四个感知阶段。
- `src/MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs`
  - 修复 sherpa `vad-with-online-asr` 输出解析：
    - 现在识别 `vad segment(... ) results: ...`。
    - 不再把 `Elapsed seconds`、`RTF`、`num threads` 等 CLI 日志混入 transcript。
    - JSON `{ "text": "..." }` 输出也兼容 `\n` / `\r\n`。
  - 实时缓存窗口从 8 秒改为 4 秒，降低感知延迟。
  - 对同一 source 的连续相同文本做去重。
  - 系统音频不再每个窗口创建新 speaker，固定为 `Remote` / `remote-1`。
- `models.example.json`
  - 默认在线参数新增 `--num-threads=2 --blank-penalty=1.0`。
  - 默认离线参数新增 `--num-threads=2`。
- `src/MeetingTransfer.App\Configuration\SettingsFileService.cs`
  - 已存在的 `models.json` 若缺少新参数，会在启动时自动迁移。
- `tests/MeetingTransfer.Tests\MeetingTransfer.Tests.csproj`
  - 增加 `MeetingTransfer.Stt.SherpaOnnx` 项目引用。
- `tests/MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs`
  - 新增 VAD 输出解析测试。
  - 新增 JSON text 输出解析测试。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln -c Release` 通过：
  - `5` passed
  - `0` failed
- `dotnet publish ... -o publish\win-x64` 成功。
- 新 exe 启动成功：
  - `C:\Users\C3EZ\Documents\Meeting_Transfer\publish\win-x64\MeetingTransfer.App.exe`
- 启动后 `publish\win-x64\models.json` 已自动迁移：
  - 在线参数包含 `--num-threads=2`
  - 在线参数包含 `--blank-penalty=1.0`
  - 离线参数包含 `--num-threads=2`
- 使用内置 sherpa VAD + online ASR 跑模型自带 `test_wavs\0.wav`：
  - CLI 正常输出 `vad segment(...) results: ...`
  - 新单测确认 parser 只取识别文本，不取 CLI 日志。

### 说明
- 这次修复的是一个明确 bug：之前 VAD CLI 的 `results:` 输出没有被 parser 识别，导致整段命令行日志可能进入 transcript。
- 模型自身仍是 sherpa-onnx bilingual streaming paraformer；它在部分纯中文样例中会混入英文 token，这是模型行为，不是 UI 或 parser 问题。后续若要显著提升中文准确率，需要内置中文专用模型或增加 Whisper final pass。
