using System.IO;
using Claxe.Models;

namespace Claxe.Services;

public static class SessionStore
{
    public static SessionState? Load() => JsonStore.Load<SessionState>(AppPaths.SessionFile);

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
