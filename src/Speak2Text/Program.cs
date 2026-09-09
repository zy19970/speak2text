using Speak2Text.Utilities;

namespace Speak2Text;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Keep a visible temp folder beside Speak2Text.exe and remove stale
        // task directories left by crashes or forced shutdowns.
        AppPaths.PrepareTemporaryDirectory();

        Application.Run(new MainForm());
    }
}
