using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Claxe.Services.Conversion;

/// <summary>
/// Office formats (Word, PowerPoint, Excel) are converted by LibreOffice running headless.
/// Sorune doesn't ship it; the converter panel offers to install it with winget.
/// </summary>
public static partial class LibreOffice
{
    public const string DownloadPage = "https://www.libreoffice.org/download/download-libreoffice/";
    private const string WingetId = "TheDocumentFoundation.LibreOffice";
    private static readonly TimeSpan ConvertTimeout = TimeSpan.FromMinutes(5);

    /// <summary>soffice.exe, or null when LibreOffice isn't installed.</summary>
    public static string? Find()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var key in new[] { @"SOFTWARE\LibreOffice\UNO\InstallPath", @"SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath" })
            {
                using var install = hive.OpenSubKey(key);
                if (install?.GetValue(null) is string folder && File.Exists(Path.Combine(folder, "soffice.exe")))
                    return Path.Combine(folder, "soffice.exe");
            }
        }

        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "LibreOffice", "program", "soffice.exe"))
            .FirstOrDefault(File.Exists);
    }

    public static string? Version(string soffice)
        => FileVersionInfo.GetVersionInfo(soffice).ProductVersion;

    /// <summary>Converts into a scratch folder first, so nothing next to the source is overwritten.</summary>
    public static async Task ConvertAsync(string source, string format, string output)
    {
        var soffice = Find() ?? throw new InvalidOperationException(L.T("LibreOffice kurulu değil."));
        var scratch = Path.Combine(Path.GetTempPath(), "Sorune-convert", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var start = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "--headless", "--norestore", "--nolockcheck", "--nodefault",
                    // Its own profile: works even while the user has LibreOffice open.
                    "-env:UserInstallation=" + new Uri(Path.Combine(AppPaths.DataRoot, "libreoffice-profile")).AbsoluteUri,
                    "--convert-to", format, "--outdir", scratch, source,
                },
            };

            using var process = Process.Start(start) ?? throw new InvalidOperationException(L.T("LibreOffice başlatılamadı."));
            using var timeout = new CancellationTokenSource(ConvertTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(L.T("LibreOffice zamanında bitiremedi."));
            }

            var produced = Directory.GetFiles(scratch).FirstOrDefault()
                           ?? throw new IOException(L.T("LibreOffice bu dosyayı dönüştüremedi."));
            File.Move(produced, output);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is harmless.
            }
        }
    }

    /// <summary>
    /// Installs LibreOffice with winget (Windows asks for permission). <paramref name="progress"/>
    /// gets 0..1 while downloading, null when the step has no measurable progress.
    /// Returns false when winget isn't available.
    /// </summary>
    public static async Task<bool> InstallAsync(Action<double?> progress)
    {
        var start = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            ArgumentList =
            {
                "install", "--id", WingetId, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
        };

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Info($"winget bulunamadı: {ex.Message}");
            return false;
        }
        if (process is null)
            return false;

        using (process)
        {
            _ = process.StandardError.ReadToEndAsync();
            var buffer = new char[512];
            var line = new StringBuilder();
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var c = buffer[i];
                    if (c is '\r' or '\n')
                    {
                        Report(line.ToString(), progress);
                        line.Clear();
                    }
                    else
                    {
                        line.Append(c);
                    }
                }
            }

            await process.WaitForExitAsync();
            Log.Info($"winget LibreOffice kurulumu bitti, çıkış kodu {process.ExitCode}");
        }
        return true;
    }

    // winget's text is localized; only the "12.3 MB / 345 MB" part is read.
    private static void Report(string line, Action<double?> progress)
    {
        var match = SizePattern().Match(line);
        if (match.Success
            && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var done)
            && double.TryParse(match.Groups[3].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var total)
            && total > 0)
        {
            var doneBytes = done * Scale(match.Groups[2].Value);
            var totalBytes = total * Scale(match.Groups[4].Value);
            progress(Math.Clamp(doneBytes / totalBytes, 0, 1));
        }
        else if (line.Length > 0 && !line.Trim().All(c => c is '-' or '\\' or '|' or '/' or ' '))
        {
            progress(null);
        }
    }

    private static double Scale(string unit) => unit.ToUpperInvariant() switch
    {
        "KB" => 1e3,
        "MB" => 1e6,
        "GB" => 1e9,
        _ => 1,
    };

    [GeneratedRegex(@"([\d.,]+)\s*(KB|MB|GB)\s*/\s*([\d.,]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();
}
