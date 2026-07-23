# EchoMinutes 跨平台路线评估

## 当前结论

EchoMinutes 1.2 的核心数据、导出、模型目录、下载器、STT 接口和外部 CLI 调度均为 `net8.0`。包括 SQLite v3 录音轨道迁移在内的 91 项核心测试会在 `windows-latest`、`ubuntu-latest`、`macos-latest` 上重复验证；WPF/WaveOut 回放仍只属于 Windows 客户端。

这不等于完整应用已经支持 Linux 或 macOS。当前仍有三块明确绑定 Windows：

- `MeetingTransfer.App` 使用 WPF 和 `net8.0-windows`。
- `MeetingTransfer.Audio` 使用 NAudio WASAPI 捕获系统音频与麦克风。
- 更新器、Inno Setup 安装器和仓库携带的 whisper.cpp / sherpa-onnx / FFmpeg 二进制均为 Windows x64 产物。

因此不应通过给 WPF 项目添加 RID 或条件编译来宣称跨平台。那只会产生无法运行、无法录音或无法更新的空壳构建。

## 推荐技术方向

### UI：Avalonia，而不是继续扩展 WPF

建议把现有 ViewModel、Core 和 STT 服务保留，将窗口和控件层逐步迁移到 Avalonia：

- Avalonia 同时覆盖 Windows、macOS 与 Linux，适合当前高密度桌面工作台。
- 先建立与 `MeetingTransfer.App` 并存的 `MeetingTransfer.Desktop`，不要一次删除稳定的 WPF 客户端。
- 历史会话、模型管理、设置和转写编辑是最适合先迁移的纯 UI 页面。
- 音频设备选择、窗口控制、更新和文件选择器放在平台适配层，不进入共享 ViewModel。

### 音频：接口保留，按平台实现

现有 `IAudioCaptureService` 可以作为边界，但实现需拆分：

| 平台 | 麦克风 | 系统音频 |
| --- | --- | --- |
| Windows | WASAPI capture | WASAPI loopback |
| macOS | CoreAudio / AVFoundation | ScreenCaptureKit，需要用户授权 |
| Linux | PipeWire / PulseAudio | PipeWire monitor/source |

建议先实现麦克风跨平台，再实现系统音频。系统音频涉及 macOS 隐私授权和 Linux 音频服务器差异，不能用一个 FFmpeg 命令假装完全兼容。

### 运行时与模型

- 按 RID 发布 `win-x64`、`linux-x64`、`osx-arm64`，后续再评估 `linux-arm64` 和 `osx-x64`。
- 每个平台分别固定 whisper.cpp、sherpa-onnx 与 FFmpeg 的版本、SHA256 和第三方许可。
- macOS 优先验证 whisper.cpp Metal；Linux 优先验证 Vulkan，其次 CPU fallback。
- 模型权重继续独立下载，目录和数据库使用平台用户数据目录，而不是应用安装目录。

### 更新与打包

- Windows 保留当前 Inno Setup 与安全更新器。
- macOS 使用签名、notarization 和 DMG/PKG；更新包必须验证签名，不能只依赖同源 SHA256。
- Linux 第一阶段提供 AppImage 或 tarball，稳定后再评估 Flatpak。
- 发布工作流按平台拆 job，所有平台必须共享同一个版本入口和 changelog。

## 建议里程碑

### v1.1：稳定 Windows 产品与共享核心

- 完成数据库迁移、历史会话、转写编辑、可取消任务、实时背压和配置恢复。
- 保持 WPF 为唯一正式 GUI。
- 通过 Linux 实机和三平台 CI 固化 Core/STT 可移植性。

### v1.1.2：录音可回放的历史会话

- SQLite v3 保存录音轨道、来源、时间轴偏移与时长，旧数据库原位升级。
- Windows 客户端支持点击片段时间戳回放、停止播放和缺失文件诊断。
- 先补齐可验证的“转写—录音”数据关系；跨平台 CLI 与 Avalonia 方向不取消，只顺延一个里程碑。

### v1.1.3：跨平台命令行预览

- 增加 `MeetingTransfer.Cli`，支持离线媒体导入、Whisper 转写、JSON/SRT/VTT 导出。
- 发布 `linux-x64` 与 `osx-arm64` 预览包，验证模型目录、进程取消、FFmpeg 和 GPU 后端。
- CLI 不承诺实时录音，用于先证明端到端离线工作流。

### v1.1.4：Avalonia 桌面预览

- 迁移历史会话、导入、编辑、导出和模型管理。
- 接入各平台麦克风；系统音频按平台逐一验收。
- 建立 macOS 签名/notarization 与 Linux 包装流程后，才标记完整桌面支持。

## 下一轮急需项

1. 为缺失录音增加安全的“重新定位文件”流程，并校验候选 WAV 的时长、来源与格式后再更新引用。
2. 引入真正的 token/word alignment。当前 bundled whisper.cpp 实测只有可靠片段时间戳，普通 token 的 `t_dtw` 为 `-1`；需要单独评估 DTW 对齐模型，不能用字符比例伪造逐词精度。
3. 模型大文件下载增加 HTTP Range 断点续传，并对失败重试保留已验证的部分块。
4. 自动更新从“同一 Release 中的 SHA256”升级为代码签名或独立签名清单。
5. 为录音后离线精修增加真实双轨长会议基准，记录耗时、峰值内存、漏字率和 speaker 边界误差。

## 可选功能

- 会话标签、收藏与全文搜索。
- 音频波形、播放位置跟随高亮与片段级重新转写。
- 术语表、初始提示词和会议模板。
- 批量导入与后台队列。
- 可选的本地摘要/行动项生成；默认关闭并继续遵守本地优先原则。
