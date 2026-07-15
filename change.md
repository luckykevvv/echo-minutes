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
- 第二次 Actions 暴露 SDK 可移植性问题：CI 将数组 `.Reverse()` 绑定为返回 `void` 的原地反转。已改为显式倒序索引循环，避免不同 .NET 8 SDK 的重载解析差异。
- 第三次 Actions 的 build/test 已通过，publish 暴露 `NETSDK1047`：通用 restore 没有生成 `win-x64` 目标，且 runner 默认选中了预装 SDK 10。已新增 `global.json` 锁定 .NET SDK 8.0.422，并在 publish 前显式执行 App 的 `-r win-x64` restore。
- 第四次 Actions 已通过 publish 和 Inno Setup 安装，但安装器编译失败：Chocolatey 安装的 Inno Setup 不包含 `Languages\ChineseSimplified.isl`。已用同版本 Inno Setup 6.7.1 在本机复现并移除该非内置语言引用；安装器界面暂用内置英文，应用本身的中英文功能不受影响。
- 修复后已在本机重新 publish，并用 Inno Setup 6.7.1 成功生成 `echo-minutes-setup-x64.exe`（55,633,688 bytes）；发布目录模型权重扫描和用户运行数据扫描均为 0。
- 将本地与 CI 生成的 `/artifacts/` 加入 `.gitignore`，避免安装器、便携包和临时发布目录误入 Git。
- 最终 `v1.0.0` Actions（run `29391954234`）全部通过，正式 Release 已发布且不是 draft/prerelease。
- Release 四个资产均已上传：安装器 55,643,903 bytes、便携 ZIP 100,299,948 bytes，以及各自 SHA256 文件。
- GitHub 资产 API 返回的安装器与 ZIP digest 分别为 `46e00bdc...b04144f`、`3cdb698e...bcb9902`，与 Release 中对应 `.sha256` 文件完全一致。
- Release 验收时远端 `main`、远端 `v1.0.0`、本地 HEAD 与本地 tag 均指向发布提交 `4ad61fe7745fd21b5fb77246535bdaaa7ff5b7ed`；后续仅追加本变更记录，Release tag 保持不动。

## 执行命令

```powershell
git rm --cached -r third_party
git add third_party .github/workflows/release.yml README.md installer
git check-attr filter -- third_party/ffmpeg/bin/avcodec-63.dll
git cat-file -s :third_party/ffmpeg/bin/avcodec-63.dll
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
python -c "import yaml, pathlib; yaml.safe_load(pathlib.Path('.github/workflows/release.yml').read_text(encoding='utf-8'))"
choco install innosetup --no-progress -y
curl.exe -4 --noproxy '*' -fL -o "$env:TEMP\innosetup.6.7.1.nupkg" "https://community.chocolatey.org/api/v2/package/innosetup/6.7.1"
& "$env:TEMP\echo-minutes-inno\app\ISCC.exe" "/DMyAppVersion=1.0.0" installer\EchoMinutes.iss
git -c http.proxy= -c https.proxy= -c http.sslBackend=schannel -c http.version=HTTP/1.1 -c http.curloptResolve=github.com:443:140.82.116.4 push origin main
$env:GIT_LFS_SKIP_PUSH='1'; git -c http.proxy= -c https.proxy= -c http.sslBackend=schannel -c http.version=HTTP/1.1 -c http.curloptResolve=github.com:443:140.82.116.4 push origin v1.0.0
curl.exe --noproxy '*' -H 'Accept: application/vnd.github+json' -H 'User-Agent: EchoMinutes-release-audit' 'https://api.github.com/repos/luckykevvv/echo-minutes/releases/tags/v1.0.0'
```
