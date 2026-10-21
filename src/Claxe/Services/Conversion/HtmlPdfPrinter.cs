using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services.Conversion;

/// <summary>
/// Prints HTML (an .html file, or a page generated from text/Markdown) to PDF with an invisible
/// WebView2. Scripts are off: the documents are the user's own files, not pages to run.
/// </summary>
public static class HtmlPdfPrinter
{
    public static async Task PrintFileAsync(string htmlFile, string output)
    {
        var window = Application.Current.MainWindow ?? throw new InvalidOperationException("Ana pencere yok.");
        var environment = await BrowserEnvironment.GetAsync();
        var controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(window).Handle);
        try
        {
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            core.Settings.IsScriptEnabled = false;

            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            core.Navigate(new Uri(htmlFile).AbsoluteUri);
            if (await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(30))) != loaded.Task || !loaded.Task.Result)
                throw new IOException(L.T("Belge açılamadı."));

            var settings = environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            if (!await core.PrintToPdfAsync(output, settings))
                throw new IOException(L.T("PDF yazılamadı."));
        }
        finally
        {
            controller.Close();
        }
    }

    /// <summary>Writes the HTML to a temporary file (no size limit, unlike NavigateToString) and prints it.</summary>
    public static async Task PrintHtmlAsync(string html, string output)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"sorune-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(temp, html);
        try
        {
            await PrintFileAsync(temp, output);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
