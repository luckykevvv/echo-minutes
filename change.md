# Change Log — 2026-07-20 — EchoMinutes 1.1 可靠性、历史会话与跨平台核心

> 发布编号说明（2026-07-23）：本轮最初按 `1.1.0` 构建和验证；根据后续版本策略，在推送 GitHub 分支时正式编号调整为 `1.1.1`。下方保留原始命令和候选包名称以维持可追溯性。

## 任务范围

- 在新分支 `codex/v1.1-reliability` 上落实上一轮 review 的功能与漏洞修复。
- 循环执行“实现 → 单项测试 → 修复失败 → 全量回归 → 发布/GUI/runtime 验收”。
- 评估并实际验证 Linux/macOS 方向，不把 WPF 部分编译误称为完整跨平台支持。

## 分支与版本

- 从 `main` 创建 `codex/v1.1-reliability`；创建前检查工作树和 skip-worktree 状态。
- `VersionPrefix` 与安装器默认版本提升到 `1.1.0`，开发构建继续保留 `-dev` 后缀，正式 publish 使用 `Version=1.1.0`。
- 上一轮纯 review 日志归档为 `change/change-33.md`；更早的正式版收尾日志为 `change/change-32.md`。

## 已实现功能

### SQLite v2 与历史会话

- `speakers`、`segments` 改为 `(session_id, id)` 会话内复合主键，外键级联删除。
- 旧库自动迁移；从 segment 快照重建被旧全局 speaker 主键覆盖的历史说话人。
- 保存会话时先在同一事务中清理该会话旧子表，再完整写回，合并/删除后不残留旧行。
- 新增会话列表、完整加载和删除 API，包含时间、片段数、speaker 数与总时长摘要。
- 新增“会议档案”页面、新建会议、打开历史会话和删除旧会话。

### 转写编辑

- 支持按文本或 speaker 搜索。
- 支持直接编辑片段文本，Esc 放弃修改。
- 支持 `Ctrl+Enter` 按光标比例拆分文本与时间区间。
- 支持合并同一 speaker 的上一片段、删除片段并清理未再使用的 speaker。

### 录音后高质量精修

- 设置 → 常规新增可选开关；默认关闭，避免未确认的长时间后处理。
- 录音仍先保存实时稿和每个音频源的完整 WAV；启用后再用当前离线模型逐轨重新转写。
- 麦克风轨统一为“我 / Me”，系统音频轨保留离线 speaker 结果；全部成功后才原子替换实时稿。
- 精修失败或取消时保留实时稿和原始录音，不用半成品覆盖现有会话。

### 任务、进程与实时可靠性

- 导入和精修新增“取消处理”，复用同一个可观察取消状态。
- FFprobe 改为异步、可取消、15 秒超时和进程树清理。
- 实时音频从 `async void` 积压改为容量 32 的 `Channel` 单消费者；队列满时跳过新转写块，但 WAV 录音继续保存。
- `RelayCommand` 新增可等待执行和统一错误路由，异步命令异常不再直接成为 WPF 未处理异常。
- 外部语音 CLI 的 stdout/stderr 合并、进度回调、取消和进程清理拆到 `ExternalCliRunner`。
- `WhisperMaxSegmentSeconds` 真实参与分句，不再是无效配置。
- 实时模型初始化前的清理不再提前解除 busy 状态，避免导入、设置、历史等其他命令在模型尚未就绪时并发进入。
- WAV recorder 的写入与释放统一在同一把锁内检查状态，修复停止录音与最后一个回调竞争时释放后重建 writer 的窗口。

### 配置、安全与诊断

- 损坏或 `null` 的 `appsettings.json` / `models.json` 会备份为 `.broken-*`、恢复示例/默认配置并在 onboarding 后提示。
- 补齐嵌套配置的 null 恢复和音频范围归一化，配置临时文件在成功/失败后都会清理。
- 模型 `tar.bz2` 成员在解压前检查声明大小，解压后再次检查实际大小，阻止异常压缩膨胀。
- 新增 5 MiB 滚动本地日志 `data/logs/echo-minutes.log`，记录版本、恢复、导入、保存、实时失败和未处理异常。
- 日志目录不可创建时诊断功能会安全禁用，不再由辅助日志反过来阻止应用启动。
- 新空白会话不会落库污染历史；已持久化会话即使将片段全部删空，仍会保存删除结果。
- 离线精修先把完整新文档提交到 SQLite，成功后才替换界面内存稿；取消或存储失败继续保留实时稿。
- GitHub Actions 的 checkout、setup-dotnet 和 release action 固定到核对过的 commit SHA。

### 跨平台基础

- 新增 push / pull_request CI：Core/STT 在 Windows、Ubuntu、macOS runner 上测试；完整 WPF 解决方案仍在 Windows 单独测试。
- 修复测试中写死的 `cmd.exe` 与反斜杠路径。
- 新增 `docs/cross-platform-roadmap.md`，明确 Avalonia、平台音频、按 RID 运行时、打包签名与 v1.2/v1.3 里程碑。
- 当前不宣称 GUI 跨平台：WPF、NAudio WASAPI、Updater、Inno Setup 与 bundled binaries 仍是 Windows-only。

## 失败反馈与修复循环

1. 首次 publish 因缺少 `net8.0-windows/win-x64` RID assets 报 `NETSDK1047`；补显式 RID restore 后 publish 通过。
2. 首次编辑工作台构建因 WPF `TextBox` 不支持 `LineHeight` 失败；移除无效属性后 0 warning / 0 error。
3. 首次编辑烟雾测试错误假定“合并 speaker 会合并文本”；改为先验证 speaker 归并，再显式调用片段合并，行为与测试一致。
4. 首次 Ubuntu 测试 84 项中 2 项失败：`cmd.exe` 和 `m\\encoder.onnx` 为测试写死 Windows；改为按 OS 选择 `/bin/sh` / `cmd.exe` 和 `Path.Combine`，Ubuntu 重跑全绿。
5. computer-use 可读取 onboarding 与主工作台完整可访问性树，但 Windows 桌面处于锁屏，skill 安全规则禁止解锁/继续输入；没有盲点坐标。改由 WPF 真渲染烟雾测试覆盖页面切换和编辑交互，并保留本地日志/Event Log 证据。
6. 首个最终候选的真实关闭日志显示 `Saved session ... with 0 segment(s)`；追踪到 fresh document 无条件落库，增加持久化状态后重新发布，空白启动/关闭改为 `Skipped new empty session ...` 且不创建 SQLite 文件。
7. 最终差异审查发现 `StartAsync` 的旧状态清理会重置 `IsBusy`；调整 busy 设置顺序，并验证 busy/recording 时 speaker merge command 均不可执行。
8. 最终差异审查发现精修在 SQLite 成功前先替换内存文档；改为数据库提交成功后再切换 UI，保持取消/失败语义一致。
9. 用“文件占用目标目录”模拟不可写日志路径，首次测试证明 `Directory.CreateDirectory` 会成为启动风险；初始化改为 best-effort，新增回归后通过。
10. 并发审查发现 recorder 在 `Write` 锁外检查 `_disposed`；将检查移入锁并让 `Dispose` 在释放 writer 前原子标记结束，消除最后音频回调竞争。

## 实际验证

- Windows Release build：0 warning / 0 error。
- Windows 自动化：核心 85/85、WPF/更新器/配置恢复 12/12，总计 97/97。
- Ubuntu 24.04 `linux-x64`：固定 SDK 8.0.422，Core/STT 85/85 通过。
- NuGet `AuditMode=all`：restore 通过，无 NU1901–NU1904。
- XAML/XML：8 个文件全部可解析。
- 本地化：中文 183 键、英文 183 键，无缺失。
- GitHub Actions YAML：2 个 workflow 均通过 PyYAML 解析。
- `dotnet format --verify-no-changes --severity warn` 与 `git diff --check` 通过。
- whisper.cpp Vulkan 实测：AMD Radeon 780M，26.9 秒合成会议音频约 2 秒完成，JSON 输出 8 个带有效片段时间戳的 segment。
- token 级实测：普通 token 的 `t_dtw=-1`，因此本轮没有用字符比例伪造逐词时间戳；后续需单独 DTW 对齐模型。
- 配置恢复 runtime probe：两个损坏配置均生成 `.broken-*` 备份并恢复为可解析 JSON，Windows Application Event Log 未发现 EchoMinutes/.NET crash。
- 首轮正常关闭 runtime probe：进程响应、`CloseMainWindow()` 成功、退出码 0，日志含启动/保存/退出，Windows Application Event Log 匹配崩溃 0；该轮由日志发现空会话落库问题。
- 修复后 runtime probe：进程响应、正常关闭、退出码 0；日志含 `Skipped new empty session`，SQLite 文件不存在，Windows Application Event Log 匹配崩溃 0。
- 最终纯净候选 `artifacts/v1.1-rc2-20260720`：58 个正式运行文件、265,061,828 bytes、模型权重 0、用户数据 0、PDB 0、`.broken` / `.tmp` / `.part` 0、许可原文 9；App/Core/Updater 均为 `1.1.0`。
- README/CHANGELOG/roadmap/third-party notice 的本地链接缺失 0；敏感信息扫描未发现内网 IP、开发账号、密码、API key、bearer token 或私钥残留。
- 两个 GitHub Actions workflow 均可解析，7 个 `uses:` 全部固定 40 位 commit SHA；所有 8 个项目的直接与传递 NuGet 包均报告无已知漏洞。

## 本机与远程可追溯操作

- 本机新增 SSH alias `dev115` 到 `C:\Users\C3EZ\.ssh\config`，仅记录 HostName/User/accept-new，不保存密码。
- Linux 开发机：`/home/taohao11134/work/echo-minutes-v1.1` 为源码验证目录，`/home/taohao11134/work/.dotnet` 为用户级 SDK 8.0.422；未使用 sudo、未修改系统包或服务。
- skill 的 rsync/scp wrapper 因参数名 `$Host` 与 PowerShell 只读变量冲突而无法运行；在明确记录后仅用 raw `scp.exe` 传输源码 ZIP/单个测试文件，所有远程命令仍通过 `remote-cmd.ps1` 和 `dev115` alias 执行。
- 本机发布目录 `artifacts/v1.1-rc2-20260720` 保持纯净；用 `Copy-Item -Recurse` 建立独立的 `artifacts/v1.1-runtime-probe2-20260720`，仅在后者写入测试配置和运行数据，避免把 runtime probe 污染物误当发布输入。

## 主要命令

```powershell
git status --short
git ls-files -v | Select-String '^S'
git switch -c codex/v1.1-reliability
dotnet restore MeetingTransfer.sln -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1901;NU1902;NU1903;NU1904'
dotnet build MeetingTransfer.sln -c Release --no-restore
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet list MeetingTransfer.sln package --vulnerable --include-transitive
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 -p:Version=1.1.0
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:Version=1.1.0
python -c "import pathlib,yaml; [yaml.safe_load(p.read_text()) for p in pathlib.Path('.github/workflows').glob('*.yml')]"
Start-Process artifacts/v1.1-runtime-probe2-20260720/MeetingTransfer.App.exe -WorkingDirectory artifacts/v1.1-runtime-probe2-20260720 -WindowStyle Hidden -PassThru
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$started.AddMinutes(-1)}
& "$HOME\.codex\skills\windows-ssh-remote-dev\scripts\ssh.ps1" -Host dev115 -Diag
& "$HOME\.codex\skills\windows-ssh-remote-dev\scripts\remote-cmd.ps1" -Host dev115 -Command "uname -a; ... dotnet test ..."
```

## 下一轮优先级

1. 会话数据库保存录音轨道路径，增加时间轴点击播放与缺失录音修复提示。
2. 为录音后精修建立 30–120 分钟双轨基准和内存/速度/边界误差门禁。
3. 模型下载增加 HTTP Range 断点续传。
4. 引入独立签名清单和 Windows 代码签名；macOS 版本需 notarization。
5. v1.2 先实现 Linux/macOS 离线 CLI，再开始 Avalonia GUI，避免一次性重写稳定 WPF 客户端。
