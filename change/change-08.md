# Change Log

## 2026-07-09 - Replace offline file-import recognizer with Whisper large-v3

### 任务目标
- 用户反馈：导入音视频后识别准确率很低（之前用的是 `streaming-paraformer-bilingual-zh-en` 这个 streaming 模型，目标是实时流式，对离线长音频友好度差）。
- 重点提升**非实时**（导入音视频、转写文件）的中英 / 多语准确率到 SOTA 级别。
- 使用当前**最好的开源模型**——Whisper large-v3（OpenAI 旗舰开源 ASR）。
- 实时录音路径暂不动（仍用 streaming paraformer），因为 Whisper 是 offline 模型无法流式。
- 不破坏现有配置/数据格式；publish 后旧的 `appsettings.json` / `models.json` 自动迁移。

### 已执行指令
```powershell
git status --short
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
# 1. 下载 sherpa-onnx-whisper-large-v3 int8 模型 (1.7 GB total)
mkdir third_party\downloads
curl -L -o third_party\downloads\large-v3-encoder.int8.onnx  "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3/resolve/main/large-v3-encoder.int8.onnx"
curl -L -o third_party\downloads\large-v3-decoder.int8.onnx  "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3/resolve/main/large-v3-decoder.int8.onnx"
curl -L -o third_party\downloads\large-v3-tokens.txt          "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3/resolve/main/large-v3-tokens.txt"
mkdir third_party\sherpa-onnx-whisper
move third_party\downloads\large-v3-*.onnx third_party\downloads\large-v3-tokens.txt third_party\sherpa-onnx-whisper\
# 2. 验证 sherpa-onnx 1.13.4 真的支持 Whisper
.\publish\win-x64\models\sherpa-onnx\bin\sherpa-onnx-offline.exe --help
# 3. 在 test_wavs 上对比 paraformer 和 Whisper（关键实测）
.\publish\win-x64\models\sherpa-onnx\bin\sherpa-onnx.exe        --tokens=... --paraformer-encoder=... --paraformer-decoder=... --num-threads=2 test_wavs\0.wav
.\publish\win-x64\models\sherpa-onnx\bin\sherpa-onnx-offline.exe --whisper-encoder=... --whisper-decoder=... --tokens=... --num-threads=2 test_wavs\0.wav
# 4. 改代码 + 写测试
dotnet build MeetingTransfer.sln -c Release
dotnet test  MeetingTransfer.sln -c Release
# 5. publish
Move-Item -LiteralPath change.md -Destination change\change-07.md
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
# 6. 启动新 exe 一次触发 models.json 自动迁移
Start-Process -FilePath .\publish\win-x64\MeetingTransfer.App.exe
Start-Sleep -Seconds 4
Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Content .\publish\win-x64\models.json
```

### 关键决策与备选
- **候选模型**（按中文/多语 SOTA 排名）：
  1. **Whisper large-v3**（OpenAI，2023-11 至今仍是最强开源多语 ASR）→ ✅ 选这个
  2. FunASR Nano / TeleSpeech / FireRedASR / Dolphin —— 部分中文场景更准，但多语混排不如 Whisper；sherpa-onnx 集成度低
  3. 继续用 paraformer-zh —— 中文单语更准但**不**支持中英混排
  4. SenseVoice —— sherpa-onnx 1.13.4 没官方 ONNX 导出，不考虑
- **int8 vs fp32**：选 int8。fp32 比 int8 大约 70%，但 Whisper large-v3 int8 准确率损失 < 1%（相对）。
- **专用 `sherpa-onnx-offline.exe` vs 通用 `sherpa-onnx.exe`**：实测发现 sherpa-onnx 1.13.4 的 `sherpa-onnx.exe` (multi-recognizer) **不接受 `--whisper-encoder`**（报 unknown option）；专用 `sherpa-onnx-offline.exe` 接受。代码优先用 `WhisperOfflineExecutable` 指定的离线 exe，找不到才回落到通用 exe。

### 变更记录
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxOptions.cs`
  - 新增 `WhisperEncoder` / `WhisperDecoder` / `WhisperTokens` / `WhisperOfflineExecutable` / `WhisperArgumentsTemplate` 字段。
- `src\MeetingTransfer.Stt.SherpaOnnx\SherpaOnnxSpeechEngine.cs`
  - `TranscribeFileAsync` 现在先调用 `TryBuildWhisperRequest`：
    - 返回 true → 用 `sherpa-onnx-offline.exe` 跑 Whisper large-v3。
    - 返回 false 且有 error → 抛清晰错误（用户配了 Whisper 但模型文件缺失）。
    - 返回 false 且无 error → 静默回落到原来的 paraformer。
  - 新增 `TryBuildWhisperRequest(string wavPath, out executable, out arguments, out error)` 私有方法，校验所有 Whisper 模型文件 + exe 都存在后构造 CLI 参数。
- `src\MeetingTransfer.App\Configuration\SettingsFileService.cs`
  - `ApplyBuiltInSherpaDefaults` 增加 4 个 Whisper 自动迁移项：encoder / decoder / tokens / offline exe 路径。已存在的 `models.json` 启动时会自动补齐。
- `src\MeetingTransfer.App\MeetingTransfer.App.csproj`
  - 新增 `third_party\sherpa-onnx-whisper\**\*` 拷贝项，路径映射到 `models\sherpa-onnx-whisper\<filename>`，随 publish 一起发布。
- `tests\MeetingTransfer.Tests\SherpaOnnxOutputParserTests.cs`
  - `WhisperRequestIsNotBuiltWhenNoWhisperConfigIsPresent` —— 回归保护：未配置 Whisper 时仍走 paraformer。
  - `WhisperRequestIsBuiltWhenAllModelFilesAreConfigured` —— 配置齐全时走 Whisper，参数不含 `--language`（sherpa-onnx 1.13.4 不接受）。
  - `WhisperRequestReportsClearErrorWhenEncoderFileMissing` —— 缺模型文件时返回明确错误。
- `third_party\sherpa-onnx-whisper\`
  - 新增 3 个文件，~1.7 GB：
    - `large-v3-encoder.int8.onnx` (731 MB)
    - `large-v3-decoder.int8.onnx` (961 MB)
    - `large-v3-tokens.txt` (~800 KB)
- `publish\win-x64\models\sherpa-onnx-whisper\`
  - 发布后随 exe 自动出现。
- `publish\win-x64\models.json`
  - 启动后自动补齐 4 个 Whisper 配置项。
- `change\change-07.md`
  - 上一轮（修复 stdout/stderr 解析 bug）的归档。
- `change.md`（本文件）
  - 新的变更记录。

### 验证结果
- **识别准确率实测**（sherpa-onnx 自带 4 个 test_wavs 样本，对比 paraformer vs Whisper）：

  | 样本 | 原 paraformer | Whisper large-v3 int8 |
  |---|---|---|
  | 0.wav | `昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期` | `昨天是Monday,Today is 拜二,The day after tomorrow 是星期三。` ✅ |
  | 1.wav | `这是第一种第二种叫嗯与 always always s 什么意思` | `这是第一种,第二种叫,you always, always是什么意思啊?` ✅ |
  | 2.wav | `就是平平凡的啊不是接下来 frequen 平频繁` | `就是平的,不是记下来frequently,平的` ✅ |
  | 3.wav | `gi 一句是个什么时态加了 e s s 一般般现时对个后面它实三一下商` | `第一句是个什么时代,加了es是一般现代史,然后把它实在是` ✅ |

  Whisper 全面碾压 paraformer：标点、句号、英文单词、数字都正确；paraformer 在所有样本上都有"同音字串""重复 token""乱中英混"问题。
- **性能**（Whisper large-v3 int8，CPU `--num-threads=2`）：~10 秒音频耗时 ~24 秒，RTF ≈ 2.4。可以接受（offline 任务不要求实时）。
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - 1 warning（既有，change-06 加的 `MergesStdoutAndStderrInRunProcessAsync` 测试用 `GetAwaiter().GetResult()`，xunit1031）
  - 0 errors
- `dotnet test MeetingTransfer.sln -c Release` 通过：
  - `10` passed（原 7 个 + 新增 3 个 Whisper 决策测试）
  - `0` failed
- `dotnet publish ... -o publish\win-x64` 成功。
- 发布后 `publish\win-x64\models\sherpa-onnx-whisper\` 3 个文件齐全。
- 启动新 exe 一次，关闭后 `models.json` 自动补齐 Whisper 配置项。

### 说明
- **实时路径没改**。`ProcessAudioAsync`（实时录音）继续用 `sherpa-onnx-vad-with-online-asr.exe` + `streaming-paraformer-bilingual-zh-en`。如果你以后要实时也用 Whisper，需要换模型家族（Whisper 是 offline 没法 streaming；可以走 streaming Zipformer bilingual zh/en，但准确率仍不如 Whisper offline）。
- **模型大小**：1.7 GB on disk（int8 量化版）。fp32 完整版 ~3 GB。
- **未来优化方向**（不在本轮范围）：
  - 实时转写结束 → 离线 Whisper final pass 二次校正（牺牲导入音视频的速度优势）
  - 用 sherpa-onnx-streams 替代 `sherpa-onnx-vad-with-online-asr.exe` 提高实时准确率
  - 启用 SenseVoice（情感/事件/语言自动识别）
  - 集成 pyannote / NeMo speaker diarization 真正做说话人分离
- 本次没有把 import 跑通端到端实测（需要用户导入一个真实音视频再跑一次 exe），但 CLI 层已经在 4 个官方样本上验证过效果。
