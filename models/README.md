# models

默认模型文件名：

`MOSS-Transcribe-Diarize-Q8_0.gguf`

推荐从 transcribe.cpp 的 MOSS 模型文档所指向的 handy-computer Hugging Face 仓库下载 Q8_0 量化版本。模型约 987 MB，因此不直接提交到 GitHub 普通仓库。

程序也允许在界面中选择其他兼容的 `.gguf` 模型文件，但当前解析和命令行参数是按 MOSS-Transcribe-Diarize 设计的。
