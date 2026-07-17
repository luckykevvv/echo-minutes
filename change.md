# Change Log — 2026-07-15–16 — UI 统一与正式版本地化收尾

## 用户反馈

- 右上角最小化、最大化和关闭按钮的视觉大小不统一。

## 调整

- 将字符 `— / □ / ×` 替换为固定画布的矢量窗口控制图标，避免不同字符字面框或系统字体缺字导致的大小差异。
- 三个窗口控制按钮统一为 `40 × 40` 点击区域、10px 图标、正常字重，并让按钮及图标在 42px 标题栏内水平/垂直居中。
- 主窗口、设置窗口、新手引导窗口和更新窗口共用相同图标规范。
- 关闭按钮右侧留白统一为 2px，使整组控件与 42px 标题栏对齐。
- 将最小化横线从画布下沿移到 10px 画布的垂直中心，与最大化和关闭图标共用同一中心轴。
- 设置页三个标签改为带实色背景和边框的分段容器，容器 2px 内边距覆盖原先透明外圈；标签间不再保留透明 Margin。
- 将独立 `SettingsWindow` 重构为可嵌入的 `SettingsView`，设置按钮现在在主 GUI 内容区内切换页面，不再打开新窗口。
- 设置页沿用主窗口标题栏，并提供 `Back to workspace`、Cancel 和 Save changes 三种返回路径。
- 保存后主 ViewModel 立即重新加载运行配置、模型状态和存储路径；模型下载、工具路径选择和更新检查保持原有行为。
- 模型列表移除 8px 透明内边距和透明 ListBoxItem 底色，改为覆盖整行的实色背景与 1px 分隔线；悬停和选中状态同样覆盖完整行宽。
- 模型删除操作不再使用透明 QuietButton，统一采用有实体背景和边框的操作按钮。
- 覆盖 WPF 分组列表默认 `GroupItem` 模板，移除 Offline / Realtime 分组外围残留的透明缩进；分组标题和模型行现在从面板左右边缘连续铺底。
- 按用户标注补齐三个结构空带：模型库标题与分组标题使用实色抬升背景、左右面板之间的 14px 区域铺设 Surface 底和中心分隔线、详情面板右侧改为 10px 实色边轨。
- 进一步确认详情区竖线来自嵌套 `ScrollViewer` 的默认边框；详情文字现直接由外层圆角卡片提供 19px Padding，内部滚动容器设为零边框、零 Padding，并移除多余右侧边轨。
- 当前开发版本提升为 `v1.0.1-dev`，并在主标题栏右上角、窗口控制按钮左侧持续显示程序集版本，便于区分旧发布包和本地验收构建；正式 Release 仍由 tag 工作流覆盖为无 `-dev` 的版本号。
- 实机确认详情区残留竖线来自面板间中心分隔线和详情卡片左描边，而非滚动容器；现已移除两者，详情卡片改用无描边的 `SurfaceRaisedBrush` 实色圆角背景承载全部描述。
- 窗口控制按钮显式使用白色 `InkBrush` 前景，确保矢量最小化、最大化和关闭图标不会继承系统默认黑色。
- ComboBox 的透明展开命中层由右侧 30px 扩展为整个元素宽度，文字、空白区和箭头均可点击展开；箭头保持独立右对齐，并增加模板宽度烟雾测试。
- 新增基于语言代码和可替换 ResourceDictionary 的本地化层，当前支持 `zh-CN` 与 `en-US`，语言保存到 `Ui.Language`，后续可通过新增语言包扩展。
- 设置页新增“常规 / General”语言选择并即时预览；新手引导标题栏同样可选择语言并立即持久化，主工作台同步使用同一语言状态。
- 安装器内置 English 与简体中文选择，仓库固定携带 Inno Setup 官方 `ChineseSimplified.isl`，自定义快捷方式与启动文案也提供双语。
- 修复自定义 ComboBox 选中项直接显示 `LanguageOption { Code = ... }` 的问题；语言目录项现在以 `简体中文` / `English` 自然语言显示，同时保留标准语言代码用于持久化。
- 模型页的模型名称、支持语言、离线/实时模式、分组标题、描述、速度、准确度、引擎标签、下载状态和操作按钮全部接入动态语言资源；切换设置语言后现有卡片和分组会立即刷新。
- 设置页剩余的工具、离线语言、更新检查和存储提示完成双语化；更新弹窗及下载/校验/失败状态同步跟随当前语言。
- 新手引导的侧栏说明、隐私与按需下载说明、模型准备提示、实时/离线工作流和状态文案完成双语化；窗口产品名统一为 EchoMinutes。
- 正式版清理中将 README、程序集产品元数据和安装器默认版本统一为 EchoMinutes 1.0.1，并将 README 中旧的独立设置窗口描述改为主界面内嵌设置页。
- 主窗口的内嵌设置标题、设置重载错误和新建会议默认标题跟随界面语言；中文默认标题使用“会议”，英文使用“Meeting”。
- GitHub Release 工作流的无模型检查新增 `tokens.txt`，与模型权重扩展名一起阻止进入便携 ZIP 和安装器输入目录。
- 更新器不再无条件递归删除 ZIP 所在目录；临时清理现在只允许作用于 `%TEMP%\EchoMinutes\updates` 下由应用创建的单级任务目录，异常或手动路径不会被删除。
- 更新器在解压前验证 ZIP 路径、条目数量与 2 GiB 展开上限，阻止路径穿越与异常膨胀包；新增真实 ZIP 覆盖测试，确认程序文件更新时 `appsettings.json`、`models.json` 和 `data/` 保持不变。
- 模型下载新增按目录声明大小计算的 512 MiB–8 GiB 下载上限，同时检查 HEAD、响应长度和实际流量，避免异常上游耗尽磁盘。
- 自动更新的 SHA256 响应限制为 16 KiB，避免异常校验文件无界进入内存。
- 模型目录审计发现 SenseVoice 原地址指向 937 MB FP32 文件、Paraformer 的 `model.onnx` 地址实际不存在；两者已切换到上游真实存在的 `model.int8.onnx`，并补充 `{Model}` 参数别名测试。
- 通过 Hugging Face 仓库树中的 LFS OID 与实际下载校验补齐 Whisper tiny.en/base/small、SenseVoice 和 Paraformer 共 7 个缺失 SHA256；当前目录 8 个模型、14 个文件全部固定摘要，无空摘要。
- 修正上述模型的目录大小为上游实际文件总和，避免 UI 显示错误，并确认 SenseVoice/Paraformer 的新模型和 tokens 地址均返回 HTTP 200。
- 修复设置页配置覆盖竞争：默认模型在页面打开后发生变化时，“保存设置”现在将界面字段合并到磁盘最新配置，不会用构造时的旧快照清空刚选择的模型。
- 新手引导切换语言同样基于最新配置保存，避免下载完成并自动设为默认后再切换语言导致默认模型丢失；烟雾测试覆盖两条写入顺序。
- 引导第二步不再把仅安装 Speaker diarization 视为可继续：至少需要一个离线或实时转写模型，说话人功能模型单独安装时会显示明确双语提示。
- 区分开发验收与正式产物版本：源码默认继续显示 `v1.0.1-dev` 便于识别本地构建，但 `artifacts/publish`、便携 ZIP、更新器和安装器均使用显式 `Version=1.0.1`，应用内部显示与安装器版本一致。
- 最终发布目录自动移除 App/Core/音频/STT/Updater 共 6 个 PDB 调试符号（164,420 bytes），保留运行文件并减少本机构建路径泄露；GitHub Release 工作流新增 PDB 禁入检查。
- 修正 `THIRD_PARTY_NOTICES.md` 的旧产品名、FFmpeg 来源与 SQLitePCLRaw 漏项，记录实际 NuGet 版本及 BtbN/FFmpeg 对应来源。
- 发布包新增 9 份上游许可原文，覆盖 FFmpeg GPLv3、whisper.cpp、sherpa-onnx、ONNX Runtime、NAudio、SharpCompress、Microsoft .NET、SQLitePCLRaw 与 SQLite；CI 会阻止许可文件缺失的 Release。
- 设置页文件浏览器与媒体导入选择器的过滤器跟随当前语言，不再固定显示英文 `Executables and model files / Media files / All files`。
- 离线引擎内部英文进度不再覆盖 UI 语言；准备、转写、后处理、说话人识别和完成阶段统一由主界面按设置语言格式化。
- 模型下载异常增加本地化“下载失败”上下文；默认 `Me / Remote / Speaker N` 标签会在未被用户改名时随中英文切换为 `我 / 远端 / 说话人 N`，自定义标签保持不变。
- 安装器启动前检查 x64 .NET 8 Desktop Runtime：从 dotnet shared host 注册路径、系统/用户默认目录及 `DOTNET_ROOT_X64` / `DOTNET_ROOT` 查找 `Microsoft.WindowsDesktop.App\\8.*`；缺失时以当前安装语言提示并提供微软官方下载入口，静默安装则安全退出。
- 再次按截图复核语言下拉框：设置页和新手引导均显式使用 `DisplayName`，显示 `简体中文` / `English`；模型名称、描述、速度、准确度、支持语言、类别、执行方式、操作按钮及下载状态均随当前设置语言即时刷新，技术族名和引擎名保留上游产品名称。
- 补齐主窗口最小化、最大化、关闭按钮的动态 ToolTip，以及更新窗口右上角关闭按钮的“稍后”提示；窗口控制提示不再固定为英文，切换设置语言后立即更新。
- 自动更新启动独立更新器时传递当前 `zh-CN` / `en-US` 语言；更新器退出主程序后若覆盖失败，系统错误对话框的标题、说明和日志标签会使用所选语言。主程序检测到更新器组件缺失时也改用本地化提示。
- GitHub Release 工作流新增唯一版本入口：tag 必须严格使用正式 `vMAJOR.MINOR.PATCH`，解析后通过 `RELEASE_VERSION` 传给构建、测试、publish 和 Inno Setup；创建 Release 前逐一核对 App、Core、Updater 程序集与安装器版本，阻止内部版本漂移的产物上传。
- Windows `FileVersionInfo.ProductVersion` 对 Inno Setup 安装器可能返回尾部填充空格；CI 版本比较现先调用 `Trim()`，避免正确的 `1.0.1` 被误判为不一致。
- Release 的普通 restore 与 win-x64 RID restore 均启用 `NuGetAuditMode=all`，并将 NU1901–NU1904 全部提升为错误；直接或传递依赖一旦出现任意等级的已知安全公告，正式发布会在构建前停止。
- 实时 Paraformer 模型目录中的旧 `silero_vad.onnx` Hugging Face 地址已开始返回 HTTP 401，会让新手引导的实时模型下载在最后一个文件失败；改用 sherpa-onnx 官方 `asr-models` Release 的公开稳定地址。官方 API 标注文件大小 643,854 bytes，SHA256 与目录现有 `9E2449…1FD6` 完全一致，因此无需改变模型内容或校验值。
- WPF 烟雾测试新增 Silero VAD 官方下载地址及固定 SHA256 断言，避免目录未来回退到失效仓库。
- 修复设置页和新手引导的静态语言事件泄漏：每个模型卡片此前订阅 `LocalizationManager.LanguageChanged` 后从未解除，反复打开设置会永久保留整套视图并在每次切换语言时重复刷新。模型卡片/列表、设置视图现实现显式释放，关闭页面时取消未完成下载并解除订阅；主窗口 ViewModel 关闭时也解除自身语言事件。
- 主窗口移除内嵌设置视图前会先解除 `SettingsSaved` / `CloseRequested` 事件并释放页面；程序退出时若设置页仍打开，也执行同样清理。

## 验证

- 5 个受影响 XAML 文件均通过 XML 解析。
- Release build：0 warning / 0 error。
- 自动化测试：74/74 通过（核心测试 73、应用烟雾测试 1）。
- 烟雾测试新增主工作台 → 内嵌设置 → 返回工作台的可见性与视图托管验证。
- 本地 publish 后实际启动确认：点击设置只切换主窗口内容，进程中不再出现独立 Settings 窗口。
- 本地化改造后 Release build 0 warning / 0 error、自动化测试 74/74 通过。
- Inno Setup 6.7.1 已实际读取仓库内 `ChineseSimplified.isl` 并成功生成双语安装器（55,641,994 bytes）。
- 本地化扩展后 Release build 继续保持 0 warning / 0 error，核心测试 73/73、应用烟雾测试 1/1 通过。
- 新增语言烟雾断言，覆盖语言选项自然语言显示、模型描述/模式/操作按钮即时切换及中英文资源加载。
- 新版已发布到 `publish/win-x64-localized`；共 55 个文件、265,071,541 bytes，扫描确认不含 `.onnx`、模型 `.bin`、`.pt`、`.pth`、`.safetensors` 或 `tokens.txt` 权重文件。
- 因 `publish/win-x64/MeetingTransfer.App.exe` 正在运行，未强制结束用户进程，也未覆盖该目录。
- 正式版审计确认 158 个中文资源键与 158 个英文资源键一一对应，7 个受影响 XAML 文件均可解析。
- 执行带 NuGet 漏洞审计的完整 restore 成功，未报告已知漏洞。
- 从干净的 `artifacts/publish` 重新发布：55 个文件、265,072,161 bytes；模型权重 0、用户运行数据 0。
- 使用 Inno Setup 6.7.1 重新生成 EchoMinutes 1.0.1 双语安装器，大小 55,637,805 bytes；同时生成 98,151,998 bytes 的便携 ZIP 和两份匹配的 SHA256 文件。
- 安全加固后新增 5 项回归测试：测试总数为 79/79（核心 75、WPF/更新器烟雾 4），Release build 继续保持 0 warning / 0 error。
- 非 XAML 文案收尾后中英文资源键为 161/161，无差异；烟雾测试增加默认说话人标签双向切换断言，79/79 继续通过。
- 完成非 XAML 双语收尾后使用正式版本号从干净目录发布并扫描：58 个文件、264,977,569 bytes，许可文件 9、PDB 0、模型权重 0、用户运行数据 0；最终安装器为 55,612,182 bytes，便携 ZIP 为 98,075,547 bytes，两份 SHA256 已同步更新并复核。
- 新增 .NET Desktop Runtime 检测后，Inno Setup 6.7.1 编译成功；安装器为 55,613,314 bytes，SHA256 为 `1235f1231888b26e6c3a4377a1771c1861c1db3767c9bd3a53ecc3595cfff548`，校验文件已同步。
- 语言显示复核构建发布到 `publish/win-x64-localized-review`：58 个文件，模型权重 0、PDB 0；完整测试 79/79 通过，中英文资源键 161/161 且无差异。
- 发布物复审确认便携 ZIP 共 60 个文件条目、展开后 264,977,569 bytes，模型权重 0、用户配置/数据 0、PDB 0、许可原文 9；ZIP 与安装器两份 SHA256 均匹配。窗口 ToolTip 本地化后完整测试仍为 79/79，中英文资源键为 164/164 且无差异，并重新生成 `publish/win-x64-localized-review`。
- 更新器失败路径加入中英文回归测试后，核心测试 75/75、WPF/更新器烟雾测试 6/6，总计 81/81；中英文资源键 165/165 且无差异，并再次生成最新 `publish/win-x64-localized-review`。
- Release YAML 通过解析校验；本地模拟确认 `v1.0.1` 可解析、非法及预发布格式会被拒绝，App/Core/Updater 均为 `1.0.1`，安装器 `ProductVersion.Trim()` 后为 `1.0.1`，全部版本门禁通过。
- 重新查询 NuGet 官方源确认当前 8 个项目均无已知漏洞；带 `NuGetAuditMode=all` 与 NU1901–NU1904 阻断参数的解决方案 restore 和 win-x64 RID restore 均实际通过。README 相对链接全部有效，未发现真实密钥、开发机密码/内网地址、临时补丁或缓存残留。
- 对模型目录执行实时网络复核：原始检查发现 Silero VAD 1 个地址返回 401，修复后 8 个模型、14 个文件全部为 HTTPS、全部具有 64 位 SHA256，14/14 地址跟随重定向后返回 HTTP 200；81/81 测试通过并重新生成 `publish/win-x64-localized-review`。
- 新增设置页释放回归断言：返回工作台后对旧模型卡片计数，再切换语言确认通知数保持 0；静态语言事件订阅/解除检查成对，81/81 测试继续通过并重新生成候选构建。
- 完成最终 GUI 视觉验收：复核中英文新手引导、模型下载与自动激活、三类模型分区、缺少模型提示、主工作台、无 speaker 状态、内嵌设置、更新页、窗口控制与版本号；并修复英文默认会议标题和 Whisper 多语言标签重复显示。
- 使用正式安装器实机复核安装模式、`English / 简体中文` 语言选择、英文安装路径页与 `EchoMinutes 1.0.1` 版本显示；随后取消安装，未改变系统安装状态。
- 最终正式产物复核：安装器 55,604,384 bytes，SHA256 为 `2bcfe17448866791f625023c4922686ca2ea369f30357454e813f4ac156c2bbf`；便携 ZIP 98,076,434 bytes，展开后 264,979,837 bytes，SHA256 为 `63975a6b2ca64f2b52a4fec581d782ea701673db5c8398ae6e8d792a8d84e766`。两份校验文件均匹配，ZIP 中模型权重 0、用户数据 0、PDB 0、第三方许可 9。
- 提交前清理 NAudio 与 SQLite 许可文本首尾多余空行；`git diff --cached --check` 通过，并确认暂存内容不含开发机地址、密码、私钥或 GitHub token。
- 准备发布 GitHub Release `v1.0.1`：确认 Release 工作流由正式版本标签触发，并会自动执行构建、测试、无模型/用户数据/PDB 门禁，随后上传安装器、便携 ZIP 及两份 SHA256 文件。

## 执行命令

```powershell
dotnet build MeetingTransfer.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet test tests/MeetingTransfer.App.SmokeTests/MeetingTransfer.App.SmokeTests.csproj -c Release --no-restore -p:NuGetAudit=false
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o publish/win-x64-localized
dotnet restore MeetingTransfer.sln
dotnet restore src/MeetingTransfer.App/MeetingTransfer.App.csproj -r win-x64 -p:NuGetAudit=false
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -o artifacts/publish
& 'C:\Users\C3EZ\AppData\Local\Temp\echo-minutes-inno\app\ISCC.exe' installer/EchoMinutes.iss
Compress-Archive -Path artifacts/publish/* -DestinationPath artifacts/echo-minutes-win-x64.zip -CompressionLevel Optimal
Get-FileHash artifacts/echo-minutes-setup-x64.exe -Algorithm SHA256
Get-FileHash artifacts/echo-minutes-win-x64.zip -Algorithm SHA256
dotnet test MeetingTransfer.sln -c Release --no-build --no-restore -p:NuGetAudit=false
dotnet list MeetingTransfer.sln package --include-transitive
artifacts\publish\Models\ffmpeg\bin\ffmpeg.exe -version
curl.exe -L --fail -o third_party\licenses\FFmpeg-GPL-3.0.txt https://raw.githubusercontent.com/FFmpeg/FFmpeg/c57660fb18/COPYING.GPLv3
curl.exe -L --fail -o third_party\licenses\whisper.cpp-MIT.txt https://raw.githubusercontent.com/ggml-org/whisper.cpp/master/LICENSE
curl.exe -L --fail -o third_party\licenses\sherpa-onnx-Apache-2.0.txt https://raw.githubusercontent.com/k2-fsa/sherpa-onnx/master/LICENSE
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:Version=1.0.1 -o artifacts/publish
dotnet test MeetingTransfer.sln -c Release --no-restore
dotnet publish src/MeetingTransfer.App/MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false --no-restore -o publish/win-x64-localized-review /p:VersionSuffix=dev
git diff --check
git status --short
git ls-files -v | Select-String '^S'
git tag -a v1.0.1 -m "EchoMinutes 1.0.1"
git push origin v1.0.1
Add-Type -AssemblyName System.IO.Compression.FileSystem
```
