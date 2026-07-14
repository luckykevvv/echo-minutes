# Change Log

## 2026-07-12 - 接通离线说话人分离并完成遗留问题 review

### 任务目标
- Review 当前仓库的待修复 bug 和悬而未决问题。
- 修复“UI 有 Speaker 概念，但导入音视频实际上没有 speaker diarization”的核心缺口。
- 保持默认发布包开箱即用，不要求用户手动下载或配置分人模型。

### 根因
- `SherpaOnnxOptions` 和 `DiarSegment` 已有 diarization 脚手架，发布目录也残留了可执行文件与模型，但 `TranscribeFileAsync` 从未调用 diarizer。
- Whisper 转写后的每条 segment 被统一写死为 `Speaker 1`；此前修复的只是重复 Speaker 列表，并非真正分人。
- 连续导入文件复用同一个 `TranscriptDocument`，会让不同文件的 `speaker-1` 错误合并到同一会话。

### 变更记录
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`
  - 离线 STT 完成后调用 `sherpa-onnx-offline-speaker-diarization.exe`。
  - 解析 `start -- end speaker_XX` 输出。
  - 以时间重叠将转写切分并映射为 `Speaker 1 / Speaker 2 / ...`。
  - 忽略小于 150ms 的边界交叠，避免产生单字符 speaker turn。
  - 支持自动聚类 threshold，或配置固定 speaker count。
- `src/MeetingTransfer.App/Configuration/SettingsFileService.cs`
  - 自动迁移 diarizer、pyannote segmentation、3D-Speaker embedding 三个默认路径。
- `third_party/sherpa-onnx/models/speaker-diarization/`
  - 纳入 `model.int8.onnx` 和 `eres2net.onnx` 等发布所需文件（约 46.8 MB）。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 每次成功导入创建新的 `TranscriptDocument`，避免跨文件 session/speaker id 冲突。
- `models.example.json`、`README.md`
  - 增加开箱即用的 diarization 默认配置与行为说明。
- `tests/MeetingTransfer.Tests/SpeakerDiarizationTests.cs`
  - 覆盖 CLI 输出解析、最大重叠分配、跨多人 turn 切分。
- `src/MeetingTransfer.Stt.SherpaOnnx/MeetingTransfer.Stt.SherpaOnnx.csproj`
  - 向测试程序集开放内部测试边界。
- 上一份不同任务的 `change.md` 已归档为 `change/change-15.md`。

### 已执行指令
```powershell
git status --short
git ls-files -v | Select-String '^S'
rg --files change
rg -n -i "diari|speaker|embedding|cluster|TODO|FIXME" src tests README.md
Invoke-WebRequest https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/0-four-speakers-zh.wav
sherpa-onnx-offline-speaker-diarization.exe --clustering.num-clusters=4 --segmentation.pyannote-model=... --embedding.model=... 0-four-speakers-zh.wav
Copy-Item publish/win-x64/models/sherpa-onnx/models/speaker-diarization/... third_party/sherpa-onnx/models/speaker-diarization/...
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe
dotnet run -c Release  # 临时端到端 probe：Whisper + diarization 官方四人样本
Move-Item change.md change/change-15.md
```

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release`：通过，0 errors。
- `dotnet test MeetingTransfer.sln -c Release --no-build`：46 passed，0 failed。
- `dotnet publish ... -o publish/win-x64`：成功。
- 发布版启动成功；`models.json` 自动写入 diarization 三个路径、`ClusterCount=-1`、`Threshold=0.5`。
- 官方 56.9 秒四人中文样本：真实端到端完成 Whisper 转写 + diarization，最终识别出 `speaker-1`、`speaker-2`、`speaker-3`、`speaker-4`。
- diarizer 单独运行耗时约 12 秒，输出 10 个 speaker turns。

### Review 后仍悬而未决
- 实时录音目前只按音频源区分 `Me` / `Remote`，尚未做实时声纹聚类；本轮实现的是准确性更高的离线导入分人。
- Whisper.cpp 输出的句级时间戳不是逐词时间戳；多人快速抢话或重叠说话时，文字在 speaker turn 边界的切分是按持续时间近似分配，speaker 标签可靠性高于边界文字精度。
- sherpa-onnx diarization 当前为 CPU provider；约 0.21 RTF，但长会议仍会增加后处理等待时间。
- legacy sherpa Whisper 的 30 秒切块路径仍有 5 秒 overlap 重复风险；默认 whisper.cpp Vulkan 路径不走该 legacy 分支。
- 模型下载仍缺 SHA256 校验、断点续传和 retry；catalog URL 也未做持续可用性检测。
- 现有测试仍有一条既有 xUnit1031 warning（同步等待异步调用），不影响功能但应后续清理。
