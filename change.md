# Change Log — 2026-07-15 — 统一窗口控制按钮

## 用户反馈

- 右上角最小化、最大化和关闭按钮的视觉大小不统一。

## 调整

- 将字符 `— / □ / ×` 替换为 Windows Fluent 窗口控制图标，避免不同字符字面框导致的大小差异。
- 三个窗口控制按钮统一为 `40 × 40` 点击区域、10px 图标、正常字重，并让按钮及图标在 42px 标题栏内水平/垂直居中。
- 主窗口、设置窗口、新手引导窗口和更新窗口共用相同图标规范。
- 关闭按钮右侧留白统一为 2px，使整组控件与 42px 标题栏对齐。

## 验证

- 5 个受影响 XAML 文件均通过 XML 解析。
- Release build：0 warning / 0 error。
- 自动化测试：74/74 通过（核心测试 73、应用烟雾测试 1）。

## 执行命令

```powershell
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
```
