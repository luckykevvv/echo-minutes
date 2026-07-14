# Change Log

## 2026-07-12 - Fix Speaker list duplication on import

### 任务目标
- 用户反馈：导入一个音视频后，右侧 Speaker 列表显示 7-8 个 "Speaker 1 / local:false" 重复条目，与 segment 数量一致。
- 根因：`RunWhisperCppAsync`（change-15 改造后）调 `SplitIntoSentences` 把 60s 音频切成 ~16 段，但**每段都用 `NextSpeakerId()` 拿新 id**（循环内 `sentences.Select(s => new TranscriptSegment { SpeakerId = NextSpeakerId() ... })`）。
- 这些 segment 传到 `MainWindowViewModel.AddSegment` → `Document.EnsureSpeaker(segment.SpeakerId, ...)` 按 id 找不到已有 → 每个 segment 创建一个新 Speaker 实体 → UI 上看到 N 段 N 个 Speaker 1。

### 已执行指令
```powershell
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Where-Object Id -ne 3256 | Stop-Process -Force
Get-ChildItem 'publish\win-x64\MeetingTransfer.*.dll' | Remove-Item -Force
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Start-Process -FilePath '.\publish\win-x64\MeetingTransfer.App.exe'; Start-Sleep 5; Stop-Process -Force
```

### 变更记录
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`
  - **`RunWhisperCppAsync` 第 612 行修复**：把 `SpeakerId = NextSpeakerId()` 从 `sentences.Select(s => new TranscriptSegment { ... })` 内提到外层做 `var sharedSpeakerId = NextSpeakerId();`。所有句子共享同一 speaker id → `EnsureSpeaker` dedup → UI 只显示 1 个 Speaker 1。
  - 加注释解释这个 bug 防止回归。
- 注释里其他 `NextSpeakerId()` 调用（171/212/930/966）：
  - **171, 212, 930**：每条路径只产生 1 个 segment，所以每次调 `NextSpeakerId()` 1 次，**不存在重复 speaker bug**。
  - **966（chunked 路径）**：已经在循环外算 `var speakerId = NextSpeakerId();` 然后整段共享（change-16 改的）—— **本来就是对的**。
- `tests/MeetingTransfer.Tests/SpeakerIdAllocationTests.cs`（新建）
  - 3 个测试：
    1. `NextSpeakerId_IsMonotonicallyIncreasingAcrossCalls` —— 验证 helper 自增行为（speaker-1, speaker-2, speaker-3）。
    2. `TranscriptDocument_EnsureSpeaker_DeduplicatesById` —— 同一 id 调两次只返回同一 instance，第二次的 rename 名字丢弃。这是双层防线：即使将来又有 engine 调用忘记共享 id，`EnsureSpeaker` 仍然能 dedup。
    3. `TranscriptDocument_MergeSpeakers_RemovesSourceAndRewiresSegments` —— 现有 `MergeSpeakers` API 行为验证（speakers 合并、segments 重连）。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` → **0 errors / 1 warning**（既有 xUnit1031）。
- `dotnet test MeetingTransfer.sln -c Release` → **43 passed / 0 failed**（原 40 + 新 3 个 speaker 测试）。
- `dotnet publish ... -o publish\win-x64` → 成功。
  - `MeetingTransfer.Stt.SherpaOnnx.dll` 69632 字节 / 00:11
- 新 exe 启动成功。

### UI 实际变化

| 场景 | 之前 | 现在 |
|---|---|---|
| 导入 60s 音频（16 段）| 16 × "Speaker 1 / local:false" | 1 × "Speaker 1 / local:false" |
| Session Summary 文字 | "16 段，16 位说话人" | "16 段，1 位说话人" |
| Speaker 列表滚动条 | 长 | 短（1 项）|
| MergeSpeakerCommand 是否可点 | 是（>1 个 speaker）| 否（CanExecute = false，因为只有 1 个）|

### 设计决策
- **没改 `NextSpeakerId` helper 本身**（仍是 `speaker-N` 自增），因为：(a) 当前 engine 没有说话人分离，所有 import 段都是同一说话人；(b) 简单改函数语义可能影响未来加 diarization；(c) `EnsureSpeaker` dedup 已经能兜底。
- **没碰 `RunModelAsync` / `ChunkedWhisperTranscribeAsync`**：它们各自只产生 1 个 segment 或共享一个 speakerId，不存在此 bug。
- **测试用了反射 + Activator.CreateInstance**：`NextSpeakerId` 是 private instance helper，没法直接调。构造一个最小 SherpaOnnxSpeechEngine 实例只用来调它（不进真实 STT 流程）。

### 已知遗留
- **跨 import 累积**：`MainWindowViewModel.ImportAsync` 没清空 `Document.Speakers` 和 `Document.Segments`，每次 import 新文件老 Speaker 还在。如果用户连续 import 多个文件，右侧 Speaker 列表会显示多个 "Speaker 1"（不同 id 但同名）。**这次没修** —— 用户只要求"修一下重复"，没明确要求"跨文件清空"。要不要在下一轮也修？
- **`SpeakerName = "Speaker 1"` 写死**：所有 import 都用这名字，未来加 diarization 需要从 whisper-cli 输出里提取说话人标签或加 pyannote 类库。
- **`localFalse` UI 标签**：截图里显示 `local:false` 是因为 `segment.SourceKind == AudioSourceKind.Microphone` 为假（import 路径不是 Microphone）。**逻辑对的**，但名字让人困惑（"不是 local"）。可能改成 `remote` 或干脆只在 Microphone 时显示。

### 截图前后对比
**之前**（用户贴图）：
- 列表 7-8 项，全部 "Speaker 1 / local:false"
- 蓝条选中其中一项
- 滚动条表明还有更多

**现在**：
- 列表 1 项 "Speaker 1 / local:false"
- 选中即它
- 滚动条消失