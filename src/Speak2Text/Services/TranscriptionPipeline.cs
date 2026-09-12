using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class TranscriptionPipeline
{
    private readonly FfmpegService _ffmpeg;
    private readonly LongAudioTranscriptionService _longAudioTranscriber;
    private readonly TranscriptExporter _exporter;

    public TranscriptionPipeline()
    {
        var processRunner = new ProcessRunner();
        _ffmpeg = new FfmpegService(processRunner);
        var transcriber = new TranscribeCliService(processRunner);
        _longAudioTranscriber = new LongAudioTranscriptionService(_ffmpeg, transcriber);
        _exporter = new TranscriptExporter();
    }

    public Task<long> ProbeDurationMillisecondsAsync(
        string inputPath,
        CancellationToken cancellationToken)
        => _ffmpeg.ProbeDurationMillisecondsAsync(inputPath, cancellationToken);

    public async Task<PipelineResult> RunAsync(
        TranscriptionOptions options,
        IProgress<PipelineMessage>? progress,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);
        var workDirectory = AppPaths.CreateTemporaryWorkDirectory();

        try
        {
            progress?.Report(new PipelineMessage(
                PipelineStage.Converting,
                "FFMPEG",
                "正在读取音频信息并准备转换…"));

            var wavPath = await _ffmpeg.ConvertToMono16KhzWavAsync(
                options.AudioPath,
                workDirectory,
                options,
                engine => progress?.Report(PipelineMessage.FromEngine(PipelineStage.Converting, engine)),
                cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Transcribing,
                "MOSS_LOAD",
                CudaBackendUi.IsCudaSelected
                    ? "正在使用 NVIDIA CUDA 加载 MOSS 模型…"
                    : "正在加载 MOSS Q8 模型…"));

            var result = await RunTranscriptionWithCudaFallbackAsync(
                wavPath,
                options,
                workDirectory,
                progress,
                cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Exporting,
                "EXPORT",
                "正在生成 Markdown / TXT / SRT / JSON…"));

            var files = await _exporter.ExportAsync(result, options, cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Completed,
                "DONE",
                "转写完成。",
                100));

            return new PipelineResult(result, files);
        }
        finally
        {
            await CleanupWorkDirectoryAsync(workDirectory);
        }
    }

    private async Task<TranscriptionResult> RunTranscriptionWithCudaFallbackAsync(
        string wavPath,
        TranscriptionOptions options,
        string workDirectory,
        IProgress<PipelineMessage>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _longAudioTranscriber.TranscribeAsync(
                wavPath,
                options,
                workDirectory,
                engine => progress?.Report(PipelineMessage.FromEngine(PipelineStage.Transcribing, engine)),
                cancellationToken);

            // The UI's explicit CUDA option intentionally travels through MainForm
            // as "auto" and is forced to --backend cuda by the native dispatcher.
            // If that run succeeded, record the actual backend as CUDA.
            if (CudaBackendUi.IsCudaSelected &&
                string.Equals(result.Backend, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return CopyResultWithBackend(result, "cuda");
            }

            return result;
        }
        catch (Exception ex) when (
            !string.Equals(options.Backend, "cpu", StringComparison.OrdinalIgnoreCase) &&
            IsCudaFailure(ex.ToString()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reason = DescribeCudaFailure(ex.ToString());
            progress?.Report(new PipelineMessage(
                PipelineStage.Transcribing,
                "MOSS_GPU_FALLBACK",
                $"CUDA 失败：{reason}；正在使用同一临时 WAV 自动切换 CPU 重试…",
                DiagnosticDetail: ex.ToString()));

            var cpuOptions = CopyOptionsWithBackend(options, "cpu");
            var cpuResult = await _longAudioTranscriber.TranscribeAsync(
                wavPath,
                cpuOptions,
                workDirectory,
                engine => progress?.Report(PipelineMessage.FromEngine(PipelineStage.Transcribing, engine)),
                cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Transcribing,
                "MOSS_CPU_FALLBACK",
                $"CUDA 失败后已自动切换 CPU 并继续处理。原错误：{reason}",
                DiagnosticDetail: ex.ToString()));

            return cpuResult;
        }
    }

    private static bool IsCudaFailure(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.ToLowerInvariant();
        return text.Contains("ggml_cuda") ||
               text.Contains("cuda error") ||
               text.Contains("cudaerror") ||
               text.Contains("cuda backend") ||
               text.Contains("cuda-capable") ||
               text.Contains("cudart") ||
               text.Contains("cublas") ||
               text.Contains("nvcuda") ||
               text.Contains("cuda driver");
    }

    private static string DescribeCudaFailure(string value)
    {
        var text = value.ToLowerInvariant();

        if (text.Contains("out of memory") ||
            text.Contains("memory allocation") ||
            text.Contains("failed to allocate") ||
            text.Contains("kv cache allocation failed"))
        {
            return "CUDA 显存不足或 KV cache / 缓冲区分配失败";
        }

        if (text.Contains("no cuda-capable device") ||
            text.Contains("driver version") ||
            text.Contains("cuda driver") ||
            text.Contains("nvcuda") ||
            text.Contains("backend not available"))
        {
            return "CUDA 后端不可用，或 NVIDIA 驱动与 CUDA 运行时不兼容";
        }

        if (text.Contains("cublas"))
            return "CUDA cuBLAS 执行失败";

        return "CUDA 后端执行失败";
    }

    private static TranscriptionOptions CopyOptionsWithBackend(
        TranscriptionOptions source,
        string backend)
        => new()
        {
            AudioPath = source.AudioPath,
            ModelPath = source.ModelPath,
            OutputDirectory = source.OutputDirectory,
            Backend = backend,
            Language = source.Language,
            ExportMarkdown = source.ExportMarkdown,
            ExportText = source.ExportText,
            ExportSrt = source.ExportSrt,
            ExportJson = source.ExportJson,
            LimitCpu = source.LimitCpu,
            MaxCpuPercent = source.MaxCpuPercent,
            LimitGpu = source.LimitGpu,
            MaxGpuPercent = source.MaxGpuPercent
        };

    private static TranscriptionResult CopyResultWithBackend(
        TranscriptionResult source,
        string backend)
        => new()
        {
            SourceAudioPath = source.SourceAudioPath,
            ModelPath = source.ModelPath,
            Backend = backend,
            Language = source.Language,
            GeneratedAt = source.GeneratedAt,
            Segments = source.Segments,
            FullText = source.FullText
        };

    private static void ValidateOptions(TranscriptionOptions options)
    {
        if (!File.Exists(options.AudioPath))
            throw new FileNotFoundException("录音文件不存在。", options.AudioPath);
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("模型文件不存在。", options.ModelPath);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("请选择输出目录。", nameof(options.OutputDirectory));
    }

    private static async Task CleanupWorkDirectoryAsync(string directory)
    {
        const int attempts = 5;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                Directory.Delete(directory, recursive: true);
                return;
            }
            catch when (attempt < attempts)
            {
                await Task.Delay(250);
            }
            catch
            {
                return;
            }
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

public sealed record PipelineMessage(
    PipelineStage Stage,
    string Phase,
    string Message,
    double? Percent = null,
    long? PositionMilliseconds = null,
    long? DurationMilliseconds = null,
    bool IsEstimate = false,
    string? DiagnosticDetail = null)
{
    public static PipelineMessage FromEngine(PipelineStage stage, EngineProgress engine)
        => new(
            stage,
            engine.Phase,
            engine.Message,
            engine.Percent,
            engine.PositionMilliseconds,
            engine.DurationMilliseconds,
            engine.IsEstimate,
            engine.DiagnosticDetail);
}

public sealed record PipelineResult(TranscriptionResult Transcript, IReadOnlyList<string> ExportedFiles);
