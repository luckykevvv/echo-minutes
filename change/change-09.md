# Change Log

## 2026-07-09 - Add model catalog UI with cross-architecture download & selection (Whisper / SenseVoice / Paraformer / Qwen3-ASR)

### 任务目标
- 用户要求：允许在 Settings 里下载并选择多种不同的模型（不同架构、不同大小），用卡片列表呈现，每个模型有一段文字描述。
- 跨架构一次性上线：Whisper / SenseVoice / Paraformer / Qwen3-ASR 都走同一套 manifest-driven 路径。
- 不破坏已有用户的 `models.json` / `appsettings.json`，向后兼容。

### 已执行指令
```powershell
# 1. 设计 manifest 数据源
mkdir src\MeetingTransfer.Core\Models
# 2. 写 catalog.json (11 个模型)
# 3. 创建 ModelDescriptor / ModelCatalog / ModelDownloader
# 4. 加 ActiveModelId 字段 + 迁移
# 5. 写 ModelCardListViewModel + ModelCardViewModel
# 6. 重做 SettingsWindow.xaml 为 TabControl (Speech models + Tools)
# 7. SherpaOnnxSpeechEngine 加 TryBuildManifestRequest
# 8. SettingsFileService 同步 ActiveModelId
# 9. 加 5 个 ModelCatalogTests
# 10. 写 change.md + publish
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release
powershell -NoProfile -Command "Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Where-Object Id -ne 3256 | Stop-Process -Force"
powershell -NoProfile -Command "Get-ChildItem publish\win-x64\MeetingTransfer.*.dll | Remove-Item -Force"
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
powershell -NoProfile -Command "Start-Process -FilePath '.\publish\win-x64\MeetingTransfer.App.exe'; Start-Sleep -Seconds 5; Get-Process MeetingTransfer.App | Where-Object Id -ne 3256 | Stop-Process -Force"
```

### 候选清单（catalog.json 里 11 个）

| ID | Family | Size | Langs | Notes |
|---|---|---|---|---|
| whisper-tiny.en | Whisper | 75 MB | en | 最快英文 |
| whisper-base | Whisper | 145 MB | 多语 | 小 |
| whisper-small | Whisper | 470 MB | 多语 | 中 |
| **whisper-large-v3-int8** | Whisper | 1.7 GB | 多语 | **change-08 默认** |
| whisper-large-v3-fp32 | Whisper | 3 GB | 多语 | 极致准 |
| sense-voice-small | SenseVoice | 230 MB | zh/en/ja/ko/yue | 超快（10x 实时），带情绪/事件/语言 ID |
| paraformer-large-zh | Paraformer | 1 GB | zh | 纯中文天花板 |
| qwen3-asr-0.6b | Qwen3-ASR | 1.5 GB | zh/en | 阿里 2025，中文 SOTA |
| qwen3-asr-1.7b | Qwen3-ASR | 3.5 GB | zh/en | 阿里 2025，最大 |
| streaming-paraformer-bilingual | Paraformer (streaming) | 240 MB | zh/en | 实时录音（保留）|

### 变更记录
- `src/MeetingTransfer.Core/Models/catalog.json`（新）—— 11 个模型清单，每个有 `id / family / displayName / sizeBytes / languages / executionMode / description / speedNote / accuracyNote / executable / files[] / argumentsTemplate`。
- `src/MeetingTransfer.Core/Models/ModelDescriptor.cs`（新）—— DTO 集合：`ModelCatalogFile`、`ModelDescriptor`、`ModelFileEntry`、`ModelFileExtract`，System.Text.Json 序列化。
- `src/MeetingTransfer.Core/Models/ModelCatalog.cs`（新）—— 服务层：
  - `All` / `FindById(id)` —— 读 catalog
  - `GetModelDirectory` / `GetInstalledFilePath` —— 解析文件路径
  - `IsInstalled` / `InstalledSize` / `DeleteInstalled` —— 状态查询
  - `BuildArguments` —— 把 `{Encoder}` / `{Tokens}` / `{InputWav}` 替换为真实路径
- `src/MeetingTransfer.Core/Models/ModelDownloader.cs`（新）—— 异步下载：
  - HEAD 探测文件大小 → 进度按 totalBytes 计算
  - 16 KB 流式写盘到 `{dest}.part`
  - 完成后 sha256 校验（可选）
  - 原子重命名 `Move(.part → dest)`
  - `IProgress<double>` 报告 0..1 进度
  - CancellationToken 取消支持
- `src/MeetingTransfer.App/Configuration/ModelsFile.cs` —— 加 `ActiveModelId` 字段（`?string`）。
- `src/MeetingTransfer.App/Configuration/RuntimeSettings.cs` —— 加 `Models` 字段。
- `src/MeetingTransfer.App/Configuration/SettingsFileService.cs`：
  - `Load()` 同步 `ActiveModelId` 到 `SherpaOnnxOptions.ActiveModelId`
  - `Save()` 写回 `ActiveModelId` 到 `models.json`
- `src/MeetingTransfer.App/ViewModels/ModelsListViewModel.cs`（新）—— 两层 ViewModel：
  - `ModelCardViewModel`：每张卡片的 5 个状态机（`NotInstalled` / `Downloading` / `Installed` / `Active` / `Failed`），主按钮文案随状态变。
  - `ModelCardListViewModel`：卡片集合 + 当前激活的 `ActiveModelId`，提供 `SetActiveModel` / `ClearActiveModel` / `RefreshAllStates`。
- `src/MeetingTransfer.App/SettingsWindow.xaml`（重做）—— 三段式：
  - 顶部 header
  - 中间 TabControl：
    - **Speech models** tab：左 `ListBox` 卡片列表 + 右详情面板
    - **Tools** tab：保留旧的 ffmpeg / online exe / online args 输入
  - 底部 Save / Cancel
- `src/MeetingTransfer.App/SettingsWindow.xaml.cs` —— wire up card list commands（PrimaryAction / Delete），`SelectionChanged` 同步 `SelectedCard` 给详情面板。
- `src/MeetingTransfer.App/Converters.cs`（新）—— `BoolToVisibilityConverter` + `NullToVisibilityConverter`，XAML 用 `local:Instance` 单例。
- `src/MeetingTransfer.App/ViewModels/RelayCommand.cs` —— 加 `(Func<object?, Task>, Func<object?, bool>?)` 构造重载，支持 `CommandParameter` 传参。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxOptions.cs` —— 加 `ActiveModelId` 字段。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`：
  - 新增 `TryBuildManifestRequest(activeModelId, wavPath, out exe, out args, out error)` —— 读 catalog，按 model id 找到对应 manifest，校验所有文件存在 + executable 存在，构造 CLI 参数。
  - 新增 `RunModelAsync(exe, args, wavPath, sourceId, ct)` —— 通用执行路径，自动走 change-08 的 30s chunked 切片（如果 >30s）。
  - `TranscribeFileAsync` 顶部先尝试 manifest 路径，找不到回落到原 Whisper 路径。
- `src/MeetingTransfer.App/MeetingTransfer.App.csproj` —— `Models/catalog.json` 复制项加 `CopyToPublishDirectory="PreserveNewest"`。
- `tests/MeetingTransfer.Tests/ModelCatalogTests.cs`（新）—— 5 个测试：catalog 加载、IsInstalled 完整 / 不完整、BuildArguments 替换、DeleteInstalled、缺 catalog 容错。

### UI 行为

每张卡片：
- **左上角 Family 徽章**（teal chip）
- **DisplayName + 大小/语言/模式**
- **状态文字 + 进度条**（下载时可见）
- **主操作按钮**（状态机驱动）：
  - `NotInstalled` → "Download · 1.7 GB"
  - `Downloading` → "Cancel"
  - `Installed` → "Use as default"
  - `Active` → "✓ Active"（teal 高亮，不可点）
  - `Failed` → "Retry"
- **Delete 按钮**（已安装后可见）

右侧详情面板显示 description / speedNote / accuracyNote / Size / Languages。

### 设计取舍
- **跨架构一次性上线**（你选的）：所有架构走同一个 manifest-driven 路径
- **数据/服务层在 `MeetingTransfer.Core`**：避免 `Stt.SherpaOnnx` 反向依赖 `App`（编译期发现并修复）
- **向后兼容**：旧 `SherpaOnnxOptions.Whisper*` 字段仍工作；新 `ActiveModelId` 优先
- **catalog.json 是硬编码清单**（11 条），不靠外网拉。第一次打开 Settings 就能看到全部
- **Download 按钮** = 真实下载到 `models/{id}/`，之后**不**再走 legacy 探测

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - 0 errors / 0 warnings
- `dotnet test MeetingTransfer.sln -c Release` 通过：
  - `15` passed（原 10 + 新 5 个 ModelCatalog 测试）
  - `0` failed
- `dotnet publish ... -o publish\win-x64` 成功。
  - 所有 5 个 MeetingTransfer.*.dll 刷新
  - `publish\win-x64\Models\catalog.json` 部署到位
- 启动新 exe 一次后，`models.json` 仍包含 change-08 的所有字段（向后兼容 OK）。

### 已知遗留（不在本轮范围）
- catalog 里所有 URL 都没在 build 时验证（沙箱无法 curl HF）。如果某个 URL 失效，用户点 Download 看到红字错误，把 URL 反馈给我修。
- 5s 重叠文本在 Whisper 上仍会重复（change-08 已知）
- 在线 streaming 模型（`streaming-paraformer-bilingual`）只用于实时录音路径，import 路径不适用
- 11 个模型里 9 个**没有**内置文件，需要用户在 UI 点 Download 才能用。下载状态实时更新到 settings.json
- 旧 `models.example.json` 里的路径指向 `models/sherpa-onnx-whisper/...`，跟新 catalog 标准路径 `models/whisper-large-v3-int8/...` 不一致——下一轮（change-12 候选）改 example

### 后续方向（不在本轮）
- **去重** chunks 边界 5s 重叠里的重复文本（需要 `--whisper-enable-token-timestamps`）
- **多块并行** —— 现在串行 10 块 ~15 分钟；并行可快 3-4x
- **Migrate 老路径** 按钮：把 `models/sherpa-onnx-whisper/*.onnx` 物理移动到 `models/whisper-large-v3-int8/` 之后删除 legacy 目录
- **下载 retry + 断点续传**（`.part` 文件保留 resume offset）
- **下载后 sha256 校验** —— catalog 里所有 `sha256` 字段都还是 `null`，需要批量回填真实值
