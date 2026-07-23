# Change Log — 2026-07-23 — 1.1.1 / 1.1.2 版本重编号与 GitHub 分支发布

## 任务范围

- 将上一轮称为 1.1 的可靠性、历史会话与编辑功能重建为独立 `1.1.1` 快照并推送远端同名分支。
- 将上一轮称为 1.2 的录音回放和最新 UI 优化统一重编号为 `1.1.2`，验证后推送远端同名分支。
- 不修改远端 `main`，不创建正式 tag 或 GitHub Release；避免未经用户最终验收升级次版本或触发正式发布。
- 后续常规迭代默认只递增补丁版本；只有用户检查并明确同意后才升级次版本号，功能迭代规模不受版本号策略限制。

## 开始状态

- 当前 `codex/v1.2-history-playback`、`codex/v1.1-reliability` 与 `main` 都指向提交 `277006c`；v1.1/v1.2 功能累计保留在未提交工作树。
- 远端只有 `origin/main`，没有 `1.1.1` 或 `1.1.2` 分支。
- 当前没有 skip-worktree 文件。
- 本机未安装 `gh` CLI；用户本轮只要求推送分支，不创建 PR，因此使用已配置的普通 Git HTTPS remote。
- 上一轮 UI 优化日志已归档为 `change/change-36.md`。

## 已执行命令

```powershell
git status -sb
git branch -vv
git remote -v
git log --oneline --decorate --graph -12
git ls-files -v | Select-String '^S'
git ls-remote --heads origin
Move-Item -LiteralPath change.md -Destination change/change-36.md
```

## 版本策略落实

- 用户确认常规迭代只递增补丁版本，功能规模照常；次版本升级必须在用户检查并明确批准后进行。
- 原 v1.1 功能快照正式编号为 `1.1.1`；原 v1.2 回放与 UI 功能正式编号为 `1.1.2`。
- README、CHANGELOG、程序集、安装器和跨平台路线版本号同步调整；归档日志保留当时实际使用的旧候选编号，并由本日志解释映射关系。

## 1.1.1 快照重建与发布

- 从 `main` 创建独立临时 worktree，根据原 v1.1 会话记录按顺序重放 55 次补丁调用中的成功源码补丁；排除原会话中已失败的大补丁和只针对 `artifacts/` runtime probe 的临时配置。
- 原日志移动时点同步恢复为 `change/change-32.md` 与 `change/change-33.md`，最终 `change.md` 保留 v1.1 实施记录并增加 1.1.1 重编号说明。
- `1.1.1` Release build 0 warning / 0 error；Core/STT 85/85、WPF 12/12，总计 97/97；格式与差异检查通过。
- 提交 `9351bd3`（`release: prepare EchoMinutes 1.1.1`）已推送到 `origin/1.1.1`，远端 `main` 未修改。

## 1.1.2 验证

- 当前完整工作树已将程序集、安装器、CHANGELOG、README 和路线文档从 1.2 系列重编号为 `1.1.2` / `1.1.3` / `1.1.4` 补丁序列。
- `1.1.2` Release build 0 warning / 0 error；Core/STT 91/91、WPF/Audio/配置/更新器 13/13，总计 104/104。
- `1.1.2` 将以 `1.1.1` 提交为父提交生成增量提交，确保远端历史真实表达“可靠性与历史编辑 → 录音回放与 UI 优化”。

## 补充命令

```powershell
git worktree add -b 1.1.1 C:\Users\C3EZ\Documents\Meeting_Transfer-v1.1.1-stage main
dotnet restore MeetingTransfer.sln -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1901;NU1902;NU1903;NU1904'
dotnet build MeetingTransfer.sln -c Release --no-restore
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore
dotnet format MeetingTransfer.sln --verify-no-changes --no-restore --severity warn
git add -A
git commit -m "release: prepare EchoMinutes 1.1.1"
git push -u origin 1.1.1
git write-tree
git commit-tree <tree> -p 9351bd3 -m "release: prepare EchoMinutes 1.1.2"
git push -u origin 1.1.2
```

## 失败反馈与处理

1. PowerShell 直接调用临时 `apply_patch.bat` 被系统拒绝访问；停止该路径，改由正式补丁工具顺序应用。
2. 原会话包含一次明确失败、随后拆分成功的诊断日志大补丁；重建时按原 tool output 排除失败调用，只采用后续成功补丁。
3. 原 runtime probe 的 `artifacts/` 配置补丁不属于源码且目标目录未进入 Git；重建时明确跳过，不把测试数据带入 1.1.1 分支。
