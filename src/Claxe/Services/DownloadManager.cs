using System.Diagnostics;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

public enum DownloadStatus
{
    InProgress,
    Paused,
    Completed,
    Cancelled,
    Failed,
}

public sealed class DownloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long? TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public DownloadStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => Status is DownloadStatus.InProgress or DownloadStatus.Paused;

    public bool FileExists => Status == DownloadStatus.Completed && File.Exists(FilePath);
}

/// <summary>
/// Claxe's own download manager. WebView2 still performs the download (including its
/// SmartScreen check); Claxe hides WebView2's flyout and keeps the list in downloads.json.
/// </summary>
public sealed class DownloadManager
{
    /// <summary>Opening these runs code, so the user confirms first.</summary>
    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".scr", ".lnk", ".reg", ".hta", ".cpl", ".jar", ".appx", ".appxbundle",
    };

    private readonly List<DownloadRecord> _records; // oldest first
    private readonly Dictionary<string, CoreWebView2DownloadOperation> _live = [];

    public DownloadManager()
    {
        _records = JsonStore.Load<List<DownloadRecord>>(FilePath) ?? [];
        foreach (var record in _records.Where(r => r.IsActive))
        {
            record.Status = DownloadStatus.Failed;
            record.Error = L.T("Sorune kapandığında yarım kaldı");
        }
    }

    /// <summary>Any change, including progress (can fire many times per second).</summary>
    public event EventHandler? Changed;

    public event EventHandler<DownloadRecord>? Completed;

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "downloads.json");

    /// <summary>Newest first.</summary>
    public IReadOnlyList<DownloadRecord> Items => Enumerable.Reverse(_records).ToList();

    public int ActiveCount => _records.Count(r => r.IsActive);

    /// <summary>True once something was downloaded in this run (shows the toolbar button).</summary>
    public bool HasSessionDownloads { get; private set; }

    /// <summary>0..1 across active downloads with a known size; null when unknown or idle.</summary>
    public double? ActiveProgress
    {
        get
        {
            var sized = _records.Where(r => r.IsActive && r.TotalBytes > 0).ToList();
            if (sized.Count == 0)
                return null;
            return Math.Clamp((double)sized.Sum(r => r.ReceivedBytes) / sized.Sum(r => r.TotalBytes!.Value), 0, 1);
        }
    }

    public void Attach(CoreWebView2 core) => core.DownloadStarting += OnDownloadStarting;

    public void Cancel(string? id)
    {
        if (id is not null && _live.TryGetValue(id, out var operation))
            operation.Cancel();
    }

    public void Pause(string? id)
    {
        if (id is null || !_live.TryGetValue(id, out var operation) || Find(id) is not { Status: DownloadStatus.InProgress } record)
            return;

        operation.Pause();
        record.Status = DownloadStatus.Paused;
        RaiseChanged(save: true);
    }

    public void Resume(string? id)
    {
        if (id is null || !_live.TryGetValue(id, out var operation) || !operation.CanResume || Find(id) is not { } record)
            return;

        operation.Resume();
        record.Status = DownloadStatus.InProgress;
        record.Error = null;
        RaiseChanged(save: true);
    }

    /// <summary>The finished file, or null if it no longer exists.</summary>
    public string? GetFile(string? id) => Find(id) is { FileExists: true } record ? record.FilePath : null;

    public static bool IsRisky(string path) => RiskyExtensions.Contains(Path.GetExtension(path));

    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void ShowInFolder(string? id)
    {
        if (Find(id) is not { } record)
            return;

        if (File.Exists(record.FilePath))
            Process.Start("explorer.exe", $"/select,\"{record.FilePath}\"");
        else if (Path.GetDirectoryName(record.FilePath) is { } folder && Directory.Exists(folder))
            Process.Start("explorer.exe", $"\"{folder}\"");
    }

    public void Remove(string? id)
    {
        if (Find(id) is { IsActive: false } record)
        {
            _records.Remove(record);
            RaiseChanged(save: true);
        }
    }

    /// <summary>Removes finished entries from the list (never deletes the files).</summary>
    public void ClearFinished(DateTime? since = null)
    {
        if (_records.RemoveAll(r => !r.IsActive && (since is null || r.StartedAt >= since)) > 0)
            RaiseChanged(save: true);
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Hide WebView2's own flyout; the download itself continues normally.
        e.Handled = true;

        var operation = e.DownloadOperation;
        var record = new DownloadRecord
        {
            Url = operation.Uri,
            FilePath = e.ResultFilePath,
            FileName = Path.GetFileName(e.ResultFilePath),
            TotalBytes = operation.TotalBytesToReceive is { } total ? (long)total : null,
            Status = DownloadStatus.InProgress,
        };
        _records.Add(record);
        _live[record.Id] = operation;
        HasSessionDownloads = true;

        operation.BytesReceivedChanged += (_, _) =>
        {
            record.ReceivedBytes = operation.BytesReceived;
            RaiseChanged(save: false);
        };
        operation.StateChanged += (_, _) => OnStateChanged(record, operation);
        Log.Info($"İndirme başladı: {record.FileName}");
        RaiseChanged(save: true);
    }

    private void OnStateChanged(DownloadRecord record, CoreWebView2DownloadOperation operation)
    {
        switch (operation.State)
        {
            case CoreWebView2DownloadState.InProgress:
                record.Status = DownloadStatus.InProgress;
                break;

            case CoreWebView2DownloadState.Completed:
                record.Status = DownloadStatus.Completed;
                record.FilePath = operation.ResultFilePath;
                record.FileName = Path.GetFileName(operation.ResultFilePath);
                record.ReceivedBytes = operation.BytesReceived;
                record.TotalBytes = operation.BytesReceived;
                _live.Remove(record.Id);
                Completed?.Invoke(this, record);
                break;

            case CoreWebView2DownloadState.Interrupted:
                switch (operation.InterruptReason)
                {
                    case CoreWebView2DownloadInterruptReason.UserPaused:
                        record.Status = DownloadStatus.Paused;
                        break;
                    case CoreWebView2DownloadInterruptReason.UserCanceled:
                        record.Status = DownloadStatus.Cancelled;
                        _live.Remove(record.Id);
                        break;
                    default:
                        record.Status = DownloadStatus.Failed;
                        record.Error = Describe(operation.InterruptReason);
                        if (!operation.CanResume)
                            _live.Remove(record.Id);
                        break;
                }
                break;
        }

        RaiseChanged(save: true);
    }

    private static string Describe(CoreWebView2DownloadInterruptReason reason) => reason switch
    {
        CoreWebView2DownloadInterruptReason.FileMalicious => L.T("Zararlı olabileceği için engellendi"),
        CoreWebView2DownloadInterruptReason.FileSecurityCheckFailed => L.T("Güvenlik denetimi başarısız oldu"),
        CoreWebView2DownloadInterruptReason.FileBlockedByPolicy => L.T("İlke tarafından engellendi"),
        CoreWebView2DownloadInterruptReason.FileNoSpace => L.T("Diskte yer yok"),
        CoreWebView2DownloadInterruptReason.FileAccessDenied => L.T("Klasöre yazma izni yok"),
        CoreWebView2DownloadInterruptReason.FileNameTooLong or CoreWebView2DownloadInterruptReason.FileTooLarge
            => L.T("Dosya kaydedilemedi"),
        CoreWebView2DownloadInterruptReason.NetworkFailed or CoreWebView2DownloadInterruptReason.NetworkTimeout
            or CoreWebView2DownloadInterruptReason.NetworkDisconnected or CoreWebView2DownloadInterruptReason.NetworkServerDown
            => L.T("Ağ bağlantısı kesildi"),
        CoreWebView2DownloadInterruptReason.UserShutdown => L.T("Sorune kapandığında yarım kaldı"),
        _ when reason.ToString().StartsWith("Server", StringComparison.Ordinal) => L.T("Sunucu hatası"),
        _ => L.T("İndirme başarısız oldu"),
    };

    private DownloadRecord? Find(string? id) => _records.FirstOrDefault(r => r.Id == id);

    private void RaiseChanged(bool save)
    {
        if (save)
        {
            try
            {
                JsonStore.Save(FilePath, _records);
            }
            catch (IOException ex)
            {
                Log.Error("İndirme listesi kaydedilemedi.", ex);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
