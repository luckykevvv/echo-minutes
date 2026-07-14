# Change Log

## 2026-07-13 - 修复两人录音被过度拆分为 16 个 Speaker

### 用户现象
- 最新导入会话实际只有两个人，但右侧显示 `Speaker 1 / 5 / 7 / 9 / 10 / 11 / 12 / 13 ...`。
- 这不是 UI 重复项；SQLite 中该会话确实保存了 16 个不同 speaker id。

### 根因与真实复现
- 发布版使用 `DiarizationClusterCount=-1` 与 `DiarizationClusterThreshold=0.5` 自动聚类。
- sherpa-onnx 的 cluster threshold 越小越容易拆出更多 cluster；0.5 对麦克风距离、语气和背景变化过于敏感。
- 真实输入：`publish/win-x64/recordings/标准录音 8-20260711122951.wav`，时长 482.712 秒。
- 复跑结果：
  - threshold 0.8：6 speakers。
  - threshold 0.9：5 speakers。
  - 固定 `num-clusters=2`：2 speakers，分别累计 356.7 秒和 21.1 秒。
- 说明仅提高自动阈值仍不能可靠合并；必须允许使用已知人数约束。
- 跳号是第二个独立问题：UI 直接把 sherpa 原始 cluster id 加一，没有重新连续编号。

### 变更记录
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxOptions.cs`
  - 默认 speaker count 从 Auto 改为 2。
  - Auto threshold 默认从 0.5 改为 0.9。
- `src/MeetingTransfer.App/Configuration/SettingsFileService.cs`
  - 旧的 `Auto + 0.5` 配置启动时自动迁移为 `2 speakers + 0.9`。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`
  - 新增原始 cluster id 到连续显示编号的映射。
  - 例如原始 `speaker_04 / speaker_12` 现在显示为 `Speaker 1 / Speaker 2`，不再跳号。
- `src/MeetingTransfer.App/SettingsWindow.xaml(.cs)`
  - Tools 页新增 `Expected speakers`：Auto (experimental)、2（recommended）、3–8。
  - 保存时同步写入 `DiarizationClusterCount`。
- `models.example.json`
  - 新安装默认改为 2 speakers、threshold 0.9。
- `tests/MeetingTransfer.Tests/SpeakerDiarizationTests.cs`
  - 新增稀疏 cluster id 连续重编号测试。
- 上一不同任务的 `change.md` 已归档为 `change/change-17.md`。

### 已执行指令
```powershell
python -c "查询 meeting-transfer.sqlite 最新 session 与 distinct speaker 数量"
sherpa-onnx-offline-speaker-diarization.exe --clustering.cluster-threshold=0.8 ...
sherpa-onnx-offline-speaker-diarization.exe --clustering.cluster-threshold=0.9 ...
sherpa-onnx-offline-speaker-diarization.exe --clustering.num-clusters=2 ...
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe
Move-Item change.md change/change-17.md
```

### 验证结果
- 真实 482.712 秒问题音频固定 2 人复跑：76 turns、2 speakers，原始 id 为 00/01。
- build：0 errors（保留既有 xUnit1031 warning）。
- tests：47 passed、0 failed。
- publish：成功。
- 发布版启动成功并完成现有配置迁移：`DiarizationClusterCount=2`、`DiarizationClusterThreshold=0.9`。

### 使用说明
- 已经生成的 16-speaker 会话不会在内存中自动重算；需要用新版重新导入原音频。
- 默认适合两人对话。如果明确是 3 人或更多，在 Settings → Tools → Expected speakers 中选择准确人数后再导入。
- Auto 保留但标记为 experimental，因为此模型在真实录音上会随声学条件变化而过度聚类。
