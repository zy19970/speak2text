# Speak2Text

一个 Windows 离线录音转文字工具。第一版采用：

- **C# / .NET 10 / WinForms**：桌面界面
- **FFmpeg**：把 m4a、mp3、aac、flac 等音频统一转换为 16 kHz 单声道 WAV
- **transcribe.cpp**：本地 GGUF 推理
- **MOSS-Transcribe-Diarize 0.9B Q8_0**：中文/英文转写 + 说话人区分

目标是做成绿色便携版：解压后直接运行，不安装 Python、PyTorch、CUDA 或 .NET Runtime。

## 当前功能

- 拖拽或选择单个录音文件
- 本地离线转写
- 自动输出 `Speaker 01 / Speaker 02 / ...`
- 保留分段时间戳
- 后端可选 `auto / CPU / Vulkan`
- 语言可选自动、中文、英文
- 输出 Markdown、TXT、SRT、JSON
- 后台进程异步运行，界面不阻塞
- 支持取消正在执行的 FFmpeg / transcribe-cli 任务
- 运行时和模型均使用相对目录，便于直接复制整个文件夹到另一台 Windows 电脑

## 目录结构

```text
Speak2Text.exe
engine/
  ffmpeg.exe
  transcribe-cli.exe
  ... transcribe.cpp 所需 DLL
models/
  MOSS-Transcribe-Diarize-Q8_0.gguf
```

源码仓库中不提交 `ffmpeg.exe`、`transcribe-cli.exe` 和约 987 MB 的 GGUF 模型；请在本机准备好以后放到上述目录。

## MOSS 模型

transcribe.cpp 的 MOSS 文档推荐命令类似：

```text
transcribe-cli -m MOSS-Transcribe-Diarize-Q8_0.gguf --diarize --timestamps segment audio.wav
```

本项目为了稳定获取结构化结果，实际使用单文件 batch JSONL 模式：

```text
transcribe-cli.exe -q \
  -m models/MOSS-Transcribe-Diarize-Q8_0.gguf \
  --diarize \
  --timestamps segment \
  --backend auto \
  --batch batch.txt \
  --batch-jsonl
```

程序解析 `segments` 中的 `t0_ms / t1_ms / speaker_id / text`，再生成 Markdown、TXT、SRT 和 JSON。

MOSS Q8_0 模型下载地址可从 transcribe.cpp 官方 MOSS 文档进入：

- https://github.com/handy-computer/transcribe.cpp/blob/main/docs/models/moss-transcribe-diarize.md
- https://huggingface.co/handy-computer/MOSS-Transcribe-Diarize-gguf

## FFmpeg

下载 Windows 便携版 FFmpeg，将 `ffmpeg.exe` 放到 `engine/`。

转换参数：

```text
ffmpeg -i input.m4a -vn -ac 1 -ar 16000 -c:a pcm_s16le output.wav
```

## transcribe.cpp

Windows 下编译 transcribe.cpp 后，把 `transcribe-cli.exe` 以及该构建所需要的 DLL 一起复制到 `engine/`。

CPU 版本可以直接使用；如果构建了 Vulkan backend，可在程序里选择 Vulkan，用 Intel Iris Xe 等支持 Vulkan 的显卡尝试加速。

项目地址：

https://github.com/handy-computer/transcribe.cpp

## 开发环境

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2026 或支持 .NET 10 的 IDE

打开：

```text
src/Speak2Text/Speak2Text.csproj
```

或者命令行：

```powershell
dotnet build src/Speak2Text/Speak2Text.csproj -c Release
```

## 生成绿色便携版

前端本身采用 self-contained 单文件发布，所以目标机器不需要安装 .NET Runtime。

```powershell
./scripts/publish-portable.ps1
```

生成：

```text
dist/Speak2Text-win-x64.zip
```

如果本机仓库的 `engine/` 和 `models/` 已经放好了运行时和模型，可以：

```powershell
./scripts/publish-portable.ps1 -IncludeRuntimeFiles
```

这会把它们一起复制到便携包。模型和运行时只保留在本机，不会因为执行发布脚本而提交到 GitHub。

## 输出示例

Markdown / TXT 中会保留匿名说话人标签：

```text
[00:01:23] Speaker 01
今天主要介绍本次培训的安排……

[00:01:41] Speaker 02
这里我有一个问题……
```

后续可以直接全局替换：

```text
Speaker 01 -> 张老师
Speaker 02 -> 李老师
```

JSON 还会保留每个片段的毫秒级起止时间和 `speaker_id`，方便后续整理、批量替换姓名或再交给大模型加工。

## 注意事项

1. MOSS 会把较长录音整体放入内存。32 GB 内存处理常见培训录音通常够用，但特别长的录音仍建议预先切分。
2. `auto` backend 由 transcribe.cpp 自动选择可用后端；若 Vulkan 构建或驱动不兼容，可切换为 CPU。
3. 当前版本一次处理一个录音文件。批量队列、Speaker 名称映射、断点续转和长录音自动切片可在后续版本加入。
4. 该程序本身不联网；只有你自行下载 FFmpeg、transcribe.cpp 运行时和模型时需要网络。
