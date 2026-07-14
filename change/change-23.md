# Change Log

## 2026-07-15 - Meeting Transfer 1.0.0 发布前最终审查、漏洞修复与 README

### 审查结论
- Release 基线原为 55/55 测试通过，但发现以下正式版阻断问题：
  - `SQLitePCLRaw.lib.e_sqlite3 2.1.10` 命中 High 漏洞 `GHSA-2m69-gcr7-jv3q`。
  - 录音中直接关闭窗口不会显式停止采集、封口 WAV 和保存会话。
  - realtime chunk 与 `FinalizeSessionAsync` 未共用串行锁，停止时可能并发访问并释放识别引擎。
  - 启动中途失败没有完整回收已创建的录音、采集和识别资源。
  - FFmpeg 抽取被取消时不会杀掉进程树，可能留下后台进程与半成品 WAV。
  - 下载器只按 Content-Length 跳过已有模型；同尺寸损坏文件不会重新校验 SHA256。
  - 模型文件名和下载 URL 缺少路径逃逸 / 非 HTTPS 防护。
  - 设置页允许把 realtime Paraformer 设为离线默认模型，也允许对无法删除的内置模型显示删除操作。
  - 两个 Qwen3-ASR 模型 URL 实测返回 HTTP 401，属于不可安装的目录残留。
  - 发布项目会复制整个第三方目录，包含 server、benchmark、quantize、头文件、静态库和测试 WAV，源资产约 3.07 GB。
  - README 仅覆盖最简开发启动，没有正式版安装、工作流、数据路径、模型边界、隐私、限制和许可证说明。

### 代码与安全修复
- `src/MeetingTransfer.Core/MeetingTransfer.Core.csproj`
  - 升级到 `Microsoft.Data.Sqlite 10.0.9`。
  - 显式使用已修复的 `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`；最终 NuGet 漏洞审计六个项目均为 0。
- `src/MeetingTransfer.Stt/RealtimeTranscriptionPipeline.cs`
  - `FinalizeAsync` 与 realtime chunk 共用 `_processGate`，确保最后一块处理完成后才最终化。
- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - Start / Stop / Import / Export / Settings 根据 busy / recording / shutdown 状态互斥。
  - 启动失败时执行资源清理；Stop 使用独立串行门。
  - 导入支持关闭取消；空转写导入也会清空旧界面集合。
  - Export 捕获 I/O 异常并为非法空标题提供安全文件名。
  - 新增 `ShutdownAsync`，关闭时取消导入、停止采集、封口录音、最终化识别并保存 SQLite。
- `src/MeetingTransfer.App/MainWindow.xaml.cs`
  - 首次关闭先取消本次 Closing，等待 `ShutdownAsync`，再通过 Dispatcher 投递第二次关闭。
  - 修复真实事件日志中的 WPF `InvalidOperationException: 在窗口关闭期间...无法调用 Close`，最终退出码为 0。
- `src/MeetingTransfer.Core/Import/MediaImportService.cs`
  - FFmpeg 改用 `ArgumentList`，输出名加入毫秒与 GUID。
  - 取消 / 失败时终止进程树、等待退出并删除半成品 WAV。
- `src/MeetingTransfer.Stt.SherpaOnnx/SherpaOnnxSpeechEngine.cs`
  - legacy chunk FFmpeg 同样使用 `ArgumentList`，取消时终止进程树并清理分块文件。
- `src/MeetingTransfer.Core/Models/ModelDownloader.cs`
  - 已有文件在目录提供 SHA256 时重新校验，错误文件强制重下。
  - 下载只接受绝对 HTTPS URL；瞬时失败最多重试 3 次；进度重试时正确回退。
- `src/MeetingTransfer.Core/Models/ModelCatalog.cs`
  - 拒绝绝对路径、`..` 和带目录的模型文件名；模型 id 不能逃逸 `models/`。
  - 只有目录管理的下载模型可删除，内置 legacy / bundled 资产不可误删。
- `src/MeetingTransfer.App/ViewModels/ModelsListViewModel.cs`
  - online 模型不能设为离线导入默认模型，也不会被自动激活。
- `src/MeetingTransfer.Core/Models/catalog.json`
  - 删除上游 URL 返回 401 的 `qwen3-asr-0.6b` 与 `qwen3-asr-1.7b`；v1 保留 7 个可用模型卡。

### 发布清理与文档
- `src/MeetingTransfer.App/MeetingTransfer.App.csproj`
  - 第三方资产改为白名单：FFmpeg/ffprobe 与 DLL、whisper CLI 与运行时 DLL/默认模型、3 个 sherpa CLI、ONNX Runtime、实时模型和 diarization 模型。
  - 不再发布 benchmark、server、quantize、无关 sherpa 工具、开发头文件/库、preset 和测试 WAV。
  - 最终发布包 56 个文件、2.018 GiB；必需资产 18/18，禁止残留 0。
- `Directory.Build.props`
  - 正式版本设为 `1.0.0`，补充 Product / Authors 元数据。
- `.gitignore`
  - 忽略本地 `.codex/`、`.agents/` 工具状态。
- `README.md`
  - 重写为正式版中文文档：功能、系统要求、安装、实时/离线工作流、数据与配置、模型矩阵、已知限制、源码构建、隐私、安全、再分发和发布门禁。
- `THIRD_PARTY_NOTICES.md`
  - 补充 FFmpeg、whisper.cpp/GGML、sherpa-onnx、ONNX Runtime、NAudio、SQLite 与模型上游说明。
  - 明确当前 FFmpeg 构建启用 `--enable-gpl --enable-version3`，再分发需履行 GPLv3 义务。
- 上一不同任务的 `change.md` 已归档为 `change/change-22.md`。

### 新增回归测试
- `ModelDownloaderTests`
  - 同尺寸错误 SHA 文件会重下。
  - 正确 SHA 已有文件不发 GET。
  - HTTP 503 后重试成功。
- `RealtimeTranscriptionPipelineTests`
  - `FinalizeAsync` 等待在途 chunk 完成。
- `ModelCatalogTests`
  - 拒绝目录穿越文件名。
  - 内置 legacy 模型不可删除。
- `SqliteTranscriptStoreTests`
  - 通过实际原生 SQLite 运行时写入并查询 session。
- 最终测试数从 55 增至 62，全部通过。

### 已执行的主要指令
```powershell
git status --short
git ls-files -v | Select-String '^S'
Move-Item change.md change/change-22.md
rg --files -g '!publish/**' -g '!**/bin/**' -g '!**/obj/**'
rg -n -i 'TODO|FIXME|HACK|NotImplementedException|password|secret|api[_-]?key|C:\\Users' ...
dotnet list MeetingTransfer.sln package --outdated --include-transitive
dotnet list MeetingTransfer.sln package --vulnerable --include-transitive
dotnet restore MeetingTransfer.sln --disable-parallel -p:NuGetAudit=false
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 --disable-parallel -p:NuGetAudit=false
dotnet format MeetingTransfer.sln whitespace --no-restore
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build -p:NuGetAudit=false
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64
Get-FileHash publish/win-x64/models/whisper-cpp-vulkan/models/ggml-large-v3-turbo.bin -Algorithm SHA256
publish/win-x64/models/ffmpeg/bin/ffprobe.exe -version
publish/win-x64/models/whisper-cpp-vulkan/whisper-cli.exe --help
publish/win-x64/models/sherpa-onnx/bin/sherpa-onnx-vad-with-online-asr.exe ... test_wavs/0.wav
publish/win-x64/models/sherpa-onnx/bin/sherpa-onnx-offline-speaker-diarization.exe ... test_wavs/0.wav
Start-Process publish/win-x64/MeetingTransfer.App.exe -WindowStyle Minimized -PassThru
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=...}
```

### 最终验证
- Build：0 warnings、0 errors。
- Tests：62 passed、0 failed。
- NuGet audit：六个项目均无已知易受攻击包。
- Format：`--verify-no-changes` exit 0。
- Publish：56 files，2.018 GiB，18/18 必需资产存在，0 个 server/benchmark/quantize/header/lib/test WAV 残留。
- 模型目录：7 个模型，0 个 Qwen 残留；默认 `ggml-large-v3-turbo.bin` SHA256 匹配。
- 发布版实时 CLI：10.053 秒样本输出 3 个有效 `results:`，RTF 0.068。
- 发布版 diarization CLI：输出 `speaker_00` 时间段，RTF 0.038。
- WPF 首次启动：生成 appsettings、models 与 SQLite；WM_CLOSE 后 20 秒内退出，exit code 0，新增 .NET Runtime 1026 = 0。
- 冒烟产生的 appsettings、models、data、recordings、exports 已从发布目录清理，交付目录保持首次启动状态。

### 仍未决 / 不属于本次可安全代决的事项
- Git 仓库仍没有首个 commit，所有项目文件均为未跟踪状态；发布前应由仓库所有者确认内容后建立 v1 基线提交与 tag。
- Meeting Transfer 自有源码尚未声明 LICENSE。若公开发布，必须先由权利人选择许可证。
- 部分可选模型没有可信上游 SHA256，当前依赖 HTTPS；应继续补齐摘要后再称为全部模型供应链固定。
- 实时模式仍按音源区分我方/远端，不做低延迟多人 diarization。
- 离线说话人边界文本切分仍是时间重叠近似值，没有共享逐词时间戳。
- sherpa diarization 当前仍为 CPU；SQLite 尚无历史会话浏览/恢复 UI。
- 尚无安装器、代码签名、自动更新和崩溃恢复。
- 第三方模型的精确再分发条款仍需发布者逐项法律确认；尤其 FFmpeg GPLv3 构建必须随分发履行许可证与源码义务。
