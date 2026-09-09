using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class TranscriptionPipeline
{
    private readonly FfmpegService _ffmpeg;
    private readonly TranscribeCliService _transcriber;
    private readonly TranscriptExporter _exporter;

    public TranscriptionPipeline()
    {
        var processRunner = new ProcessRunner();
        _ffmpeg = new FfmpegService(processRunner);
        _transcriber = new TranscribeCliService(processRunner);
        _exporter = new TranscriptExporter();
    }

    public async Task<PipelineResult> RunAsync(
        TranscriptionOptions options,
        IProgress<PipelineMessage>? progress,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);
        var workDirectory = AppPaths.CreateTemporaryWorkDirectory();

        try
        {
            progress?.Report(new PipelineMessage(PipelineStage.Converting, "正在将录音转换为 16 kHz 单声道 WAV…"));
            var wavPath = await _ffmpeg.ConvertToMono16KhzWavAsync(
                options.AudioPath,
                workDirectory,
                options,
                line => progress?.Report(new PipelineMessage(PipelineStage.Converting, line)),
                cancellationToken);

            progress?.Report(new PipelineMessage(PipelineStage.Transcribing, "正在运行 MOSS-Transcribe-Diarize…"));
            var result = await _transcriber.TranscribeAsync(
                wavPath,
                options,
                workDirectory,
                line => progress?.Report(new PipelineMessage(PipelineStage.Transcribing, line)),
                cancellationToken);

            progress?.Report(new PipelineMessage(PipelineStage.Exporting, "正在生成 Markdown / TXT / SRT / JSON…"));
            var files = await _exporter.ExportAsync(result, options, cancellationToken);

            progress?.Report(new PipelineMessage(PipelineStage.Completed, "转写完成。"));
            return new PipelineResult(result, files);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static void ValidateOptions(TranscriptionOptions options)
    {
        if (!File.Exists(options.AudioPath))
            throw new FileNotFoundException("录音文件不存在。", options.AudioPath);
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("模型文件不存在。", options.ModelPath);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("请选择输出目录。", nameof(options.OutputDirectory));
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Temporary files can be cleaned by the OS later.
        }
    }
}

public enum PipelineStage
{
    Converting,
    Transcribing,
    Exporting,
    Completed
}

public sealed record PipelineMessage(PipelineStage Stage, string Message);
public sealed record PipelineResult(TranscriptionResult Transcript, IReadOnlyList<string> ExportedFiles);
