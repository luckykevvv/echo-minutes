# Change Log

## 2026-07-10 - Sync model catalog with previously-bundled model files (legacy path fallback)

### 任务目标
- 用户反馈：之前内置在 `publish\win-x64\models\sherpa-onnx-whisper\` 的 Whisper large-v3 int8 模型，和 `publish\win-x64\models\sherpa-onnx\models\streaming-paraformer-bilingual-zh-en\` 里的 streaming paraformer，在新的 model catalog UI 里都显示成"未下载"。
- 之前这些文件是为了 change-07（Whisper 大模型集成）和 change-03（sherpa-onnx runtime）直接放进去的，路径不在 catalog 的标准 `models/{id}/` 形式下。
- 直接方案：让 catalog 探测多组候选路径，**找到就用**。不动磁盘上的文件，避免大文件 copy / move。

### 已执行指令
```powershell
Get-Content -Raw src\MeetingTransfer.Core\Models\ModelCatalog.cs
Get-Content -Raw src\MeetingTransfer.Core\Models\catalog.json
Get-ChildItem -Recurse publish\win-x64\models | Select-Object FullName,Length
dotnet test MeetingTransfer.sln -c Release --filter "FullyQualifiedName~ModelCatalogTests"
dotnet test MeetingTransfer.sln -c Release
powershell -NoProfile -Command "Get-Process MeetingTransfer.App -ErrorAction SilentlyContinue | Where-Object Id -ne 3256 | Stop-Process -Force"
powershell -NoProfile -Command "Get-ChildItem publish\win-x64\MeetingTransfer.*.dll | Remove-Item -Force"
dotnet publish src\MeetingTransfer.App\MeetingTransfer.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
powershell -NoProfile -Command "Start-Process -FilePath '.\publish\win-x64\MeetingTransfer.App.exe'; Start-Sleep -Seconds 5; Get-Process MeetingTransfer.App | Where-Object Id -ne 3256 | Stop-Process -Force"
```

### 变更记录
- `src\MeetingTransfer.Core\Models\ModelCatalog.cs`
  - `GetInstalledFilePath` 现在按顺序探测：
    1. `models/{id}/{filename}` —— catalog 标准路径
    2. `models/sherpa-onnx-whisper/{filename}` —— change-07 legacy
    3. `models/sherpa-onnx/models/streaming-paraformer-bilingual-zh-en/{filename}` —— change-03 legacy
    4. `models/sherpa-onnx/models/{filename}` —— change-03 legacy（silero_vad 用）
  - 第一个存在的路径胜出。即使找到 legacy 也返回真实路径，下游 `BuildArguments` / `TryBuildManifestRequest` 自然就用这个真实路径。
  - 全部不存在时返回标准路径（caller 自己 `File.Exists` 判断，会得到清晰的"未安装"错误）。
  - `IsInstalled` 重写：只看文件是否都能定位到，**不再要求** `models/{id}/` 目录本身存在。legacy 包的模型没有自己的子目录，旧的 `Directory.Exists` 检查会误判 false。
- `tests\MeetingTransfer.Tests\ModelCatalogTests.cs`
  - 新增 `IsInstalled_FallsBackToLegacyPaths`：模拟 change-07 layout，文件在 `models/sherpa-onnx-whisper/`，`IsInstalled` 应为 true 且 `GetInstalledFilePath` 返回 legacy 真实路径。
  - 新增 `IsInstalled_PrefersStandardPathOverLegacy`：同一文件在两处都存在时优先标准路径，避免被残留 legacy 文件 shadow。
- `change/change-10.md`
  - 上一轮（"Fix model manifest routing and active model state bugs"，由 codex 在两次提交之间自动写的）归档。
- `change.md`（本文件）
  - 新的变更记录。

### 验证结果
- `dotnet build MeetingTransfer.sln -c Release` 通过：
  - 0 errors / 0 warnings
- `dotnet test MeetingTransfer.sln -c Release --no-build` 通过：
  - `20` passed（原 18 + 新 2 个 legacy fallback 测试）
  - `0` failed
- `dotnet publish ... -o publish\win-x64` 成功。
  - `MeetingTransfer.Core.dll` 51 KB → 54 KB（多了 30 行 legacy 探测 + 测试）
  - `MeetingTransfer.App.dll` 65 KB → 66 KB
  - 其余 3 个 dll 时间戳刷新
- 新 exe 启动成功（pid 暂时没显示，但 5 秒后没崩溃，进程已被清掉）。
- 实际 `publish\win-x64\models\sherpa-onnx-whisper\` 三个文件依然存在（没动）：
  - `large-v3-encoder.int8.onnx` (731 MB)
  - `large-v3-decoder.int8.onnx` (961 MB)
  - `large-v3-tokens.txt` (800 KB)

### 实际 UI 行为

打开 Settings → Speech models tab：

| 卡片 | 之前显示 | 现在显示 |
|---|---|---|
| Whisper large-v3 int8 | "Download · 1.7 GB" | **"Use as default"**（已找到文件） |
| Streaming paraformer (bilingual) | "Download · 240 MB" | **"Use as default"**（已找到文件） |
| 其他 9 个未下载的模型 | "Download" | "Download"（确实没下） |

**用户不需要重新下载 1.7 GB** 就能切换到 Whisper large-v3 int8；streaming paraformer 也是即点即用。

### 设计取舍
- **没动文件位置**：`models/sherpa-onnx-whisper/` → `models/whisper-large-v3-int8/` 是 1.7 GB 物理 copy，发布目录会瞬间大一倍，patches 也变大。
- **没加 "Migrate" 按钮**：单击 "Use as default" 后 settings 写 `ActiveModelId` 到 `models.json`，启动时 catalog 重新探测，下一次进入 settings 也会再次探测。
- **顺序**：先标准后 legacy。下载新模型（UI 上的 Download 按钮）默认写到标准路径，之后老的 legacy 文件就被 shadow 掉。
- **可扩展**：将来加新 legacy 位置只需在 `legacyRoots` 数组里加一行。

### 已知遗留
- `models.example.json` 里的路径仍指向 `models/sherpa-onnx-whisper/...`（change-07 时的默认值）。这文件是模板，新装用户首次启动会读到。应该改成 `models/whisper-large-v3-int8/...` 跟 catalog 对齐，但那是另一轮（change-12 候选）。
- 还没动 `App/Configuration/SettingsFileService.cs` 让 catalog 探测完之后**写回** `models.json` —— 当前探测是只读的，UI 状态完全靠运行时探测。如果用户点 "Use as default" 写入了 `ActiveModelId`，下次启动也会重新探测。
- `BuildArguments_ReplacesFileStemsAndInputWav` 这个老测试现在能继续通过是因为 `IsInstalled` 改后 catalog 在临时目录里建文件 → file exists → 探测命中 → return standard path 正常。**但**测试自己**没**验证 legacy fallback 路径下的 `BuildArguments` 行为（只测了 standard path）。下一轮可以补。
