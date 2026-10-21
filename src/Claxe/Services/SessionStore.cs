using System.IO;
using Claxe.Models;

namespace Claxe.Services;

public static class SessionStore
{
    public static SessionState? Load() => JsonStore.Load<SessionState>(AppPaths.SessionFile);

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
        catch (IOException ex)
        {
            Log.Error("Oturum kaydedilemedi.", ex);
        }
    }
}
