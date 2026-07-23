# Change Log — 2026-07-20 — EchoMinutes 1.2 历史录音回放迭代

## 任务范围

- 在上一轮可靠性与历史会话功能基础上，继续最高优先级迭代：保存会话录音轨道、支持按转写时间戳回放，并诊断缺失录音文件。
- 保持现有 WPF 稳定客户端，不借本轮功能一次性重写 UI 或跨平台音频层。
- 循环执行实现、单项测试、失败修复、全量回归和真实运行时验证。

## 分支与日志归档

- 开始前检查 `git status --short` 与全部 skip-worktree 状态；当前没有 `S` 文件。
- 从当前连续工作树创建 `codex/v1.2-history-playback`，未提交、未暂存，也未丢弃上一轮改动。
- 上一轮 v1.1 可靠性与跨平台核心日志归档为 `change/change-34.md`。

## 已执行命令

```powershell
git status --short
git branch --show-current
git ls-files -v | Select-String '^S'
git switch -c codex/v1.2-history-playback
Move-Item -LiteralPath change.md -Destination change/change-34.md
```

## 当前设计审计

- `PcmSessionRecorder` 已能返回每条录音的路径、来源 ID 和来源类型，但 `TranscriptDocument` 与 SQLite schema 尚未保存这些轨道。
- 历史会话只恢复 speaker/segment，主工作台时间戳仍是纯文本，没有播放服务、播放状态或缺失文件提示。
- 本轮将录音轨道作为会话子表保存；删除会话只删除数据库引用，不自动删除用户 WAV，避免不可逆数据删除。

## 已实现功能

### SQLite v3 与录音轨道

- `TranscriptDocument` 新增 `AudioTracks`，每条轨道保存 GUID、文件路径、来源 ID、来源类型、会话时间轴偏移和可选时长。
- SQLite schema 升级到 v3，新增会话级 `audio_tracks` 子表、外键级联和时间轴索引；v1/v2 数据库原位补表，不重写已有 speaker/segment。
- 保存会话时在同一事务中同步轨道，移除已解除关联的旧行；加载历史会话时恢复为规范化绝对路径。
- 数据库优先保存相对数据库目录的路径，便携目录整体移动后仍能维持引用；损坏路径按“缺失文件”降级，不让整个历史页加载失败。
- 历史摘要显示轨道数量，并实际检查每条文件是否存在；缺失录音在会话卡片显示警告。

### 时间戳回放

- 转写左侧时间戳改为可访问按钮，按 segment 的 `SourceId`、`SourceKind` 和时间轴位置选择正确轨道并定位播放。
- 新增基于 NAudio `WaveOutEvent` 的播放服务、停止播放按钮、自然结束/异常处理和本地日志。
- 旧会话没有轨道时给出“只保存了转写”的提示；文件被移动或删除时显示具体文件名，不阻止打开转写。
- 新建/切换会话、开始录音、编辑时间结构或关闭应用时会安全停止当前播放。

### 多次录音与精修一致性

- 同一会话每次录音使用时间戳加 GUID 的唯一 WAV 文件名，不再覆盖同一设备的上一条轨道。
- 捕获时间改用单调 `Stopwatch`，后续录音从既有 segment/轨道末尾继续，实时转写不再重新叠到 0 秒。
- 停止顺序改为先停止音频设备，再封口 recorder，避免最后一个回调来不及写入；封口后读取实际 WAV 时长。
- 录音后离线精修继承全部轨道，并把结果时间加上各轨道偏移；导入媒体的抽取 WAV 也登记为可回放轨道。
- 只剩录音、没有转写的会话仍会持久化；真正未开始录音的新空白会话继续跳过。

## 版本与文档

- `VersionPrefix` 与安装器默认版本提升到 `1.2.0`，CHANGELOG 增加 1.2.0 的新增、改进、修复和数据兼容说明。
- README 更新回放入口、数据表、缺失文件限制、版本命令和 91 项跨平台核心测试说明。
- 跨平台路线没有取消 CLI/Avalonia，只在其前插入录音数据关系这一必要里程碑：v1.3 离线 CLI、v1.4 Avalonia。

## 验证结果

- Windows Release build：0 warning / 0 error。
- Windows 自动化：Core/STT 91/91、WPF/Audio/配置/更新器 13/13，总计 104/104。
- Ubuntu 24.04 / .NET SDK 8.0.422 实机：Core/STT 91/91，0 failure；新增 SQLite v3、路径和轨道解析测试全部通过。
- 真实 WaveOut 静音 WAV：本机输出设备上完成从 100 ms 定位、播放、停止和资源释放。
- WPF 真渲染烟雾测试：时间戳命令成功调用 fake playback，停止命令恢复状态，删除 WAV 后显示缺失文件名。
- `dotnet format --verify-no-changes`、NuGet AuditMode=all 和 `git diff --check` 通过。
- XAML 8/8 可解析；中英文资源各 183 键，差异 0；1.2.0 Release notes 成功提取。
- 最终纯净候选 `artifacts/v1.2-rc2-20260720`：58 文件、265,081,796 bytes、模型权重/用户数据/PDB/临时文件均为 0、许可证 9，App/Core/Updater 均为 1.2.0。
- 独立 runtime probe：进程响应、`CloseMainWindow()` 成功、退出码 0；日志包含 v1.2.0 启动/跳过空白会话/正常退出，SQLite 未因空白会话创建，Windows Application Event Log 匹配崩溃 0。

## 失败反馈与修复循环

1. 首次播放烟雾回归仍按 v1.1 语义断言“转写删空后会话不落库”，但测试会话已关联录音轨道；新实现正确保留了会话并报告录音缺失。更新断言为轨道数 1、缺失数 1，避免为了通过旧测试反向丢弃用户录音记录。
2. 最终差异复核发现轨道偏移取自“开始录音”的基准，而 WAV 实际从首个音频块开始写，设备启动延迟会让点击时间戳略微向后。改为每个来源使用首个 `PcmAudioChunk.SessionOffset`，麦克风与系统音频分别精确对齐。

## 本机与远程可追溯操作

- 本机从连续工作树创建 `codex/v1.2-history-playback`，并将旧日志移动到 `change/change-34.md`。
- 本机用 `Copy-Item -Recurse` 从最终纯净候选创建 `artifacts/v1.2-runtime-probe2-20260720`；测试配置与运行数据只写入 probe，不污染发布输入。
- Linux 开发机沿用 `/home/taohao11134/work/echo-minutes-v1.1` 验证目录和 `/home/taohao11134/work/.dotnet` 用户级 SDK，没有 sudo、系统包或服务改动。
- SSH/scp skill 的 scp/rsync wrapper 仍因 `$Host` 参数与 PowerShell 只读变量冲突而不可用；本轮没有再次调用 raw scp，而是把五个文本文件 gzip+base64 后通过 `remote-cmd.ps1` 写入既有目录，并用本地/远端 SHA256 确认一致。

## 主要命令

```powershell
dotnet restore MeetingTransfer.sln -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1901;NU1902;NU1903;NU1904'
dotnet build MeetingTransfer.sln -c Release --no-restore
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet test tests/MeetingTransfer.App.SmokeTests/MeetingTransfer.App.SmokeTests.csproj -c Release --filter AudioPlaybackService_PlaysAndStopsSilentWavWhenDeviceIsAvailable
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 -p:Version=1.2.0
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:Version=1.2.0 -o artifacts/v1.2-rc2-20260720
& "$HOME\.codex\skills\windows-ssh-remote-dev\scripts\ssh.ps1" -Host dev115 -Diag
& powershell.exe -File "$HOME\.codex\skills\windows-ssh-remote-dev\scripts\remote-cmd.ps1" -Host dev115 -Command "... dotnet test tests/MeetingTransfer.Tests/MeetingTransfer.Tests.csproj -c Release ..."
Start-Process artifacts/v1.2-runtime-probe2-20260720/MeetingTransfer.App.exe -WorkingDirectory artifacts/v1.2-runtime-probe2-20260720 -WindowStyle Hidden -PassThru
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$started.AddMinutes(-1)}
```

## 下一轮优先级

1. 为缺失录音增加安全“重新定位文件”，校验 WAV 格式、时长与来源后再更新数据库引用。
2. 模型下载加入 HTTP Range 断点续传、ETag/Last-Modified 防错续传和部分文件状态验证。
3. 为录音后精修建立 30–120 分钟真实双轨基准，记录峰值内存、实时率、漏字率和 speaker 边界误差。
4. 引入真实 token/word alignment；当前 `t_dtw=-1` 时继续只承诺片段级定位，不伪造逐词精度。
5. v1.3 实现 Linux/macOS 离线 CLI，再推进 v1.4 Avalonia 与平台音频后端。

## 2026-07-23 用户测试构建

- 开始前复核当前分支为 `codex/v1.2-history-playback`，工作树保留既有 v1.1/v1.2 改动，全部 skip-worktree 检查结果为空。
- 使用 .NET SDK 8.0.422 重新执行依赖还原和 Release 编译：0 warning、0 error。
- 完整 Windows 自动化通过 104/104：Core/STT 91/91，WPF/Audio/配置/更新器 13/13。
- 生成便携测试包 `artifacts/v1.2-user-test-20260723`：发布时共 58 个文件、265,081,796 bytes，PDB 0、模型权重 0；`MeetingTransfer.App.exe` 文件版本为 1.2.0.0。
- 启动测试包后进程正常响应，并通过 Windows 可见窗口检查确认“欢迎使用 EchoMinutes”首次引导页已显示并切到前台。启动会按正常应用行为在该测试包内生成 `appsettings.json`、`models.json` 和 `data/`。

### 本轮命令

```powershell
git status --short
git branch --show-current
git ls-files -v | Select-String '^S'
dotnet --version
dotnet restore MeetingTransfer.sln -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1901;NU1902;NU1903;NU1904'
dotnet build MeetingTransfer.sln -c Release --no-restore
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 -p:Version=1.2.0
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:Version=1.2.0 -o artifacts/v1.2-user-test-20260723
Start-Process artifacts/v1.2-user-test-20260723/MeetingTransfer.App.exe -WorkingDirectory artifacts/v1.2-user-test-20260723 -PassThru
```
