# Change Log

## 2026-07-13 - 将说话人数改为每次导入选择，默认 Auto

### 用户要求
- 不要把全局默认固定为两个人。
- 每次推理时由用户自行选择人数，或者使用自动判断。

### 变更记录
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 新增本次导入专用的 `SpeakerCountOptions` 和 `SelectedSpeakerCount`。
  - 可选 Auto、2–8 人，默认 Auto。
  - 选择值只在 `ImportAsync` 创建推理引擎前写入当前内存中的 options，不持久化为全局固定人数。
  - 中英文切换时重建选项文字并保留当前选择。
- `src/MeetingTransfer.App/MainWindow.xaml`
  - 导入按钮上方新增“本次说话人数 / Speakers for this import”暗色 ComboBox。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxOptions.cs`
  - 默认 `DiarizationClusterCount` 恢复为 `-1`（Auto）。
  - Auto threshold 保持较稳妥的 0.9。
- `src/MeetingTransfer.App/Configuration/SettingsFileService.cs`
  - 启动时清除旧版本遗留的固定人数，统一恢复 Auto。
  - 旧 0.5 threshold 自动迁移为 0.9。
- `src/MeetingTransfer.App/SettingsWindow.xaml(.cs)`
  - 移除持久化的 Expected speakers 项，避免 Settings 与每次导入选择相互冲突。
- `models.example.json`
  - 新安装默认改为 `ClusterCount=-1`、`Threshold=0.9`。
- `tests/MeetingTransfer.Tests/SpeakerDiarizationTests.cs`
  - 新增默认 Auto + 0.9 的回归测试。
- 保留上一轮的连续编号修复：原始稀疏 cluster id 仍会显示为连续的 Speaker 1/2/3。
- 上一不同任务的 `change.md` 已归档为 `change/change-18.md`。

### 已执行指令
```powershell
Get-Content src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs
Get-Content src/MeetingTransfer.App/MainWindow.xaml
Get-Content src/MeetingTransfer.App/SettingsWindow.xaml
ConvertFrom-Json models.example.json
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe
Move-Item change.md change/change-18.md
```

### 验证结果
- build：0 errors（保留既有 xUnit1031 warning）。
- tests：48 passed、0 failed。
- publish：成功。
- 发布版启动成功。
- `publish/win-x64/models.json` 已迁移为 `DiarizationClusterCount=-1`、`DiarizationClusterThreshold=0.9`。
- 实际窗口捕获确认：左栏显示“本次说话人数”，默认选中“自动判断”，下拉入口和导入按钮布局正常。

### 使用行为
- Auto：使用 threshold 0.9 自动估算人数，适合不知道人数的情况，但仍可能受录音条件影响。
- 已知人数：在导入前选择 2–8 人，底层使用 `--clustering.num-clusters=N`，该次推理严格按 N 类聚合。
- 下一次启动或新会话默认仍为 Auto，不会沿用上一次固定人数。
