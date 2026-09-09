using System.Globalization;
using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class FfmpegService(ProcessRunner processRunner)
{
    public async Task<string> ConvertToMono16KhzWavAsync(
        string inputPath,
        string workDirectory,
        TranscriptionOptions options,
        Action<EngineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.FfmpegPath))
            throw new FileNotFoundException("未找到 ffmpeg.exe。", AppPaths.FfmpegPath);
        if (!File.Exists(AppPaths.FfprobePath))
            throw new FileNotFoundException("未找到 ffprobe.exe。", AppPaths.FfprobePath);

        var durationMs = await ProbeDurationMillisecondsAsync(inputPath, cancellationToken);
        onProgress?.Invoke(new EngineProgress(
            "FFMPEG",
            "正在转换音频",
            0,
            0,
            durationMs));

        var outputPath = Path.Combine(workDirectory, "audio-16k-mono.wav");
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-nostats",
            "-i", inputPath,
            "-vn",
            "-ac", "1",
            "-ar", "16000"
        };

        if (options.CpuThreadLimit > 0)
        {
            args.Add("-threads");
            args.Add(options.CpuThreadLimit.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-c:a");
        args.Add("pcm_s16le");
        args.Add("-progress");
        args.Add("pipe:2");
        args.Add(outputPath);

        long lastOutTimeUs = 0;

        void HandleProgressLine(string line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = line["out_time_us=".Length..].Trim();
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outTimeUs))
                    return;

                lastOutTimeUs = Math.Max(lastOutTimeUs, outTimeUs);
                var positionMs = Math.Clamp(lastOutTimeUs / 1000, 0, durationMs);
                var percent = durationMs > 0
                    ? Math.Clamp(positionMs * 100d / durationMs, 0d, 100d)
                    : 0d;

                onProgress?.Invoke(new EngineProgress(
                    "FFMPEG",
                    "正在转换音频",
                    percent,
                    positionMs,
                    durationMs));
                return;
            }

            if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
            {
                onProgress?.Invoke(new EngineProgress(
                    "FFMPEG",
                    "音频转换完成",
                    100,
                    durationMs,
                    durationMs));
            }
        }

        var result = await processRunner.RunAsync(
            AppPaths.FfmpegPath,
            args,
            AppPaths.EngineDirectory,
            HandleProgressLine,
            cancellationToken,
            new ProcessRunOptions(LowPriority: options.LimitCpu));

        if (result.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"FFmpeg 音频转换失败。\r\n{result.StandardError}".Trim());

        onProgress?.Invoke(new EngineProgress(
            "FFMPEG",
            "音频转换完成",
            100,
            durationMs,
            durationMs));

        return outputPath;
    }

    private async Task<long> ProbeDurationMillisecondsAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        var args = new[]
        {
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            inputPath
        };

        var result = await processRunner.RunAsync(
            AppPaths.FfprobePath,
            args,
            AppPaths.EngineDirectory,
            null,
            cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFprobe 无法读取音频时长。\r\n{result.StandardError}".Trim());

        var raw = result.StandardOutput.Trim();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) ||
            seconds <= 0)
        {
            throw new InvalidOperationException($"FFprobe 返回了无效音频时长：{raw}");
        }

        return Math.Max(1, (long)Math.Round(seconds * 1000d));
    }
}
