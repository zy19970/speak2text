using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class FfmpegService(ProcessRunner processRunner)
{
    public async Task<string> ConvertToMono16KhzWavAsync(
        string inputPath,
        string workDirectory,
        TranscriptionOptions options,
        Action<string>? onLog,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.FfmpegPath))
            throw new FileNotFoundException("未找到 ffmpeg.exe。请将它放到程序目录的 engine 文件夹中。", AppPaths.FfmpegPath);

        var outputPath = Path.Combine(workDirectory, "audio-16k-mono.wav");
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", inputPath,
            "-vn",
            "-ac", "1",
            "-ar", "16000"
        };

        if (options.CpuThreadLimit > 0)
        {
            args.Add("-threads");
            args.Add(options.CpuThreadLimit.ToString());
        }

        args.Add("-c:a");
        args.Add("pcm_s16le");
        args.Add(outputPath);

        var result = await processRunner.RunAsync(
            AppPaths.FfmpegPath,
            args,
            AppPaths.EngineDirectory,
            onLog,
            cancellationToken,
            new ProcessRunOptions(LowPriority: options.LimitCpu));

        if (result.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"FFmpeg 音频转换失败。\r\n{result.StandardError}".Trim());

        return outputPath;
    }
}
