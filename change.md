# Change Log — 2026-07-15 — 移除 Git LFS 并发布 Windows 安装器

## 用户反馈

- 当前网络恢复后不需要 Git LFS。
- GitHub Release 需要提供可直接安装的 Windows 安装包，而不只是便携 ZIP。

## Git 仓库调整

- 删除 `.gitattributes` 中的 Git LFS 规则。
- 20 个第三方运行时 DLL/EXE 恢复为普通 Git blob。
- 最大文件 `avcodec-63.dll` 为 98,917,376 bytes，低于 GitHub 单文件限制。
- Release workflow 不再启用 `actions/checkout` 的 `lfs: true`。
- README 不再要求安装 Git LFS 或执行 `git lfs pull`。

## Windows 安装器

- 新增 `installer/EchoMinutes.iss`，使用 Inno Setup 6 构建 `echo-minutes-setup-x64.exe`。
- 默认安装到 `%LocalAppData%\Programs\EchoMinutes`，无需管理员权限。
- 提供开始菜单快捷方式和可选桌面快捷方式。
- 支持正常卸载、覆盖升级、关闭正在运行的应用并在安装后启动。
- 安装内容来自已经通过模型权重和用户数据扫描的 `artifacts/publish`。
- 安装目录对当前用户可写，原有 GitHub Release 自动更新机制可继续原地更新。

## Release 工作流

- tag 构建现在同时产出：
  - `echo-minutes-setup-x64.exe`
  - `echo-minutes-setup-x64.exe.sha256`
  - `echo-minutes-win-x64.zip`
  - `echo-minutes-win-x64.zip.sha256`
- 工作流自动安装 Inno Setup、注入 tag 版本号、校验安装器确实生成，并上传全部四个 Release 资产。
- README 快速开始改为优先推荐安装器，便携 ZIP 保留为备选。
- 已知限制更新为“尚未代码签名”，不再错误声称没有安装器或自动更新。

## 验证

- Release build：0 warning / 0 error。
- 测试：74/74 通过。
- GitHub Actions YAML：PyYAML 解析通过。
- Inno Setup 脚本关键 section、每用户安装路径、固定输出文件名和最低权限均已静态核对。
- 当前机器未预装 Inno Setup；最终安装器编译由 Windows GitHub Actions 执行并通过实际 Release 资产验收。
- 第一次 `v1.0.0` Actions 暴露 `.gitignore` 回归：通用 `models/` 规则误排除了 `src/MeetingTransfer.Core/Models`。已改为仅忽略根目录 `/models/`，并单独忽略 `third_party` 模型权重；漏掉的四个模型目录源码已加入仓库。

## 执行命令

```powershell
git rm --cached -r third_party
git add third_party .github/workflows/release.yml README.md installer
git check-attr filter -- third_party/ffmpeg/bin/avcodec-63.dll
git cat-file -s :third_party/ffmpeg/bin/avcodec-63.dll
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
python -c "import yaml, pathlib; yaml.safe_load(pathlib.Path('.github/workflows/release.yml').read_text(encoding='utf-8'))"
```
