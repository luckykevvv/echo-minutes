# Change Log — 2026-07-15 — 1.0.0 发布前最终 Review

## 任务范围

- 完成首个正式版本前的最后一次代码、运行时与发布包检查。
- 设置页明确分隔离线转写、实时转写与功能资源。
- 无模型首次启动时，让“开始”和“导入”正确弹出缺少模型提示并可跳转设置。
- 发布包不包含任何模型权重；全部模型由用户安装后在设置中自行下载。
- 清理发布残留并完善 README。

## 修改内容

### 1. 模型页按用途分组

- `src/MeetingTransfer.App/ViewModels/ModelsListViewModel.cs`
  - 为模型卡片增加 `CategoryLabel`。
  - 使用 `ICollectionView` 按以下顺序分组：
    - `OFFLINE TRANSCRIPTION · 离线转写`
    - `REALTIME TRANSCRIPTION · 实时转写`
    - `FEATURE RESOURCES · 功能资源`
- `src/MeetingTransfer.App/SettingsWindow.xaml`
  - 模型列表改为绑定分组视图。
  - 每组增加强调色标题与横向分隔线。
  - 保留原有深色工业风、模型卡片、详情栏和下载操作。

### 2. 正确提示缺少模型

- `src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs`
  - 空闲时不再因缺少模型而禁用“开始”或“导入”；用户点击后获得明确说明。
  - “开始”缺少 Realtime Paraformer 时弹出“缺少实时模型”，可直接打开设置。
  - “导入”缺少默认离线模型时弹出“缺少离线模型”，可直接打开设置。
  - 已有离线模型但缺少 Speaker diarization 时，允许用户选择：继续仅转写、打开设置或取消。
  - 实时初始化进度文案由“加载内置模型”改为“加载实时模型”。

### 3. 修复重复关闭导致的 WPF 崩溃

- `src/MeetingTransfer.App/MainWindow.xaml.cs`
  - Windows Application 日志复现到 `Window.VerifyNotClosing()` 未处理异常。
  - 根因是异步清理完成后已经排队最终 `Close()`，用户此时再次关闭会先进入 WPF 关闭流程，随后排队回调再次调用 `Close()`。
  - 增加 `_finalCloseStarted` 状态保护，确保最终关闭只执行一次。

### 4. README 与发布策略

- `README.md`
  - 已包含功能、系统要求、快速开始、数据路径、模型与运行时、已知限制、源码构建、隐私/再分发及发布验证。
  - 明确设置页的“离线转写 / 实时转写 / 功能资源”分区。
  - 明确正式发布包不包含任何模型权重；模型由用户在设置中下载。
- `src/MeetingTransfer.App/MeetingTransfer.App.csproj`
  - 发布只复制 FFmpeg、sherpa-onnx、whisper.cpp Vulkan 的必要执行文件与运行库。
  - 不复制 `third_party` 中的模型目录或模型权重。
- 上一次不同任务的记录已归档为 `change/change-24.md`。

## 验证结果

- Release 构建：成功，0 warning / 0 error。
- 单元与回归测试：63/63 通过。
- `dotnet format --verify-no-changes`：通过。
- NuGet restore audit：成功，没有报告已知漏洞。
- win-x64 framework-dependent publish：成功。
- 发布目录：50 个文件，264,772,536 bytes（252.5 MiB）。
- 发布包模型权重扫描：0 个 `.onnx`、`.ggml`、`.gguf`、模型 `.bin`、`.pt`、`.pth`、`.safetensors`、`.ckpt`、`.tflite`、`.pb` 或 `.params` 文件。
- 发布版主程序 SHA256：`8C3A79E521A2360E1E02E2435A20AF473222D9C4902C828A0E82D579FA7FC062`（随后因最终清理重发会再次核对）。
- 真实 WPF 界面检查：
  - 无模型时“开始”和“导入”均可点击。
  - 状态区同时列出缺少的离线、实时和说话人分离模型。
  - “开始”正确显示 Realtime Paraformer 缺失提示。
  - 设置窗口可正常打开。
  - 离线、实时、功能资源三组标题与横向分隔线可见，卡片归类正确且无明显裁切回归。

## 执行命令

```powershell
git status --short
git ls-files -v | Select-String '^S'
rg -n "CategoryLabel|CardsView|缺少实时模型|缺少离线模型|OpenSettingsAsync|GroupStyle" -S .
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-2)}
dotnet restore MeetingTransfer.sln -p:NuGetAudit=true
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 -p:NuGetAudit=true
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath (Resolve-Path 'publish/win-x64').Path -Recurse -Force
Get-ChildItem publish/win-x64 -Recurse -File
Get-FileHash publish/win-x64/MeetingTransfer.App.exe -Algorithm SHA256
```

第一次带 `--no-restore` 的 RID publish 因资产文件缺少 `win-x64` target 返回 `NETSDK1047`；补执行带 `-r win-x64` 的 restore 后发布成功。
测试进程终止后的第一次发布目录删除遇到短暂 DLL 文件锁；等待进程完全退出后重试成功，随后从空目录完成最终发布。

## 仍需知悉的限制

- 实时模式尚不执行多人声纹聚类；Speaker diarization 只用于导入文件。
- 离线 ASR 与 diarization 的说话人边界仍是按时间重叠近似映射。
- 当前 sherpa diarizer 使用 CPU。
- 部分可选模型上游尚未提供已固定到目录的可信 SHA256；这些文件依赖 HTTPS 与下载长度检查，README 已明确披露。
- 尚无历史会话浏览、安装器、自动更新与代码签名。
