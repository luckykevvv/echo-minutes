# Change Log

## 2026-07-14 - 修复 realtime 长时间无输入产生大量伪错误转写

### 用户现象
- realtime 长时间检测不到输入后，转写区出现一大串看似报错的内容。

### 根因与真实复现
- realtime 每累计 4 秒就启动一次 `sherpa-onnx-vad-with-online-asr.exe`，不检查音频是否为静音。
- 真实 4 秒 16kHz 单声道静音测试中，sherpa 退出码为 0、stdout 为空，但 stderr 会输出几十行配置和统计信息，例如：
  - `VadModelConfig(...)`
  - `OnlineRecognizerConfig(...)`
  - `Creating recognizer / Recognizer created`
  - `Elapsed seconds / Real time factor`
- `ExtractTranscriptText` 在没有 VAD result、JSON text 或 `text:` 行时，旧逻辑直接返回整个 stderr。因此这些诊断日志每 4 秒被当成一条转写添加到 UI，并非 sherpa 真正连续报错。
- `ChunkReady` 使用 async event handler，异常未捕获；系统音频与麦克风也可能并发进入同一个 engine/pipeline。

### 变更记录
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxOptions.cs`
  - 新增 `RealtimeSilenceThresholdDb`，默认 `-50 dBFS`。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`
  - 4 秒 realtime buffer 达到阈值后先计算 PCM16 RMS。
  - 低于 -50 dBFS 的静音窗口直接丢弃，不写临时 WAV、不启动 sherpa 进程。
  - `ExtractTranscriptText` 不再把无法解析的整段输出原样返回。
  - 新增 sherpa 诊断行过滤，只保留真正有意义的 plain-text 识别结果。
  - `SourceAudioBuffer` 新增静音判断、Discard 与统一 Reset。
- `src/MeetingTransfer.Stt/RealtimeTranscriptionPipeline.cs`
  - 新增 `SemaphoreSlim`，串行化 realtime chunk 推理，避免多个音源并发访问 engine buffer/dictionary 和同时拉起识别进程。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 捕获 realtime `OperationCanceledException`，Stop 时不显示错误。
  - 其他 realtime 异常转为状态栏提示，并限制最多每 10 秒报告一次，防止错误刷屏。
- `tests/MeetingTransfer.Tests/SherpaOnnxOutputParserTests.cs`
  - 新增真实静音 sherpa 诊断输出返回空文本测试。
  - 新增 RMS 静音/语音电平区分测试。
  - 新增连续 6 个 4 秒静音窗口不启动缺失 recognizer 的回归测试。
  - 将旧同步等待测试改成 async/await，消除 xUnit1031。
- 上一不同任务的 `change.md` 已归档为 `change/change-21.md`。

### 已执行指令
```powershell
ffmpeg -f lavfi -i anullsrc=r=16000:cl=mono -t 4 -c:a pcm_s16le meeting-transfer-4s-silence.wav
sherpa-onnx-vad-with-online-asr.exe --silero-vad-model=... --tokens=... --paraformer-encoder=... --paraformer-decoder=... meeting-transfer-4s-silence.wav
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe
Move-Item change.md change/change-21.md
```

### 验证结果
- 真实 4 秒静音 CLI：确认退出码 0、无转写、只有诊断 stderr。
- parser：相同诊断文本现在返回空字符串。
- 静音门控：数字静音判定为 silent，约 -24 dBFS 测试语音判定为非静音。
- 连续 6 个 4 秒静音窗口：全部返回空 segments；recognizer 路径故意设为不存在也没有异常，证明未启动外部进程。
- build：0 warnings、0 errors。
- tests：55 passed、0 failed。
- publish 成功，发布版启动正常。

### 最终行为
- 长期无输入：每 4 秒清空静音 buffer，保持等待，不启动 sherpa、不产生 transcript、不刷诊断日志。
- 恢复说话：正常音量超过阈值后继续实时识别。
- 多音源 chunk：按顺序处理，不再并发破坏 engine 状态。
- Stop：在途任务取消属于正常流程，不显示错误。
