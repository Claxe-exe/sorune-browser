using System.IO;
using Claxe.Models;

namespace Claxe.Services;

public static class SessionStore
{
    private static bool _ownsMarker;

    private static string MarkerFile => Path.Combine(AppPaths.DataRoot, "running.lock");

    /// <summary>When the saved session was last written (local time), or null when there is none.</summary>
    public static DateTime? LastSavedAt => File.Exists(AppPaths.SessionFile) ? File.GetLastWriteTime(AppPaths.SessionFile) : null;

    /// <summary>True when the previous run never reached a normal exit (crash, kill, power loss).</summary>
    public static bool PreviousRunCrashed { get; private set; }

    /// <summary>
    /// Never throws: a missing, corrupt (moved aside by JsonStore) or unreadable file just means
    /// there is nothing to restore, so a bad session can never keep Sorune from starting.
    /// </summary>
    public static SessionState? Load()
    {
        try
        {
            return JsonStore.Load<SessionState>(AppPaths.SessionFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Oturum dosyası okunamadı.", ex);
            return null;
        }
    }

    /// <summary>Used when session restore is switched off, so stale tabs never come back later.</summary>
    public static void Delete()
    {
        try
        {
            File.Delete(AppPaths.SessionFile);
        }
        catch (IOException ex)
        {
            Log.Error("Oturum dosyası silinemedi.", ex);
        }
    }

    public static void Save(SessionState state)
    {
        try
        {
            JsonStore.Save(AppPaths.SessionFile, state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Oturum kaydedilemedi.", ex);
        }
    }

    /// <summary>Called once at startup by the running instance: notes a leftover marker, then sets its own.</summary>
    public static void MarkRunning()
    {
        try
        {
            PreviousRunCrashed = File.Exists(MarkerFile);
            File.WriteAllText(MarkerFile, Environment.ProcessId.ToString());
            _ownsMarker = true;
            if (PreviousRunCrashed)
                Log.Info("Önceki çalışma düzgün kapanmamış.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Çalışma işareti yazılamadı.", ex);
        }
    }

    /// <summary>Normal exit: removes the marker (only the instance that wrote it).</summary>
    public static void MarkCleanExit()
    {
        if (!_ownsMarker)
            return;

        try
        {
            File.Delete(MarkerFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Çalışma işareti silinemedi.", ex);
        }
    }
}
