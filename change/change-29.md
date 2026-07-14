# Change Log — 2026-07-15 — 正式版视觉验收与模型名称换行

## 用户反馈

- Realtime Paraformer 的模型名称过长，模型卡片出现横向越界和上下白线。
- 正式发布前需要完成新手引导、缺少模型提示、模型分组和 speaker 空状态的前台视觉验收。

## 修复

- `src/MeetingTransfer.App/SettingsWindow.xaml`
  - 模型名称改为独占一行并启用 `TextWrapping="Wrap"`。
  - Family、Engine 和 CPU/GPU 徽标移到名称下方的 `WrapPanel`，避免与长名称争抢横向空间。
- `src/MeetingTransfer.App/OnboardingWindow.xaml`
  - 新手引导第二步使用相同的两行布局：名称可换行，Family 与 Engine 徽标位于下一行。
- 保留此前完成的 speaker 区改版：无 speaker 时只有空状态；有 speaker 时在对应列表卡片内编辑 label。

## 前台视觉验收

- 使用正式 `publish/win-x64/MeetingTransfer.App.exe` 从无用户配置状态启动。
- 第一步正常显示；点击“下一步”可立即进入第二步，未再出现卡死或无响应。
- 第二步的 Offline transcription、Realtime transcription 和 Feature resources 分组清晰。
- Realtime Paraformer 名称与徽标分行显示，没有横向越界，也没有异常上下白线。
- 未下载模型时点击“下一步”，页面保持在第二步并显示橙色“请先选择并安装至少一个模型”提示。
- 跳过引导后主窗口正常显示；0 位 speaker 时右侧只有空状态，不存在可输入名称框。
- 设置页中 Offline、Realtime、Feature resources 分组和长模型名称均正常渲染。
- 验收过程中未下载任何模型。

## 自动化验证

- Release 测试：68/68 通过。
- 最终 Release build：0 warning / 0 error。
- 最终 `dotnet format --verify-no-changes`：通过。

## 最终发布

- 关闭验收应用后删除带运行期配置的 `publish/win-x64`，并从空目录重新发布。
- 同时清理旧的 `publish/win-x64-fixed` 临时发布目录。
- 正式 `publish/win-x64`：50 个文件，264,801,392 bytes（252.5 MiB）。
- 模型权重文件：0。
- `appsettings.json`、`models.json`、`data/`、`recordings/`、`exports/`：0。
- `MeetingTransfer.App.dll` SHA256：`8076906E289F4146C3C438F04EFE4D4101EAFED8726CF0D2F732DE2926BFD56A`。
- `publish/win-x64-locked` 仍由旧进程 PID 36360 占用，本次未强制终止该进程；它不是正式发布目录。

## 执行命令与操作

```powershell
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
dotnet test MeetingTransfer.sln -c Release --no-build
rg -n "Wrap|TextWrapping|ModelDisplayName|Family|Engine" src\MeetingTransfer.App\SettingsWindow.xaml src\MeetingTransfer.App\OnboardingWindow.xaml
Get-FileHash publish\win-x64\MeetingTransfer.App.dll -Algorithm SHA256
```

发布前通过 PowerShell 校验目标绝对路径位于工作区内，再使用 `Remove-Item -LiteralPath ... -Recurse -Force` 清理 `publish/win-x64` 与 `publish/win-x64-fixed`。前台操作通过 Windows Computer Use 完成：启动正式应用、进入引导第二步、滚动模型列表、验证缺少模型提示、跳过引导、打开设置页并关闭应用。
