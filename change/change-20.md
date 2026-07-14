# Change Log

## 2026-07-14 - 修复进度文本间距并接入 ASR + 说话人识别真实总进度

### 用户现象
- 进度卡片标题显示为“空闲0%”，标题和百分比之间没有合理间距。
- ASR 进入说话人识别阶段时进度归零或切换为不确定动画。
- 说话人识别实际运行很久，但 UI 没有显示 sherpa 的真实百分比。

### 根因
- 进度标题使用 `DockPanel`，左侧文字和右侧百分比没有稳定的列布局。
- whisper.cpp 子流程在 ASR 结束时直接上报 `Complete=100%`，随后 diarization 又上报 `PostProcessing + Indeterminate=0%`，造成进度倒退。
- sherpa diarizer 会在 stderr 输出 `progress 0.19% ... progress 100.00%`，但原实现使用不监听 stderr 的 `RunProcessAsync`，真实进度被丢弃。
- 主窗口把引擎内部百分比直接写入总进度条，没有为抽取、ASR、后处理和 diarization 建立统一区间。

### 变更记录
- `src/MeetingTransfer.Stt/ISpeechEngine.cs`
  - `TranscriptionStage` 新增 `AsrComplete` 和 `Diarizing`。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`
  - ASR 子流程结束改为上报 `AsrComplete`，不再提前冒充整个任务 100%。
  - diarization 改用 `RunProcessAsyncWithStderr`。
  - 新增 `TryParseDiarizationProgress`，解析 sherpa 的真实 `progress X%`。
  - diarization 从 0 到 100 持续回调；结束后才上报整个任务 `Complete`。
  - 未配置 diarizer时也明确上报最终 Complete。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 新增单条总进度映射：
    - 音频抽取/模型准备：0–45%，未知耗时阶段使用 indeterminate。
    - ASR 解码：45–85%。
    - ASR 文本后处理：85–90%。
    - 说话人识别：90–100%。
  - 使用 `Math.Max` 保证异步回调不会让总进度倒退。
  - ASR 开始时从真实区间下界 45% 开始，不再伪跳到 70%。
  - 中文界面显示“正在识别说话人 N%”。
  - 成功导入/导出后保留 100%，不立即重置为 0。
- `src/MeetingTransfer.App/MainWindow.xaml`
  - 标题和百分比改为两列 Grid，百分比左侧增加 12px 间距。
- `tests/MeetingTransfer.Tests/SpeakerDiarizationTests.cs`
  - 新增真实 sherpa stderr 进度格式解析测试。
- 上一不同任务的 `change.md` 已归档为 `change/change-19.md`。

### 已执行指令
```powershell
rg -n "TranscriptionProgress|BeginProgress|SetProgress|EndProgress" src tests
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet run -c Release --project %TEMP%/MeetingTransferDiarProbe/MeetingTransferDiarProbe.csproj
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe
Move-Item change.md change/change-19.md
```

### 验证结果
- build：0 warnings、0 errors。
- tests：51 passed、0 failed。
- 真实 56.9 秒四人样本端到端：
  - `AsrComplete` 后进入 `Diarizing 0%`。
  - sherpa 真实回调连续显示 1%、3%、4% ... 99%、100%。
  - 最后才上报 `Complete`。
- publish 成功，发布版启动正常。
- 实际窗口捕获确认：标题“空闲”和右侧“0%”分列显示，间距正常。

### 最终进度行为
- 已知百分比时显示真实 determinate 进度。
- 音频抽取/模型加载等没有可靠百分比的阶段使用 indeterminate，不伪造精确值。
- ASR 到说话人识别阶段继承总进度，90% 后继续增长，不归零、不倒退。
