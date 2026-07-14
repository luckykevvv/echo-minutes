# Change Log

## 2026-07-10 - Use whisper.cpp Vulkan for offline Whisper and clean old runtime bundles

### 任务目标
- 用户找到可直接使用的 Windows Vulkan 版 whisper.cpp：
  `https://github.com/jerryshell/whisper.cpp-windows-vulkan-bin`
- 目标是在本机 AMD GPU 上加速离线导入转写。
- 参考历史 `change.md` / `change\change-08.md` / `change\change-09.md` / `change\change-10.md`，在安装 whisper.cpp 后清理不再需要的依赖和旧模型。

### 已执行指令
```powershell
git status --short
git ls-files -v | Select-String '^S'
Get-Content change.md
rg -n "whisper-cpp|vulkan|third_party|sherpa-onnx-whisper|Hermes|hermes" change change.md src tests README.md models.example.json appsettings.example.json
Invoke-RestMethod -Uri 'https://api.github.com/repos/jerryshell/whisper.cpp-windows-vulkan-bin/releases/latest' | ConvertTo-Json -Depth 6
.\third_party\whisper-cpp-vulkan\whisper-cli.exe --help
Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin' -Method Head -UseBasicParsing
curl.exe -L --fail --progress-bar -o third_party\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin
Stop-Process -Id 35488 -Force
curl.exe -L --fail --progress-bar -C - -o third_party\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin
Get-Item third_party\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin | Select-Object FullName,Length
Get-FileHash third_party\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin -Algorithm SHA256
Remove-Item -LiteralPath ... -Recurse -Force
dotnet build MeetingTransfer.sln -c Release
dotnet test MeetingTransfer.sln -c Release --no-build
.\third_party\whisper-cpp-vulkan\whisper-cli.exe -m .\third_party\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin -f .\third_party\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\test_wavs\0.wav -l zh -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
Start-Process -FilePath '.\publish\win-x64\MeetingTransfer.App.exe' -WindowStyle Hidden -PassThru
.\publish\win-x64\models\whisper-cpp-vulkan\whisper-cli.exe -m .\publish\win-x64\models\whisper-cpp-vulkan\models\ggml-large-v3-turbo.bin -f .\publish\win-x64\models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\test_wavs\0.wav -l zh -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np
Move-Item -LiteralPath change.md -Destination change\change-11.md
```

### 变更记录
- `third_party\whisper-cpp-vulkan\`
  - 使用已解压的 Vulkan 版 whisper.cpp runtime。
  - 新增 `models\ggml-large-v3-turbo.bin`。
  - 模型大小：`1,624,555,275` bytes。
  - SHA256：`1FC70F774D38EB169993AC391EEA357EF47C88757EF72EE5943879B7E8E2BC69`。
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxOptions.cs`
  - 新增 `WhisperCppExecutable` / `WhisperCppModel` / `WhisperCppArgumentsTemplate`。
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs`
  - `TranscribeFileAsync` 在未选择非 Whisper manifest 模型时优先走 whisper.cpp。
  - 默认参数：
    `-m "{Model}" -f "{InputWav}" -l zh -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np`
  - 加入 whisper.cpp 输出清洗，过滤 `ggml_vulkan` / `whisper_*` 日志、timestamp 前缀和贴在文本后的 timing。
  - `ActiveModelId` 非空且不是 `whisper-*` 时仍走原 manifest 路径，避免覆盖 SenseVoice / Paraformer / Qwen3-ASR 选择。
- `src\MeetingTransfer.App\Configuration\SettingsFileService.cs`
  - 启动时自动补齐 whisper.cpp Vulkan 路径和参数。
  - 如果旧 `models/sherpa-onnx-whisper/...` 路径已经不存在，则自动置空 `WhisperEncoder` / `WhisperDecoder` / `WhisperTokens`。
- `src\MeetingTransfer.App\MeetingTransfer.App.csproj`
  - 新增 `third_party\whisper-cpp-vulkan\**\*` 发布映射到 `models\whisper-cpp-vulkan\...`。
  - 删除 `third_party\sherpa-onnx-whisper\**\*` 发布映射。
- `models.example.json`
  - 新增 whisper.cpp Vulkan 默认配置。
- `tests\MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs`
  - 新增 whisper.cpp 请求构造测试。
  - 新增 whisper.cpp 输出解析测试。
- `change\change-11.md`
  - 归档上一轮 `change.md`。

### 清理记录
- 已删除：
  - `third_party\whisper-cpp\`（旧 CPU whisper.cpp runtime）
  - `third_party\whisper-cpp-src\`（源码树）
  - `third_party\sherpa-onnx-whisper\`（旧内置 sherpa Whisper ONNX 模型）
  - `third_party\downloads\`（下载缓存）
  - `publish\win-x64\models\sherpa-onnx-whisper\`（旧发布模型）
- 已保留：
  - `third_party\sherpa-onnx\`：实时录音仍依赖 `sherpa-onnx-vad-with-online-asr.exe` 和 streaming paraformer。
  - `third_party\ffmpeg\`：导入音视频和切片仍需要。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - 0 warnings / 0 errors
- `dotnet test MeetingTransfer.sln -c Release --no-build` 通过：
  - 22 passed / 0 failed / 0 skipped
- `dotnet publish ... -o publish\win-x64` 成功。
- 发布版启动成功并自动迁移 `publish\win-x64\models.json`：
  - `WhisperEncoder` / `WhisperDecoder` / `WhisperTokens` 已置空。
  - `WhisperCppExecutable` / `WhisperCppModel` / `WhisperCppArgumentsTemplate` 已写入。
- 发布版真实 GPU 推理通过：
  - 命令使用 `publish\win-x64\models\whisper-cpp-vulkan\whisper-cli.exe`
  - Vulkan 设备枚举到：
    - `AMD Radeon RX 7600M XT`
    - `AMD Radeon 780M`
  - 输出：
    `昨天是monday.today is 礼拜二.the day after tomorrow 是星期三。`

### 关键发现
- 默认 `beam 5 / best-of 5` 在本机 Vulkan 解码阶段会无错误退出，exit code 为 1。
- 加上 `-nfa -bs 1 -bo 1` 后 Vulkan 路径稳定成功。
- `-l auto` 对中英混合测试样本会误判为英文并漏掉开头中文，因此默认改成 `-l zh`。用户如需英文优先，可在 `models.json` 里把 `-l zh` 改回 `-l auto` 或 `-l en`。
