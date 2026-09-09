using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class TranscriptExporter
{
    public async Task<IReadOnlyList<string>> ExportAsync(
        TranscriptionResult result,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(options.AudioPath));
        var exported = new List<string>();

        if (options.ExportMarkdown)
        {
            var path = Path.Combine(options.OutputDirectory, $"{baseName}_逐字稿.md");
            await File.WriteAllTextAsync(path, BuildMarkdown(result), new UTF8Encoding(false), cancellationToken);
            exported.Add(path);
        }

        if (options.ExportText)
        {
            var path = Path.Combine(options.OutputDirectory, $"{baseName}_逐字稿.txt");
            await File.WriteAllTextAsync(path, BuildText(result), new UTF8Encoding(false), cancellationToken);
            exported.Add(path);
        }

        if (options.ExportSrt)
        {
            var path = Path.Combine(options.OutputDirectory, $"{baseName}.srt");
            await File.WriteAllTextAsync(path, BuildSrt(result), new UTF8Encoding(false), cancellationToken);
            exported.Add(path);
        }

        if (options.ExportJson)
        {
            var path = Path.Combine(options.OutputDirectory, $"{baseName}.json");
            var payload = new
            {
                source_audio = result.SourceAudioPath,
                generated_at = result.GeneratedAt,
                model = Path.GetFileName(result.ModelPath),
                backend = result.Backend,
                language = result.Language,
                speakers = result.SpeakerIds.Select(x => $"Speaker {x:00}").ToArray(),
                full_text = result.FullText,
                segments = result.Segments.Select(x => new
                {
                    start_ms = x.StartMilliseconds,
                    end_ms = x.EndMilliseconds,
                    speaker_id = x.SpeakerId,
                    speaker = x.SpeakerLabel,
                    text = x.Text
                })
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
            exported.Add(path);
        }

        return exported;
    }

    private static string BuildMarkdown(TranscriptionResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Path.GetFileNameWithoutExtension(result.SourceAudioPath)}");
        sb.AppendLine();
        sb.AppendLine($"> 转写时间：{result.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}  ");
        sb.AppendLine($"> 模型：{Path.GetFileName(result.ModelPath)}  ");
        sb.AppendLine($"> 后端：{result.Backend}  ");
        sb.AppendLine($"> 语言：{result.Language}");
        sb.AppendLine();

        foreach (var segment in result.Segments)
        {
            sb.AppendLine($"**[{TimeText.ToClock(segment.StartMilliseconds)}] {segment.SpeakerLabel}**");
            sb.AppendLine();
            sb.AppendLine(segment.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildText(TranscriptionResult result)
    {
        var sb = new StringBuilder();
        foreach (var segment in result.Segments)
        {
            sb.Append('[').Append(TimeText.ToClock(segment.StartMilliseconds)).Append("] ")
              .Append(segment.SpeakerLabel).Append(": ")
              .AppendLine(segment.Text);
        }
        return sb.ToString();
    }

    private static string BuildSrt(TranscriptionResult result)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < result.Segments.Count; i++)
        {
            var segment = result.Segments[i];
            var start = Math.Max(0, segment.StartMilliseconds);
            var end = segment.EndMilliseconds > start ? segment.EndMilliseconds : start + 1000;

            sb.AppendLine((i + 1).ToString());
            sb.Append(TimeText.ToSrt(start)).Append(" --> ").AppendLine(TimeText.ToSrt(end));
            sb.Append(segment.SpeakerLabel).Append(": ").AppendLine(segment.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "transcript" : value;
    }
}
