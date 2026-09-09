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
    public static string FfmpegPath => Path.Combine(EngineDirectory, "ffmpeg.exe");
    public static string FfprobePath => Path.Combine(EngineDirectory, "ffprobe.exe");
    public static string TranscribeCliPath => Path.Combine(EngineDirectory, "transcribe-cli.exe");
    public static string DefaultModelPath => Path.Combine(ModelsDirectory, "MOSS-Transcribe-Diarize-Q8_0.gguf");

    public static string CreateTemporaryWorkDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "Speak2Text");
        Directory.CreateDirectory(root);
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
