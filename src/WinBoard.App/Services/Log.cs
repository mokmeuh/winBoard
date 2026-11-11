using System.IO;
using System.Text;
using WinBoard.Services.Settings;

namespace WinBoard.App.Services;

/// <summary>Journal d'erreurs minimal : %APPDATA%/WinBoard/winboard.log (jamais d'exception propagée).</summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string Path_ => System.IO.Path.Combine(AppPaths.Root, "winboard.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERREUR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var file = Path_;
                try
                {
                    // Rotation simple : l'ancien journal devient winboard.log.old.
                    if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                        File.Move(file, file + ".old", overwrite: true);
                }
                catch (IOException) { }

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(level).Append(' ').Append(message);
                if (ex != null) sb.AppendLine().Append(ex);
                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Le journal ne doit jamais faire planter l'application.
        }
    }
}
