using System.IO;
using Claxe.Models;

namespace Claxe.Services;

public static class SettingsStore
{
    public static AppSettings Load()
    {
        // Missing keys get their defaults; saving right away writes them into the file,
        // so settings added in newer versions show up for hand editing too.
        var settings = JsonStore.Load<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
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
