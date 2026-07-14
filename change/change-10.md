# Change Log

## 2026-07-10 - Fix model manifest routing and active model state bugs

### 任务目标
- 检查并修复参考 `change/` 历史改动后可能存在的 bug。
- 重点复查最近新增的模型 catalog / manifest 选模型、Whisper 长音频切片、设置页默认模型状态逻辑。

### 已执行指令
```powershell
Get-ChildItem -Force
git status --short
if (Test-Path change.md) { Get-Content -Raw change.md } else { 'NO change.md' }
if (Test-Path change) { Get-ChildItem -Force change } else { 'NO change dir' }
rg --files
Get-Content -Raw README.md
Get-Content -Raw change\change-08.md
Get-Content -Raw change\change-07.md
Get-Content -Raw src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs
Get-Content -Raw src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxOptions.cs
Get-Content -Raw tests\MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs
Get-Content -Raw src\MeetingTransfer.App\Configuration\SettingsFileService.cs
Get-Content -Raw src\MeetingTransfer.App\Configuration\RuntimeSettings.cs
Get-Content -Raw src\MeetingTransfer.App\Configuration\ModelsFile.cs
Get-Content -Raw src\MeetingTransfer.Core\Models\catalog.json
Get-Content -Raw src\MeetingTransfer.Core\Import\MediaImportService.cs
Get-Content -Raw src\MeetingTransfer.Core\Models\ModelDownloader.cs
Get-Content -Raw src\MeetingTransfer.Core\Models\ModelDescriptor.cs
Get-Content -Raw src\MeetingTransfer.Core\Models\ModelCatalog.cs
Get-Content -Raw tests\MeetingTransfer.Tests\ModelCatalogTests.cs
rg -n "ActiveModelId|Models|ModelDownloader|TryBuildManifestRequest|ArgumentsTemplate|BuildManifestArguments" src tests
Get-Content -Raw src\MeetingTransfer.App\ViewModels\ModelsListViewModel.cs
Get-Content -Raw src\MeetingTransfer.App\SettingsWindow.xaml.cs
Get-Content -Raw src\MeetingTransfer.App\MeetingTransfer.App.csproj
Get-Content -Raw models.example.json
dotnet test MeetingTransfer.sln -c Release
Get-Content -Raw change\change-08.md.bak
Get-Content -Raw src\MeetingTransfer.App\SettingsWindow.xaml
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
rg -n "ReplaceFilePlaceholders|GetPlaceholderAliases|BuildArguments_ReplacesCommon|LoadPreserves|Models = models|wasActive|ManifestArgumentsReplace" src tests
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-3)} | Where-Object { $_.ProviderName -in @('.NET Runtime','Application Error') -and ($_.Message -match 'MeetingTransfer|Meeting_Transfer|SettingsWindow|XamlParseException') } | Select-Object -First 8 TimeCreated,ProviderName,Id,Message | Format-List
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
# 临时 WPF 探针：引用 publish\win-x64\MeetingTransfer.App.dll，调用 SettingsWindow.ShowDialog()，500ms 后自动 Close()
dotnet run --project $env:TEMP\mt-settings-showdialog-probe\Probe.csproj -c Release
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-5)} | Where-Object { $_.ProviderName -in @('.NET Runtime','Application Error') -and ($_.Message -match 'MeetingTransfer|Meeting_Transfer|SettingsWindow|XamlParseException') } | Select-Object TimeCreated,ProviderName,Id,Message | Format-List
```

### 修复内容
- `src\MeetingTransfer.App\SettingsWindow.xaml`
  - 修复进入设置页闪退：模型卡片内 `<Run Text="{Binding SizeDisplay}">` 等 inline 绑定在 WPF 中会按 TwoWay 处理，目标属性只读时渲染阶段抛 `XamlParseException`。
  - 现在对 `SizeDisplay` / `LanguagesDisplay` / `ExecutionMode` 显式设置 `Mode=OneWay`。
- `src\MeetingTransfer.App\ViewModels\MainWindowViewModel.cs`
  - `OpenSettingsAsync()` 增加异常保护；以后设置窗口打开失败会弹出错误并更新状态栏，不再直接让整个 App 进程退出。
- `src\MeetingTransfer.Core\Models\ModelCatalog.cs`
  - 修复 catalog 参数模板替换：现在支持 `{Encoder}`、`{Decoder}`、`{Tokens}`、`{Model}`、`{ConvFrontend}`、`{SileroVadModel}` 这些通用占位符。
  - 之前只按文件 stem 替换，`large-v3-encoder.int8.onnx` 只能匹配 `{large-v3-encoder.int8}`，无法替换 catalog 中实际使用的 `{Encoder}`，导致选中 manifest 模型后 CLI 参数仍包含未替换占位符。
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs`
  - 同步修复 manifest transcription 路径里的参数模板替换逻辑，避免 `TryBuildManifestRequest` 构造出带 `{Encoder}` / `{Tokens}` 的无效参数。
- `src\MeetingTransfer.App\Configuration\SettingsFileService.cs`
  - `Load()` 现在把读取到的 `ModelsFile` 回填到 `RuntimeSettings.Models`。
  - 之前只把 `ActiveModelId` 写进 `SherpaOnnxOptions.ActiveModelId`，设置页用 `_settings.Models.ActiveModelId` 初始化时会拿到空值，导致默认模型状态显示/保存不稳定。
- `src\MeetingTransfer.App\ViewModels\ModelsListViewModel.cs`
  - 删除模型时先记录 `wasActive`，再删除文件和更新状态。
  - 之前先把 `State` 改成 `NotInstalled` 再判断 `IsActive`，删除当前默认模型时不会清空 `ActiveModelId`。
- `tests\MeetingTransfer.Tests\ModelCatalogTests.cs`
  - 新增 versioned Whisper 文件名、`{Model}` / `{ConvFrontend}` alias 的回归测试。
- `tests\MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs`
  - 新增 `BuildManifestArguments` 对 versioned Whisper 文件名的回归测试。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过。
  - 0 errors。
  - 0 warnings。
- `dotnet test MeetingTransfer.sln -c Release --no-build` 通过。
  - 18 passed / 0 failed / 0 skipped。
- `dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64` 通过。
- 临时 WPF 探针验证发布目录里的 `SettingsWindow.ShowDialog()` 可正常打开并关闭：
  - 输出 `Closing SettingsWindow`
  - 输出 `ShowDialog returned: False`
- 最近 5 分钟 Windows Application 事件日志中没有新的 MeetingTransfer `.NET Runtime` / `Application Error` 记录。

### 说明
- 本轮已重新 publish `publish\win-x64`。
- 当前仓库没有根目录旧 `change.md`，因此没有归档旧文件；历史记录仍保留在 `change\change-01.md` 到 `change\change-08.md`。
