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
- 不在界面显示识别结果预览，完成后直接打开输出目录查看文件
- 精确显示 FFmpeg 转换百分比、已处理媒体时间、已耗时和预计剩余时间
- 使用 Speak2Text 专用 transcribe.cpp 构建，在 MOSS 内部编码、prefill 和逐 token 解码阶段输出源码级实时进度
- MOSS 解码阶段优先依据模型已经生成的 `[秒数]` 时间标记计算录音覆盖进度；时间标记尚未出现时以 token 生成预算显示“约”进度
- 实时显示系统 CPU / GPU 利用率
- CPU 占用可设置上限：通过限制 FFmpeg 与 transcribe.cpp 的线程数，并降低子进程优先级实现
- GPU 占用可设置节流目标：对 Vulkan/Auto 转写进程进行占空比节流，属于近似控制
- 支持取消正在执行的 FFmpeg / transcribe-cli 任务
- 程序目录下固定保留 `temp/` 临时目录，并提供“打开临时目录”按钮，便于直接检查临时文件是否已清理
- 运行时和模型均使用相对目录，便于直接复制整个文件夹到另一台 Windows 电脑

## 目录结构

```text
Speak2Text.exe
engine/
  ffmpeg.exe
  ffprobe.exe
  transcribe-cli.exe
  ... transcribe.cpp 所需 DLL
models/
  MOSS-Transcribe-Diarize-Q8_0.gguf
temp/
  <任务时间-GUID>/
    audio-16k-mono.wav
    batch.txt
```

源码仓库中不直接提交二进制运行时和约 987 MB 的 GGUF 模型。GitHub Actions 会自动构建 `transcribe-cli.exe`（CPU + Vulkan）并下载便携版 `ffmpeg.exe`，打包进 `Speak2Text-win-x64.zip`；你实际使用时只需另外补充 MOSS Q8_0 模型。

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

GitHub Actions 会自动从 BtbN/FFmpeg-Builds 下载 Windows x64 静态 FFmpeg，并把 `ffmpeg.exe` 和 `ffprobe.exe` 放入便携包的 `engine/`。`ffprobe.exe` 用于在转换开始前读取精确媒体时长，`ffmpeg.exe -progress pipe:2` 用于实时报告已转换时间。如果是本地手工构建，也可以自行准备 `ffmpeg.exe` 放到该目录。

转换参数：

```text
ffmpeg -i input.m4a -vn -ac 1 -ar 16000 -c:a pcm_s16le output.wav
```

## transcribe.cpp

GitHub Actions 固定从 `transcribe.cpp v0.2.3` 源码构建 Windows x64 `transcribe-cli.exe`，同时启用 CPU 与 Vulkan backend。构建前会执行 `scripts/patch-transcribe-progress.py`，仅对本次构建的 MOSS 源码加入 `S2T_PROGRESS` stderr 进度协议，不修改上游仓库，也不污染 `--batch-jsonl` 的 stdout。生成的 CLI 随便携包放入 `engine/`。

程序里可以选择 `auto / CPU / Vulkan`。在支持 Vulkan 的 Intel Iris Xe 等显卡上，可以直接尝试 Vulkan；不兼容时切回 CPU。

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

## GitHub Actions 便携包

推送到 `main` 后，GitHub Actions 会自动完成：安装 Vulkan SDK、构建 transcribe.cpp CPU+Vulkan CLI、下载 FFmpeg、编译 .NET 10 WinForms、生成 self-contained 绿色版 ZIP。

Actions 产物名为：

```text
Speak2Text-win-x64
```

解压后已经包含 `Speak2Text.exe`、`engine/ffmpeg.exe`、`engine/transcribe-cli.exe`。**模型仍需单独放入 `models/`。**

## 本地生成绿色便携版

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

## 资源限制说明

默认启用 CPU 70% 与 GPU 70%：

- **CPU 70%**：不是 Windows Task Manager 意义上的精确硬限值，而是根据逻辑处理器数向下取整为可用线程数。例如 8 个逻辑处理器时，70% 会使用约 5 个工作线程，同时把子进程优先级降为 BelowNormal。这样比单纯让模型满核运行更不容易影响前台工作。
- **GPU 70%**：Windows 并没有给普通应用提供一个跨 Intel / AMD / NVIDIA 通用的“把 GPU 硬限制在 70%”接口。因此这里使用约 500 ms 周期的运行/暂停占空比节流来降低平均 GPU 压力。它是**近似目标，不保证任务管理器读数始终不超过设定值**。
- 实时 GPU 利用率使用 Windows PDH 的 `GPU Engine(*)\\Utilization Percentage` 计数器读取；驱动不提供该计数器时界面会显示 `GPU --`。

如果主要目的是后台转写同时继续办公，建议先使用默认 70%；如果仍感觉机器卡顿，可以把 CPU / GPU 调到 50%～60%。

## 注意事项

1. MOSS 会把较长录音整体放入内存。32 GB 内存处理常见培训录音通常够用，但特别长的录音仍建议预先切分。
2. `auto` backend 由 transcribe.cpp 自动选择可用后端；若 Vulkan 构建或驱动不兼容，可切换为 CPU。
3. 当前版本一次处理一个录音文件。批量队列、Speaker 名称映射、断点续转和长录音自动切片可在后续版本加入。
4. 该程序本身不联网；只有你自行下载模型时需要网络；GitHub Actions 构建阶段会联网获取 FFmpeg、Vulkan SDK 和 transcribe.cpp。


## 实时进度口径

处理进度分阶段显示，不把不同计算阶段混成一个伪精确总百分比：

1. **音频转换**：精确进度。FFprobe 给出总时长，FFmpeg 的 `out_time_us` 给出已经转换到的媒体位置。
2. **MOSS 音频编码**：源码内部按实际 30 秒 encoder chunk 完成数报告。
3. **MOSS 特征适配**：源码在 adaptor graph 完成后报告。
4. **MOSS prefill**：源码按真实 prompt chunk 完成数报告；短 prompt 为单次 prefill。
5. **MOSS 解码**：每 8 个生成 token 解码一次当前 partial text，提取最新完整数字时间标记（例如 `[123.45]`），用它除以总音频时长得到已经覆盖的录音位置。若还没有出现时间标记，则暂时以生成 token / generation budget 显示带“约”标记的进度。
6. **已耗时**：从点击“开始转写”起累计。
7. **预计剩余**：按当前阶段已经完成的比例和该阶段实耗时间动态估算，因此本质上是 ETA，不是硬保证。


## 临时文件目录

临时文件不再写入 Windows 的 `%TEMP%`。程序固定使用与 `Speak2Text.exe` 同级的：

```text
temp/
```

每个任务会创建一个可读的独立子目录，例如：

```text
temp/
└─ 20260909-154123456-2f0d9b0f.../
   ├─ audio-16k-mono.wav
   └─ batch.txt
```

任务正常完成、取消或报错后，程序会对该任务子目录进行最多 5 次删除重试。删除成功后，`temp/` 根目录仍然保留，因此可以直接打开它确认目录是否为空。

程序启动时还会扫描 `temp/`，自动删除超过 24 小时的残留任务目录。这主要用于清理断电、强制结束进程或系统异常导致的历史临时文件。正在使用或仍被锁定的目录不会被强制删除。

界面提供“打开临时目录”按钮，可以随时查看当前任务生成的临时文件和任务结束后的清理结果。
