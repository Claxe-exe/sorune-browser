using System.IO;
using Claxe.Models;

namespace Claxe.Services;

public static class SettingsStore
{
    public static AppSettings Load()
    {
        var settings = JsonStore.Load<AppSettings>(AppPaths.SettingsFile);
        if (settings is not null)
            return settings;

        // First run (or recovered from a corrupt file): write defaults so the file exists.
        settings = new AppSettings();
        Save(settings);
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            JsonStore.Save(AppPaths.SettingsFile, settings);
        }
        catch (IOException ex)
        {
            Log.Error("Ayarlar kaydedilemedi.", ex);
        }
    }
}
