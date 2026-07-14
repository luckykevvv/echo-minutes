# Change Log

## 2026-07-09 - Fix sherpa-onnx offline recognizer stdout/stderr parsing

### 任务目标
- 用户导入 `Research Presentation.mp4` 测试，转写结果只显示 `Start to create recognizer / Recognizer created in 3.08824 s` 两行 CLI 初始化日志，看不到真正识别出的文本。
- 确认问题是否出在模型侧，如果不是则修复 parser。
- 重新编译、跑测试，并把回归测试补上避免再次退化。

### 已执行指令
```powershell
git status --short
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Content -Raw src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs
Get-Content -Raw tests\MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs
Get-Content -Raw change.md
# 实测：直接调内置 sherpa-onnx.exe，看 stdout / stderr 各有什么
cd publish\win-x64
.\models\sherpa-onnx\bin\sherpa-onnx.exe --tokens=models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\tokens.txt --paraformer-encoder=models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\encoder.int8.onnx --paraformer-decoder=models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\decoder.int8.onnx --num-threads=2 models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\test_wavs\0.wav
# 用 subprocess 单独拿 stdout / stderr，确认识别文本实际写到哪条流
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release
Move-Item -LiteralPath change.md -Destination change\change-06.md
```

### 根因
- `sherpa-onnx.exe`（offline，导入音视频用）和 `sherpa-onnx-vad-with-online-asr.exe`（online，实时录音用）对 stdout / stderr 的使用不一致：
  - **online** 把 `vad segment(...) results: <text>` 写到 stdout，把统计日志写到 stderr。
  - **offline** 把 `Start to create recognizer / Recognizer created in N s` 初始化 banner 写到 stdout，把识别文本 + JSON + 统计日志都写到 stderr。
- `SherpaOnnxSpeechEngine.RunProcessAsync` 的旧逻辑是 `string.IsNullOrWhiteSpace(output) ? error : output`，offline 路径下 `output`（stdout）不是空，所以只返回了初始化 banner，**真正的识别结果整段被丢在 stderr 里**。
- 这就是为什么用户看到 `Start to create recognizer / Recognizer created in 3.08824 s` 而看不到任何中文转写 —— **不是模型问题**，模型本身在 ~3 秒内退出码 0 并正确返回了 `昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期`。
- `change-05` 的修复只覆盖了 online 引擎的 VAD 输出，offline 路径没人测过，所以漏了。

### 变更记录
- `src/MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs`
  - `RunProcessAsync` 现在把 stdout 和 stderr 合并后再返回，确保两个引擎的所有输出（banner、stats、识别文本、JSON）都能被 parser 看到。
  - 退出码非 0 时，错误消息仍然只展示 stderr 原文（避免把 banner 混进错误消息）。
  - 加了 inline 注释说明 sherpa-onnx 两个 CLI 工具对 stream 的不同用法。
- `tests/MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs`
  - 新增 `ExtractsJsonTextWhenInitBannerIsOnStdout`：模拟 offline 引擎真实输出格式（init banner 在 stdout，文本和 JSON 在 stderr），断言 parser 不再返回 banner，能拿到 `昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期`。
  - 新增 `MergesStdoutAndStderrInRunProcessAsync`：用 `cmd.exe /c echo STDOUT_LINE & echo STDERR_LINE 1>&2` 反射调 `RunProcessAsync`，断言两条流都被捕获。

### 验证结果
- 直接调内置 `sherpa-onnx.exe` + `test_wavs\0.wav`：
  - stdout：`Start to create recognizer\nRecognizer created in 2.4 s`
  - stderr：包含识别文本 `昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期` 和对应 JSON。
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln -c Release` 通过：
  - `7` passed（原 5 个 + 新增 2 个回归测试）
  - `0` failed
- 模拟修复后 `ExtractTranscriptText` 的输入（stdout + stderr 合并），返回 `昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期`，与期望一致。

### 说明
- 模型未改动。`streaming-paraformer-bilingual-zh-en` 在纯中文样本中仍可能混入英文 token，这是模型行为，不是本次问题。
- 本次没有重新 publish exe，修复已经过 `dotnet build` 验证。要拿到修复后的 exe 需要再跑一次 `dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64`。