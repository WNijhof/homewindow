using System.IO;

namespace HomeyBar.Core;

// Unexpected errors, in %LOCALAPPDATA%\HomeyBar\HomeyBar.log, so a problem can be traced afterwards
public static class Log
{
    public static string FilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeyBar", "HomeyBar.log");

    public static void Error(string where, Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Keep the file small: start over once it passes 1 MB
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Updater.CurrentText} {where}: {e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take HomeyBar down
        }
    }
}
