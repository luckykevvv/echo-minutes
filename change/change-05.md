# Change Log

## 2026-07-09 - Bundling built-in ffmpeg into the app

### 任务目标
- 用户要求 "不管是什么应该都设置好才对，都是内置的，不需要我再做任何操作就该能够使用"。
- 上一轮已经把 sherpa-onnx runtime + 模型内置并发布，本轮继续把 ffmpeg 也内置。
- ffmpeg 默认随应用一起打包，无需用户在 Settings 里手动配置。
- 自动迁移已存在的旧 appsettings.json（指向不存在的外置 ffmpeg）。
- 重新编译、测试并发布 Windows exe。

### 已执行指令
```powershell
Invoke-WebRequest -Uri "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip" -OutFile "third_party\downloads\ffmpeg-btbn.zip" -UseBasicParsing
Expand-Archive -Path third_party\downloads\ffmpeg-btbn.zip -DestinationPath third_party\downloads\ffmpeg-btbn -Force
Copy-Item -Path "third_party\downloads\ffmpeg-btbn\*\bin" -Destination "third_party\ffmpeg/bin" -Recurse -Force
Copy-Item -Path "third_party\downloads\ffmpeg-btbn\*\presets" -Destination "third_party\ffmpeg/presets" -Recurse -Force
Remove-Item third_party\ffmpeg\bin\ffplay.exe
Remove-Item third_party\downloads\ffmpeg-btbn.zip
Remove-Item -Recurse third_party\downloads\ffmpeg-btbn
& third_party\ffmpeg\bin\ffmpeg.exe -hide_banner -version
# 通过 base64+Node 脚本写入（PowerShell 会吞掉 %(...) 等特殊 token）多文件变更:
#   - src/MeetingTransfer.App/MeetingTransfer.App.csproj
#   - src/MeetingTransfer.App/Configuration/SettingsFileService.cs
#   - src/MeetingTransfer.Core/Import/MediaImportService.cs
#   - src/MeetingTransfer.App/ViewModels/MainWindowViewModel.cs
#   - appsettings.example.json
Move-Item -LiteralPath change.md -Destination change\change-04.md
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
& publish\win-x64\models\ffmpeg\bin\ffmpeg.exe -hide_banner -version
# 启动 exe 一次以触发 appsettings.json 自动迁移到内置路径
Start-Process -FilePath publish\win-x64\MeetingTransfer.App.exe
Start-Sleep -Seconds 4
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Content publish\win-x64\appsettings.json
```

### 变更记录
- 下载 BtbN FFmpeg-Builds "win64-gpl-shared" 最新版（2026-07-08 built）。
- 抽出 `bin/*.dll|*.exe` 与 `presets/`，删除 `ffplay.exe`，放入 `third_party\ffmpeg\`。
- `MeetingTransfer.App.csproj` 新增 `third_party\ffmpeg\**\*` → `models\ffmpeg\...` 的 Content 项，自动随 publish 一起发布。
- `MediaImportService`：
  - 新增 `BuiltInFfmpegRelativePath` 常量 (`models/ffmpeg/bin/ffmpeg.exe`)。
  - 新增 `ResolveFfmpegPath(string? configuredPath, string? baseDirectory = null)`：先看绝对/相对配置项，再回落到内置。
  - `ExtractAudioAsync` 现在接受 `ffmpegPath` 为 `null` 或相对路径（相对 exe 解析），找不到时抛错并提示去 Settings > ffmpeg.exe。
- `SettingsFileService.Load()`：
  - 新增 `ApplyBuiltInFfmpegDefault(ImportOptions)`：若内置 ffmpeg.exe 存在且 `FfmpegPath` 为空/不存在，把 `FfmpegPath` 改写为 `models/ffmpeg/bin/ffmpeg.exe` 并自动 Save()。
  - 这样旧配置（指向不存在的 `C:/Tools/ffmpeg/...`）下次启动时被静默迁到内置路径。
- `MainWindowViewModel.ImportAsync()`：
  - 去掉显式的 `IsNullOrWhiteSpace` 抛错，依赖 `MediaImportService` 自动解析。
- `appsettings.example.json`：
  - 默认 `FfmpegPath` 改为 `models/ffmpeg/bin/ffmpeg.exe`。
- 发布目录新增内容：
  - `models/ffmpeg/bin/ffmpeg.exe` + 7 个 dll（avcodec/avdevice/avfilter/avformat/avutil/swresample/swscale）和 `ffprobe.exe`。
  - `models/ffmpeg/presets/*.ffpreset`。
  - 总大小约 170 MB（其中 avcodec-63.dll 98 MB 是必须的）。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - `0` warnings
  - `0` errors
- `dotnet test MeetingTransfer.sln --no-build` 通过：
  - `3` passed
  - `0` failed
- `dotnet publish` 成功，发布目录新增 `models/ffmpeg/`。
- 内置 `ffmpeg.exe -hide_banner -version` 输出完整版本、配置、库版本（libavutil 61 / libavcodec 63 / libavformat 63 等）。
- 删除已发布的 `appsettings.json` 后再次启动应用：
  - 应用自动写出 `appsettings.json`，`Import.FfmpegPath` 自动迁移为 `models/ffmpeg/bin/ffmpeg.exe`。
  - 该内置 ffmpeg.exe 在磁盘上存在。
- 旧的 `C:/Tools/ffmpeg/bin/ffmpeg.exe`（不存在的外置配置）会在下次启动时自动被覆盖，不会再用。

### 已知风险 / 后续
- ffmpeg 自带配置启用 GPL + 非自由编解码（libfdk-aac 被 `--disable-libfdk-aac`，其余全开）。非 GPL 用户需要手动替换为 LGPL 构建。
- 发布目录因为 ffmpeg 二进制从 7 MB → 170 MB。如果以后要瘦身，可以只保留 `bin/*.exe + avcodec/avformat/avutil/swresample` 这 5 个 DLL（需要测试 mp3/aac 仍能解码）。