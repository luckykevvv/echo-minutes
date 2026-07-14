# Change Log

## 2026-07-15 - 发布包移除全部模型并改为设置页按需下载

### 用户要求
- 发布包不包含任何模型。
- 用户安装后统一在“设置”中自行下载所需模型。

### 原有耦合与风险
- 发布项目仍复制以下模型权重：
  - `ggml-large-v3-turbo.bin`
  - realtime Paraformer encoder / decoder / tokens / Silero VAD
  - pyannote segmentation 与 3D-Speaker ERes2Net
- offline 模型已有设置页下载卡，但 realtime 下载后的标准目录没有同步到实际运行配置。
- speaker diarization 没有设置页下载项，只能依赖随包模型。
- pyannote 官方资源只提供 `tar.bz2`，原有 `extract` catalog 字段尚未实现。
- 首次启动会写入随包模型路径和默认 active model；移除权重后会形成无效配置。

### 实现变更
- `src/MeetingTransfer.App/MeetingTransfer.App.csproj`
  - 删除所有 `.onnx` / `.bin` 模型复制项。
  - 发布包仅保留 FFmpeg/ffprobe、whisper.cpp、sherpa-onnx、ONNX Runtime 等执行文件和 DLL。
- `src/MeetingTransfer.Core/Models/catalog.json`
  - realtime Paraformer 四个文件补齐基于当前已验证文件的 SHA256。
  - 新增 `speaker-diarization` resource 卡：
    - pyannote segmentation 官方 tar.bz2。
    - 3D-Speaker ERes2Net 官方直链。
    - 两个最终解出文件均固定 SHA256。
  - 目录共 8 项：6 个 offline、1 个 realtime、1 个 diarization resource。
- `src/MeetingTransfer.Core/Models/ModelDownloader.cs`
  - 实现 catalog `extract` 字段。
  - 只支持明确声明的 `tar.bz2`。
  - 使用 forward-only reader，只提取 catalog 指定的单个精确成员，不展开任意目录。
  - 解包后再校验最终模型文件 SHA256，然后原子移动到安装目录。
  - 缺少成员、格式不支持、路径不安全或哈希不符均拒绝安装并清理临时文件。
- `src/MeetingTransfer.Core/MeetingTransfer.Core.csproj`
  - 新增 `SharpCompress 1.0.0`，用于安全读取官方 tar.bz2。
- `src/MeetingTransfer.App/Configuration/SettingsFileService.cs`
  - realtime Paraformer 下载完成后自动映射 encoder、decoder、tokens、Silero VAD 标准目录。
  - Speaker diarization 下载完成后自动映射 pyannote 与 ERes2Net 路径。
  - 不再在无模型时自动设置 `whisper-large-v3-turbo`。
  - 已选择但不存在的 offline 模型会清除 active 状态。
- `src/MeetingTransfer.App/ViewModels/ModelsListViewModel.cs`
  - online 与 resource 卡可下载/删除，但不能设置为 offline 默认模型。
  - 安装后分别显示“Installed for live recording”与“Installed for speaker labels”。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 未安装 realtime Paraformer 时禁用“开始”。
  - 未安装或未选择 offline 模型时禁用“导入”。
  - 状态栏列出尚需从设置下载的 offline、realtime、diarization 项。
  - 未安装 diarization 时仍可用已选 offline 模型导入，但不会自动分人。
- `models.example.json`
  - 所有模型路径和 `ActiveModelId` 默认改为 `null`。
  - 仅保留随程序提供的 runtime executable 路径与参数模板。
- `README.md`
  - 首次运行流程改为先进入设置下载模型。
  - 明确 realtime、offline、speaker diarization 三类用途和安装方式。
  - 明确发布包约 253 MiB、模型权重为 0。
- `THIRD_PARTY_NOTICES.md`
  - 明确发布包只含运行时/库，不含模型权重。
  - 增加 SharpCompress 说明，并区分运行时再分发条款与用户下载模型条款。
- 上一不同任务的 `change.md` 已归档为 `change/change-23.md`。

### 新增验证
- `ModelDownloaderTests.DownloadAsync_ExtractsOnlyConfiguredTarBz2Member`
  - 在内存中创建 tar.bz2。
  - 经真实 downloader 下载、单成员解包和最终 SHA256 校验。
  - 验证安装目录只得到指定模型内容。
- 使用无模型发布版首次启动：
  - `ActiveModelId` 为空。
  - realtime 与 diarization 模型路径为空。
  - WPF WM_CLOSE 退出码 0，Application 事件日志无新增 .NET Runtime 1026。
- 使用发布目录临时占位安装结构验证：
  - realtime 四条路径全部自动映射。
  - diarization 两条路径全部自动映射。
  - 验证后已清理占位文件、配置和数据。

### 已执行的主要指令
```powershell
git status --short
git ls-files -v | Select-String '^S'
Move-Item change.md change/change-23.md
rg -n "ActiveModelId|OnlineRecognizer|SpeakerDiarization|Pyannote|Embedding" src tests
Get-FileHash third_party/sherpa-onnx/models/... -Algorithm SHA256
curl.exe https://api.github.com/repos/k2-fsa/sherpa-onnx/releases/tags/speaker-segmentation-models
curl.exe https://api.github.com/repos/k2-fsa/sherpa-onnx/releases/tags/speaker-recongition-models
dotnet restore MeetingTransfer.sln --disable-parallel -p:NuGetAudit=false
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 --disable-parallel -p:NuGetAudit=false
dotnet format MeetingTransfer.sln whitespace --no-restore
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build -p:NuGetAudit=false
dotnet list MeetingTransfer.sln package --vulnerable --include-transitive
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64
Start-Process publish/win-x64/MeetingTransfer.App.exe -WindowStyle Minimized -PassThru
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=...}
```

### 最终结果
- Build：0 warnings、0 errors。
- Tests：63 passed、0 failed。
- Format：`--verify-no-changes` exit 0。
- NuGet audit：六个项目均无已知易受攻击包。
- Publish：50 files，252.5 MiB。
- 发布包模型权重：0 个 `.onnx` / `.bin` / `.pt` / `.pth` / `.safetensors` / `.ckpt` / `.ggml`。
- Catalog：8 个可下载模型/资源项。
- 无模型首次启动：配置保持空模型状态，应用退出码 0，事件日志错误 0。
- 下载目录映射模拟：realtime 4/4、diarization 2/2 路径正确。
- 最终发布目录已清理运行时生成的 `appsettings.json`、`models.json`、`data/`、`recordings/`、`exports/` 与测试占位模型。
