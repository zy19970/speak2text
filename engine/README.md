# engine

便携运行时目录。GitHub Actions 生成的完整绿色包会自动包含：

- `ffmpeg.exe`
- `ffprobe.exe`
- `transcribe-cli.exe`：Speak2Text 的小型调度器
- `transcribe-native.exe`：带 CPU + Vulkan + CUDA 的 patched transcribe.cpp 引擎
- `cudart64_12.dll`
- `cublas64_12.dll`
- `cublasLt64_12.dll`

CUDA 运行时 DLL 会跟随绿色包，因此目标电脑不需要单独安装 CUDA Toolkit。要使用 `CUDA（NVIDIA）` 后端，目标电脑仍需安装可用的 NVIDIA 显卡驱动；`nvcuda.dll` 由 NVIDIA 驱动提供，不随本项目分发。

没有 NVIDIA 显卡时仍可使用 CPU 或 Vulkan。程序不会自动安装任何系统组件。
