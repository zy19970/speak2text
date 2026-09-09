# engine

便携运行时目录。发布后请把以下文件放在 `Speak2Text.exe` 同级的 `engine` 文件夹：

- `ffmpeg.exe`
- `transcribe-cli.exe`
- transcribe.cpp Windows 构建所需的同目录 DLL（若使用动态 backend / Vulkan 构建）

程序不会自动安装任何系统组件。
