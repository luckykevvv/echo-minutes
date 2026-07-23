# Change Log — 2026-07-20 — 下一版本项目审查

## 任务范围

- 对当前 `main` 进行只读架构、数据持久化、实时转写、导入流程、测试、依赖与发布链审查。
- 不修改产品代码；仅按 `AGENTS.md` 归档上一份日志并记录本次审查与验证命令。

## 当前状态

- 工作区审查开始时无未提交修改，也没有 `skip-worktree` 文件。
- 当前公开正式版为 `v1.0.1`；GitHub Release 工作流成功，安装器、便携 ZIP 与两份 SHA256 文件均存在。
- `main` 已包含尚未进入 `v1.0.1` 的 README 演示素材和相对存储路径修复，`CHANGELOG.md` 已记录在 `Unreleased`。
- Release build 为 0 warning / 0 error；核心测试 77/77、WPF/更新器烟雾测试 6/6，总计 83/83 通过。
- NuGet 全依赖漏洞审计通过；存在可评估的常规更新：`Microsoft.Data.Sqlite 10.0.10`、`SQLitePCLRaw 3.0.4`、`NAudio 2.3.0` 等。

## 主要发现

### 必须先修复

- SQLite 的 `speakers.id` 当前是全库主键，但应用会在不同会话重复使用 `speaker-1`、`local-user`、`remote-1`。保存后续会话会覆盖较早会话的 speaker 行；再次保存合并后的文档也不会清理已经删除的 speaker/segment 行。下一版本应加入 schema version 与迁移，将 speaker 键改为会话内唯一，并以事务完整替换单个会话的子表。
- 实时音频事件使用 `async void` 处理并在一个 `SemaphoreSlim` 后排队。若外部 ASR 处理慢于实时输入，未完成事件和 PCM 块会持续积压。应改为有界 `Channel`/单消费者管线，明确背压、丢弃策略与停止时 drain 行为。
- 配置 JSON 在启动时直接反序列化；损坏、手工写错或被写成不兼容结构时会在主窗口出现前终止应用。应增加 schema 校验、备份与 `.broken-*` 恢复路径，并给用户可理解的错误提示。

### 下一轮应一起修改

- 导入流程已有 `_operationCts`，但没有用户可见的取消命令；长音频只能关闭应用中止。应新增取消按钮，并将 ffprobe/ffmpeg/ASR/diarization 的取消与超时贯通。
- `TryProbeAudioDuration` 使用无超时的同步 `WaitForExit()`；异常媒体或子进程卡住时可能冻结 UI。应改为异步、可取消并带超时的 probe。
- `WhisperMaxSegmentSeconds` 已定义并传入分句函数，但实现中明确忽略。应真正按时间切段或删除该配置，避免无效参数。
- `RelayCommand.Execute` 为 `async void` 且不捕获执行异常；例如语言配置写入失败可升级为 UI 线程未处理异常。应引入统一的 async command 错误通道。
- `SherpaOnnxSpeechEngine.cs` 已接近 1900 行并同时承担实时缓冲、进程管理、模型路由、Whisper 解析、分句、chunk、diarization 与临时文件管理；下一版本应按职责拆分，移除已不再使用的 legacy sherpa Whisper 路径。

## 建议的 v1.1.0 主线

1. 数据层迁移与历史会话：修复复合键/清理语义，增加会话列表、打开、重命名、删除和恢复。
2. 录音结束后的高质量重处理：实时模式继续提供低延迟预览，停止后可选择用离线 Whisper 重新转写并对远端轨道执行说话人分离。
3. 可编辑转写工作台：编辑文本、拆分/合并片段、搜索、跳转播放，并把逐词时间戳接入 diarization 边界与字幕导出。
4. 实时管线可靠性：有界队列、设备断开恢复、可观察的延迟/积压状态，以及真正可取消的长任务。
5. 工程门禁：新增 push / pull_request CI、数据迁移与多会话回归测试、可选 golden-audio 集成测试、Dependabot/CodeQL，并评估代码签名。

## 验证与审查命令

```powershell
git status --short
git branch --show-current
git log -5 --oneline
git ls-files -v | Select-String '^S'
dotnet restore MeetingTransfer.sln -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904
dotnet build MeetingTransfer.sln -c Release --no-restore
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore
dotnet list MeetingTransfer.sln package --outdated --include-transitive
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity info
Invoke-RestMethod -Uri 'https://api.github.com/repos/luckykevvv/echo-minutes/releases/tags/v1.0.1'
Invoke-RestMethod -Uri 'https://api.github.com/repos/luckykevvv/echo-minutes/actions/runs?per_page=10'
```

`dotnet format --verify-no-changes --severity info` 因信息级 IDE/CA/xUnit 建议返回 1；其中值得进入下一轮的包括两处未向 `ReadToEndAsync` 传递取消令牌，其余大多为构造函数、集合初始化和微小性能风格建议。
