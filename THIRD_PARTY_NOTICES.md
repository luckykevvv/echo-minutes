# Third-Party Notices

Meeting Transfer 的发布包包含独立调用或随包分发的第三方运行时与库，但不包含模型权重。本文件用于定位运行时及设置页可下载模型的上游和许可证；再分发者仍应检查实际分发内容对应的完整条款。

## FFmpeg

- Project: <https://ffmpeg.org/>
- Source: <https://git.ffmpeg.org/ffmpeg.git>
- License information: <https://ffmpeg.org/legal.html>
- Bundled build identification: `N-125505-gc57660fb18-20260708`
- The bundled binary reports `--enable-gpl --enable-version3`; treat this build as GPLv3-covered and provide the required license text and corresponding source when redistributing it.

## whisper.cpp / GGML

- Project: <https://github.com/ggerganov/whisper.cpp>
- License: MIT
- Model source: <https://huggingface.co/ggerganov/whisper.cpp>
- Whisper model weights are not included in the release package. If users download them through Settings, their terms may also inherit requirements from the original OpenAI Whisper weights.

## sherpa-onnx

- Project: <https://github.com/k2-fsa/sherpa-onnx>
- License: Apache License 2.0
- Documentation and pretrained models: <https://k2-fsa.github.io/sherpa/onnx/>

## ONNX Runtime

- Project: <https://github.com/microsoft/onnxruntime>
- License: MIT

## NAudio

- Project: <https://github.com/naudio/NAudio>
- License: MIT

## SharpCompress

- Project: <https://github.com/adamhathcock/sharpcompress>
- License: MIT
- Used only to extract the single catalog-declared file from official compressed model downloads.

## Microsoft.Data.Sqlite and SQLite

- Microsoft.Data.Sqlite: <https://learn.microsoft.com/dotnet/standard/data/sqlite/>
- .NET repository license: MIT
- SQLite: <https://www.sqlite.org/copyright.html> (public domain)

## Speaker diarization models

- pyannote segmentation integration is distributed through sherpa-onnx model assets.
- 3D-Speaker / ERes2Net project: <https://github.com/modelscope/3D-Speaker>
- Before public redistribution, verify the exact license and attribution requirements attached to each downloaded model artifact; model licenses can differ from the inference runtime license.

## Other transcribed model families

The optional model catalog links to upstream Whisper, SenseVoice and Paraformer artifacts hosted on Hugging Face. Those model weights are not automatically covered by Meeting Transfer's source terms. Consult each model card before mirroring or redistributing the weights.
