using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;

namespace Claxe.ViewModels;

/// <summary>
/// Per-site resource monitor (sorune://gorev-yoneticisi, Shift+Esc). WebView2 lists its processes
/// with the frames each one renders; main frames are matched to tabs and panels by frame id, and
/// memory / CPU come from Windows for each process id. Chromium may put several tabs of the same
/// site into one process; those tabs then share one row. Sampling runs only while the page is open.
/// </summary>
public sealed class TaskManagerController
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Func<IEnumerable<TabViewModel>> _tabs;
    private readonly Func<IEnumerable<PanelViewModel>> _panels;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private readonly Dictionary<int, (DateTime At, TimeSpan Cpu)> _lastCpu = [];
    private bool _sampling;

    public TaskManagerController(Func<IEnumerable<TabViewModel>> tabs, Func<IEnumerable<PanelViewModel>> panels)
    {
        _tabs = tabs;
        _panels = panels;
        _timer.Tick += async (_, _) => await SampleAsync();
    }

    public void Start()
    {
        if (!_timer.IsEnabled)
            _timer.Start();
        _ = SampleAsync();
    }

    private IEnumerable<TabViewModel> Viewers()
        => _tabs().Where(t => t.IsShown && t.Url is { } url && InternalPages.IsPage(url, InternalPages.TaskManagerUrl));

    private async Task SampleAsync()
    {
        var viewers = Viewers().ToList();
        if (viewers.Count == 0)
        {
            // Nobody is looking: stop measuring.
            _timer.Stop();
            _lastCpu.Clear();
            return;
        }
        if (_sampling)
            return;

        _sampling = true;
        try
        {
            var payload = await BuildAsync();
            foreach (var viewer in viewers)
                viewer.PostToInternalPage(payload);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Info($"Görev yöneticisi ölçemedi: {ex.Message}");
        }
        finally
        {
            _sampling = false;
        }
    }

    private async Task<object> BuildAsync()
    {
        var environment = await BrowserEnvironment.GetAsync();
        var processes = await environment.GetProcessExtendedInfosAsync();

        // Main frame id → what Sorune calls it.
        var owners = new Dictionary<uint, (string Kind, string Id, string Title, string? Url, bool Sleeping, bool Private)>();
        foreach (var tab in _tabs())
        {
            if (FrameIdOf(tab) is { } frame)
                owners[frame] = ("tab", tab.Id, tab.Title, tab.Url, tab.IsSuspended, tab.IsPrivate);
        }
        foreach (var panel in _panels())
        {
            if (panel.Tab is { } tab && FrameIdOf(tab) is { } frame)
                owners[frame] = ("panel", tab.Id, panel.Name, tab.Url, false, false);
        }

        var rows = new List<object>();
        var seen = new HashSet<int>();
        foreach (var process in processes)
        {
            var pid = process.ProcessInfo.ProcessId;
            seen.Add(pid);
            var (memory, cpu) = Measure(pid);
            var frames = process.AssociatedFrameInfos;
            var members = frames
                .Where(f => f.ParentFrameInfo is null && owners.ContainsKey(f.FrameId))
                .Select(f => owners[f.FrameId])
                .DistinctBy(o => o.Id)
                .Select(o => new { kind = o.Kind, o.Id, o.Title, url = o.Url, sleeping = o.Sleeping, isPrivate = o.Private })
                .ToList();
            var extension = frames.FirstOrDefault(f => f.Source?.StartsWith("chrome-extension://", StringComparison.Ordinal) == true);

            rows.Add(new
            {
                pid,
                kind = process.ProcessInfo.Kind switch
                {
                    CoreWebView2ProcessKind.Browser => "browser",
                    CoreWebView2ProcessKind.Gpu => "gpu",
                    CoreWebView2ProcessKind.Utility => "utility",
                    CoreWebView2ProcessKind.Renderer when members.Count > 0 => "page",
                    CoreWebView2ProcessKind.Renderer when extension is not null => "extension",
                    CoreWebView2ProcessKind.Renderer => "renderer",
                    _ => "other",
                },
                detail = members.Count == 0 ? FirstHost(frames) : null,
                memory,
                cpu,
                members,
            });
        }

        // Sorune's own interface process.
        var self = Environment.ProcessId;
        var (selfMemory, selfCpu) = Measure(self);
        seen.Add(self);
        rows.Insert(0, new { pid = self, kind = "sorune", detail = (string?)null, memory = selfMemory, cpu = selfCpu, members = new List<object>() });

        foreach (var gone in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastCpu.Remove(gone);

        return new { type = "taskmanager", rows, cores = Environment.ProcessorCount };
    }

    private static uint? FrameIdOf(TabViewModel tab)
    {
        try
        {
            return tab.Core?.FrameId;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FirstHost(IEnumerable<CoreWebView2FrameInfo> frames)
        => frames.Select(f => Uri.TryCreate(f.Source, UriKind.Absolute, out var uri) ? uri.Host : null).FirstOrDefault(h => !string.IsNullOrEmpty(h));

    /// <summary>Working set in bytes and CPU % since the previous sample (100% = every core busy).</summary>
    private (long Memory, double Cpu) Measure(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var now = DateTime.UtcNow;
            var total = process.TotalProcessorTime;
            double cpu = 0;
            if (_lastCpu.TryGetValue(pid, out var last))
            {
                var wall = (now - last.At).TotalMilliseconds;
                if (wall > 0)
                    cpu = Math.Clamp((total - last.Cpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100, 0, 100);
            }
            _lastCpu[pid] = (now, total);
            return (process.WorkingSet64, Math.Round(cpu, 1));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (0, 0); // the process ended meanwhile
        }
    }
}
