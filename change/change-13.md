# Change Log

## 2026-07-10 - Make whisper.cpp language selectable and update model catalog downloads

### 任务目标
- 用户反馈：`zh` / `en` / `bilingual` 不应写死在推理参数里，应该能在 UI 中调节。
- 用户反馈：底层从 sherpa-onnx Whisper 换成 whisper.cpp Vulkan 后，Settings 里的 model list 以及对应下载也需要同步调整。

### 已执行指令
```powershell
Get-Content src\MeetingTransfer.App\SettingsWindow.xaml
Get-Content src\MeetingTransfer.App\SettingsWindow.xaml.cs
Get-Content src\MeetingTransfer.App\ViewModels\ModelsListViewModel.cs
Get-Content src\MeetingTransfer.Core\Models\ModelDescriptor.cs
Get-Content src\MeetingTransfer.Core\Models\catalog.json
Get-Content src\MeetingTransfer.Core\Models\ModelDownloader.cs
Get-Content tests\MeetingTransfer.Tests\ModelCatalogTests.cs
rg -n "WhisperCpp|WhisperEncoder|ActiveModelId|ArgumentsTemplate|BuildArguments|TryBuildManifest" src tests models.example.json change.md
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet run --project .tmp-settings-probe\Probe.csproj -c Release
cmd /c rmdir /s /q .tmp-settings-probe
.\publish\win-x64\models\whisper-cpp-vulkan\whisper-cli.exe -m .\publish\win-x64\models\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin -f .\publish\win-x64\models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\test_wavs\0.wav -l zh -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np
Move-Item -LiteralPath change.md -Destination change\change-12.md
```

### 变更记录
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxOptions.cs`
  - 新增 `WhisperCppLanguage`。
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs`
  - whisper.cpp 参数模板改为 `-l {Language}`。
  - `WhisperCppLanguage` 支持：
    - `zh` -> `-l zh`
    - `en` -> `-l en`
    - `bilingual` -> `-l zh`
  - `bilingual` 映射到 `zh` 是因为 whisper.cpp 只接受单语言 token；实测中英混合样本用 `zh` 比 `auto` 更完整。
  - 如果 active model 是 English-only（例如 `whisper-tiny.en`），无论 UI 语言选择如何，都强制 `-l en`。
  - 如果 ActiveModelId 是 `whisper-*`，优先使用 catalog 里选中的 GGML 模型文件；否则仍使用 configured fallback `WhisperCppModel`。
- `src\MeetingTransfer.App\SettingsWindow.xaml`
  - Tools tab 新增 `Offline language` ComboBox：
    - `Bilingual zh/en`
    - `Chinese zh`
    - `English en`
- `src\MeetingTransfer.App\SettingsWindow.xaml.cs`
  - 加载/保存 `WhisperCppLanguage`。
- `src\MeetingTransfer.App\Configuration\SettingsFileService.cs`
  - 默认模板迁移为 `-m "{Model}" -f "{InputWav}" -l {Language} -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np`。
  - 默认语言为 `bilingual`。
  - 旧 `whisper-large-v3-int8` / `whisper-large-v3-fp32` ActiveModelId 自动迁移为 `whisper-large-v3-turbo`。
  - 如果没有 active model 且内置 `ggml-large-v3-turbo.bin` 存在，自动设为 `whisper-large-v3-turbo`。
  - `WriteJson` 从 `File.Replace()` 改为先写 `.tmp` 再 `File.Move(..., overwrite: true)`，修复发布目录中 `models.json` 迁移时的 Windows 文件替换失败。
- `src\MeetingTransfer.Core\Models\catalog.json`
  - Whisper 模型下载项从 sherpa-onnx ONNX 三件套改为 whisper.cpp GGML 单文件：
    - `whisper-tiny.en` -> `ggml-tiny.en.bin`
    - `whisper-base` -> `ggml-base.bin`
    - `whisper-small` -> `ggml-small.bin`
    - `whisper-large-v3-turbo` -> `ggml-large-v3-turbo.bin`
  - Whisper executable 统一为 `models/whisper-cpp-vulkan/whisper-cli.exe`。
  - 保留 SenseVoice / Paraformer / Qwen3-ASR / streaming paraformer 的 sherpa-onnx entries。
- `src\MeetingTransfer.Core\Models\ModelCatalog.cs`
  - `models/whisper-cpp-vulkan/models/` 加为 bundled fallback root，让内置 `ggml-large-v3-turbo.bin` 在 Settings 模型列表里显示为已安装。
  - `.bin` 模型文件支持 `{Model}` 占位符替换。
- `models.example.json`
  - 新增 `WhisperCppLanguage: "bilingual"`。
  - whisper.cpp 参数模板改为 `{Language}`。
- `tests\MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs`
  - 覆盖 `bilingual -> -l zh`。
  - 覆盖 `en -> -l en`。
- `tests\MeetingTransfer.Tests\ModelCatalogTests.cs`
  - 覆盖 GGML `.bin` 的 `{Model}` 替换。
  - 覆盖内置 `models/whisper-cpp-vulkan/models` fallback。
- `change\change-12.md`
  - 归档上一轮 whisper.cpp Vulkan 安装/清理记录。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过。
  - 0 errors。
  - 有 `NU1900` warnings：当前环境无法访问 `https://api.nuget.org/v3/index.json` 的 vulnerability metadata。
- `dotnet test MeetingTransfer.sln -c Release --no-build` 通过：
  - 25 passed / 0 failed / 0 skipped。
- `dotnet publish ... -o publish\win-x64` 成功。
  - 同样有 `NU1900` warnings，原因同上。
- 临时探针直接调用 `SettingsFileService.Load()` 验证发布目录迁移成功：
  - `ActiveModelId = whisper-large-v3-turbo`
  - `WhisperCppArgumentsTemplate = -m "{Model}" -f "{InputWav}" -l {Language} -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np`
  - `WhisperCppLanguage = bilingual`
- 发布版真实 Vulkan 推理通过：
  - 枚举到 `AMD Radeon RX 7600M XT` 和 `AMD Radeon 780M`。
  - 输出：`昨天是monday.today is 礼拜二.the day after tomorrow 是星期三。`

### 说明
- UI 的 `Bilingual zh/en` 实际传给 whisper.cpp 的是 `-l zh`。这是当前 whisper.cpp CLI 单语言参数限制下对中英会议最稳的映射。
- 如果用户处理纯英文内容，可以在 Settings -> Tools -> Offline language 里改成 `English en`。
