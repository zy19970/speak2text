# Speak2Text

一个 Windows 离线录音转文字工具。第一版采用：

- **C# / .NET 10 / WinForms**：桌面界面
- **FFmpeg**：把 m4a、mp3、aac、flac 等音频统一转换为 16 kHz 单声道 WAV
- **transcribe.cpp**：本地 GGUF 推理
- **MOSS-Transcribe-Diarize 0.9B Q8_0**：中文/英文转写 + 说话人区分

目标是做成绿色便携版：解压后直接运行，不安装 Python、PyTorch、CUDA 或 .NET Runtime。

## 当前功能

- 一次多选或拖入多个录音文件，按队列顺序串行处理
- 本地离线转写
- 自动输出 `Speaker 01 / Speaker 02 / ...`
- 保留分段时间戳
- 后端可选 `auto / CPU / Vulkan`
- 语言可选自动、中文、英文
- 输出 Markdown、TXT、SRT、JSON
- 队列支持删除、清空、上移、下移；后台串行执行，界面不阻塞
- 不在界面显示识别结果预览，完成后直接打开输出目录查看文件
- 精确显示 FFmpeg 转换百分比、已处理媒体时间、已耗时和预计剩余时间
- 使用 Speak2Text 专用 transcribe.cpp 构建，在 MOSS 内部编码、prefill 和逐 token 解码阶段输出源码级实时进度
- MOSS 解码阶段优先依据模型已经生成的 `[秒数]` 时间标记计算录音覆盖进度；时间标记尚未出现时以 token 生成预算显示“约”进度
- 实时显示系统 CPU / GPU 利用率
- CPU 占用可设置上限：通过限制 FFmpeg 与 transcribe.cpp 的线程数，并降低子进程优先级实现
- GPU 占用可设置节流目标：对 Vulkan/Auto 转写进程进行占空比节流，属于近似控制
- “停止队列”会取消当前文件并暂停后续等待项；再次点击“开始队列”可继续剩余任务
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
3. 当前版本支持多文件串行队列；为避免资源争抢，不并行启动多个 MOSS 实例。Speaker 名称映射、跨程序重启的持久化队列和长录音自动切片可在后续版本加入。
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


## 多文件转写队列

可以在“转写队列”中一次加入多个文件，或者直接把多个录音拖入窗口。重复路径不会重复加入。

队列默认严格串行：

```text
文件 1：FFmpeg → MOSS → 导出 → 清理 temp
                         ↓
文件 2：FFmpeg → MOSS → 导出 → 清理 temp
                         ↓
文件 3：...
```

这样不会同时加载多个 MOSS 模型，也不会因为并行转写突破 CPU/GPU 资源限制。

队列每一行显示：

- 文件名
- 状态：等待中 / 处理中 / 已完成 / 失败 / 已取消
- 当前阶段
- 当前阶段进度
- 已耗时

支持“删除选中”“清空队列”“上移”“下移”。某个文件失败后会记录错误并继续处理下一个文件；鼠标停在失败行的阶段单元格上可以查看错误信息。

点击“停止队列”时，当前文件会被取消并标记为“已取消”，后续尚未开始的文件继续保留为“等待中”。再次点击“开始队列”会从剩余等待项继续。

整个队列使用同一个输出目录。如果不同来源目录里存在同名录音，或者输出目录里已经有同名结果，程序会自动追加 `_2`、`_3` 等后缀，避免覆盖既有转写结果。


## GPU 长录音保护

MOSS-Transcribe-Diarize 会把整段录音保存在内存中，内存占用会随录音时长增长。上游文档给出的经验值约为每分钟额外 85 MB：30 分钟约 2.5 GB，1 小时约 5 GB。CPU 路径可以直接使用系统 RAM，而集成显卡通过 Vulkan 运行时仍受设备内存 heap、单次 buffer 分配和驱动可用显存窗口限制，因此长录音可能出现“CPU 正常、Vulkan 失败”的情况。

Speak2Text 对非 CPU 后端做两层保护：

1. Vulkan / Auto 运行 MOSS 时显式使用 `--kv-type f16`，降低解码 KV cache 的设备内存压力。
2. 如果 Vulkan / Auto 进程仍以非零退出码失败，程序不会重新做 FFmpeg 转换，而是保留当前 `temp/<任务>/audio-16k-mono.wav`，直接对同一个 WAV 自动切换到 CPU 重试。

回退过程中界面会显示：

```text
GPU失败，切换CPU
```

如果 CPU 重试成功，该文件正常导出并继续队列中的下一个文件；导出 JSON / Markdown 中记录的 backend 会写成实际使用的 `cpu`。只有 GPU 和 CPU 都失败时，该队列项才最终标记为失败。

这里不默认把长录音自动切成多个小段，因为 MOSS 的匿名说话人标签在不同切片之间不保证保持一致；在加入跨切片说话人重关联之前，自动切片会影响后续 `Speaker 01 → 张老师` 这类全局替换。


## 超长录音自动分段

MOSS 当前单次 session 的上下文上限约为 65,536 token。以 16 kHz 音频换算，上游错误信息显示单次可支持约 52,428,569 samples，也就是约 54 分 37 秒。超过这个长度时，无论 CPU 还是 Vulkan 都会报 `input too long`，这不是 GPU 专属错误。

Speak2Text 现在会在进入 MOSS 前自动检查已经转换好的 WAV 时长：

- CPU：超过 45 分钟自动分段。
- Vulkan / Auto：超过 20 分钟自动分段，主要是同时降低长录音的 GPU/Vulkan 内存压力。
- 相邻分段保留 60 秒重叠区。
- 每次只生成一个 `chunk-xxx.wav`，该分段识别完成后立即删除，不会把所有分段同时留在 `temp/`。
- 分段进度会合并成整份录音的总体识别进度，界面显示“分段 x/n”。

说话人处理采用重叠区自动重映射：程序比较相邻分段重叠 60 秒内各 Speaker 的时间重合关系，把新分段的匿名 Speaker ID 尽量映射回已有全局 Speaker ID。最终合并时以重叠区中点作为交接位置，避免同一段话重复输出。

这是基于时间重叠的自动匹配，通常适合培训、会议这类连续录音，但不能把它当作声纹身份认证。如果边界附近同时多人抢话、长时间静音或说话人恰好在分段处完全更换，仍可能出现 Speaker 编号映射不准的情况。
