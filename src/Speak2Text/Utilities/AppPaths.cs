namespace Speak2Text.Utilities;

public static class AppPaths
{
    public static string RuntimeRoot
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable("SPEAK2TEXT_HOME");
            return string.IsNullOrWhiteSpace(overrideRoot)
                ? AppContext.BaseDirectory
                : Path.GetFullPath(overrideRoot);
        }
    }

    public static string EngineDirectory => Path.Combine(RuntimeRoot, "engine");
    public static string ModelsDirectory => Path.Combine(RuntimeRoot, "models");
    public static string TemporaryDirectory => Path.Combine(RuntimeRoot, "temp");

    public static string FfmpegPath => Path.Combine(EngineDirectory, "ffmpeg.exe");
    public static string FfprobePath => Path.Combine(EngineDirectory, "ffprobe.exe");
    public static string TranscribeCliPath => Path.Combine(EngineDirectory, "transcribe-cli.exe");
    public static string DefaultModelPath => Path.Combine(ModelsDirectory, "MOSS-Transcribe-Diarize-Q8_0.gguf");

    public static void PrepareTemporaryDirectory()
    {
        Directory.CreateDirectory(TemporaryDirectory);
        CleanupStaleTemporaryDirectories(TimeSpan.FromHours(24));
    }

    public static string CreateTemporaryWorkDirectory()
    {
        Directory.CreateDirectory(TemporaryDirectory);

        var name = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var directory = Path.Combine(TemporaryDirectory, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void CleanupStaleTemporaryDirectories(TimeSpan maxAge)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        var cutoff = DateTime.UtcNow - maxAge;

        foreach (var directory in Directory.EnumerateDirectories(TemporaryDirectory))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                var lastActivity = info.LastWriteTimeUtc > info.CreationTimeUtc
                    ? info.LastWriteTimeUtc
                    : info.CreationTimeUtc;

                if (lastActivity <= cutoff)
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // A live or locked task directory is left untouched.
            }
        }
    }
}
