# Change Log

## 2026-07-09 - 修复 SherpaOnnx 启动错误提示与 offline 检查时机

### 任务目标
- 修复点击 Start 时错误要求配置 `OfflineRecognizerExecutable` 的问题。
- 实时录音启动只检查 online recognizer 配置。
- 导入音视频文件时才检查 offline recognizer 配置。
- 错误文案改为引导用户在 GUI `Settings` 中配置对应路径。
- 重新编译、测试并发布 Windows exe。

### 已执行指令
```powershell
Get-Content -Raw src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs
Get-Content -Raw src\MeetingTransfer.App\SettingsWindow.xaml.cs
Get-Content -Raw src\MeetingTransfer.App\ViewModels\MainWindowViewModel.cs
Get-Content -Raw change.md
Get-ChildItem change -Force | Select-Object Name
git status --short
Move-Item -LiteralPath change.md -Destination change\change-03.md
dotnet build MeetingTransfer.sln
dotnet test MeetingTransfer.sln --no-build
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-Process -Id 32180 -ErrorAction SilentlyContinue | Select-Object ProcessName,Id,Path | Format-List
Get-Process -Id 32180 -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-Item publish\win-x64\MeetingTransfer.App.exe | Select-Object FullName,Length,LastWriteTime
rg -n "offline recognizer is not configured|online recognizer is not configured|OfflineRecognizerExecutable|OnlineRecognizerExecutable" src\MeetingTransfer.Stt.SherpaOnnx src\MeetingTransfer.App publish\win-x64\models.example.json
Invoke-WebRequest / curl.exe downloads for sherpa-onnx v1.13.4 Windows runtime, bilingual zh/en paraformer model, and silero VAD
tar -xjf third_party\downloads\sherpa-onnx-v1.13.4-win-x64-shared-MD-Release-no-tts.tar.bz2
tar -xjf third_party\downloads\sherpa-onnx-streaming-paraformer-bilingual-zh-en.tar.bz2
dotnet build MeetingTransfer.sln
dotnet test MeetingTransfer.sln --no-build
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-Item publish\win-x64\MeetingTransfer.App.exe | Select-Object FullName,Length,LastWriteTime
publish\win-x64\models\sherpa-onnx\bin\sherpa-onnx.exe --tokens=... --paraformer-encoder=... --paraformer-decoder=... test_wavs\0.wav
```

### 变更记录
- `SherpaOnnxSpeechEngine.InitializeAsync()` 不再检查 `OfflineRecognizerExecutable`。
- 实时录音启动现在检查 `OnlineRecognizerExecutable`，错误文案引导到 GUI `Settings` 的 `Online exe`。
- 导入音频/视频时才检查 `OfflineRecognizerExecutable`，错误文案引导到 GUI `Settings` 的 `Offline exe`。
- `OfflineRecognizerExecutable` 路径不存在时现在给出独立、明确的错误。
- 下载并解压官方 sherpa-onnx Windows runtime：
  - `sherpa-onnx-v1.13.4-win-x64-shared-MD-Release-no-tts`
- 下载并解压官方中英 streaming paraformer 模型：
  - `sherpa-onnx-streaming-paraformer-bilingual-zh-en`
- 下载并内置 `silero_vad.onnx`。
- 发布目录现在包含：
  - `models/sherpa-onnx/bin/sherpa-onnx.exe`
  - `models/sherpa-onnx/bin/sherpa-onnx-vad-with-online-asr.exe`
  - `models/sherpa-onnx/models/streaming-paraformer-bilingual-zh-en/tokens.txt`
  - `encoder.int8.onnx`
  - `decoder.int8.onnx`
  - `models/sherpa-onnx/models/silero_vad.onnx`
- 默认 `models.example.json` 与发布目录 `models.json` 已指向内置文件。
- 参数模板改为 sherpa-onnx CLI 要求的 `--option=value` 格式。
- 相对模型路径现在按 exe 所在目录解析。
- 旧配置若缺失或指向不存在的 runtime/model，会自动迁移到内置路径。
- 实时录音路径增加基础可用转写：按音频源缓存约 8 秒，写临时 wav，并调用内置 sherpa-onnx 生成 transcript segment。
- `third_party/downloads/` 已加入 `.gitignore`，避免下载缓存进入版本控制。

### 验证结果
- `dotnet build MeetingTransfer.sln` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln --no-build` 通过：
  - `3` passed
  - `0` failed
- 第一次发布被正在运行的旧 `MeetingTransfer.App (32180)` 锁住。
- 已关闭旧进程并重新发布成功。
- 新 exe：
  - `C:\Users\C3EZ\Documents\Meeting_Transfer\publish\win-x64\MeetingTransfer.App.exe`
- 已确认发布目录内置 runtime/model 文件存在。
- 已用内置 `sherpa-onnx.exe` + 内置 paraformer int8 模型识别模型自带 `test_wavs/0.wav`，命令成功退出并输出 transcript JSON。
