# Change Log

## 2026-07-09 - 移除 Mock 引擎并修复设置保存反馈

### 任务目标
- 移除 Mock STT 引擎和 GUI 中的 Mock 选项。
- 应用固定使用 `SherpaOnnx` 引擎。
- 修复/增强设置保存：保存失败时在 GUI 中显示明确错误，保存成功后写入 `appsettings.json` 和 `models.json`。
- 重新编译、测试并发布 Windows exe。

### 已执行指令
```powershell
Get-ChildItem change -Force | Select-Object Name
Get-Content -Raw change.md
rg -n "Mock|SherpaOnnx|SettingsFileService|EngineBox|Speech|Engine|appsettings|models" src tests appsettings.example.json models.example.json README.md
Get-Content -Raw src\MeetingTransfer.App\Configuration\SettingsFileService.cs
Get-Content -Raw src\MeetingTransfer.App\SettingsWindow.xaml.cs
Get-Content -Raw src\MeetingTransfer.App\SettingsWindow.xaml
Move-Item -LiteralPath change.md -Destination change\change-02.md
dotnet build MeetingTransfer.sln
dotnet test MeetingTransfer.sln --no-build
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-Process -Id 27904 -ErrorAction SilentlyContinue | Select-Object ProcessName,Id,Path | Format-List
Get-Process -Id 27904 -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-Content -Raw publish\win-x64\appsettings.json
rg -n "Mock|MockSpeechEngine|EngineBox|built-in mock|STT engine" src tests README.md appsettings.example.json publish\win-x64\appsettings.example.json publish\win-x64\appsettings.json
```

### 变更记录
- 已移除 `MockSpeechEngine` 和对应测试。
- 默认配置从 `Mock` 改为 `SherpaOnnx`。
- 主运行路径不再按配置 fallback 到 Mock，固定创建 `SherpaOnnxSpeechEngine`。
- 设置窗口移除引擎下拉框，固定显示 `SherpaOnnx`。
- 设置保存时强制写入 `Speech.Engine = "SherpaOnnx"`。
- 设置保存增加异常弹窗，保存失败时不再静默失败。
- `SettingsFileService.Load()` 会把旧配置中的 `Mock` 规范为 `SherpaOnnx`。
- `SettingsFileService.Save()` 会先清除只读属性，并使用临时文件替换方式写入 JSON。
- 更新 `README.md`，去掉 mock engine 说明。
- 发布目录已有旧 `appsettings.json`，其中 `Mock` 已改为 `SherpaOnnx`。

### 验证结果
- `dotnet build MeetingTransfer.sln` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln --no-build` 通过：
  - `3` passed
  - `0` failed
- 第一次 `dotnet publish` 失败，因为旧版 `MeetingTransfer.App (27904)` 正在运行并锁定 `publish/win-x64` 中的 DLL。
- 已关闭旧进程并重新发布成功。
- `rg` 检查确认 `src`、`tests`、`README.md`、`appsettings.example.json`、发布目录配置中没有 `Mock` 残留。
