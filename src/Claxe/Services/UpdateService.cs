using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Claxe.Services;

public enum UpdateState
{
    /// <summary>Running from a build folder, not from the installer: nothing to update.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpToDate,
    Downloading,
    ReadyToRestart,
    Failed,
}

/// <summary>
/// Updates through Velopack from the GitHub repository's releases. Checks quietly once
/// after startup, downloads in the background and lets the user restart when convenient.
/// </summary>
public static class UpdateService
{
    /// <summary>Where releases are published (vpk upload github / gh release).</summary>
    public const string ReleasesUrl = "https://github.com/Claxe-exe/claxe-browser";

    private static readonly UpdateManager Manager = new(new GithubSource(ReleasesUrl, accessToken: null, prerelease: false));
    private static UpdateInfo? _pending;

    public static event EventHandler? StateChanged;

    public static UpdateState State { get; private set; } = Manager.IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled;

    public static string? AvailableVersion { get; private set; }

    public static string? Error { get; private set; }

    public static string CurrentVersion => Manager.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static async Task CheckAsync()
    {
        if (State is UpdateState.NotInstalled or UpdateState.Checking or UpdateState.Downloading or UpdateState.ReadyToRestart)
            return;

        try
        {
            Set(UpdateState.Checking);
            var update = await Manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Set(UpdateState.UpToDate);
                return;
            }

            AvailableVersion = update.TargetFullRelease.Version.ToString();
            Set(UpdateState.Downloading);
            await Manager.DownloadUpdatesAsync(update);
            _pending = update;
            Log.Info($"Güncelleme indirildi: {AvailableVersion}");
            Set(UpdateState.ReadyToRestart);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.IO.IOException or TaskCanceledException)
        {
            // Offline, GitHub unreachable, or the releases aren't public: not worth bothering the user.
            Error = ex.Message;
            Log.Info($"Güncelleme denetlenemedi: {ex.Message}");
            Set(UpdateState.Failed);
        }
    }

    /// <summary>Closes Claxe, installs the downloaded version and starts it again.</summary>
    public static void RestartToApply()
    {
        if (_pending is not null)
            Manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    private static void Set(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(null, EventArgs.Empty);
    }
}
