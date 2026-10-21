using System.Diagnostics;
using System.IO;

namespace Claxe.Services;

public static class Log
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        Debug.WriteLine(line);
        try
        {
            lock (Gate)
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
        }
        catch (IOException ex)
        {
            // The log is diagnostic only; a locked/full disk must not take the browser down.
            Debug.WriteLine($"Log yazılamadı: {ex.Message}");
        }
    }
}
