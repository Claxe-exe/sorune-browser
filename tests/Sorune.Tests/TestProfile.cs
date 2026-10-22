using System.IO;
using System.Runtime.CompilerServices;

namespace Sorune.Tests;

/// <summary>
/// Points Sorune's data folder at a throw-away directory before any test touches it, so tests
/// never read or write a real profile.
/// </summary>
internal static class TestProfile
{
    /// <summary>Tests that read or write the shared profile files run one after another.</summary>
    public const string DataRootCollection = "Profile files";

    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "sorune-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Folder);
        Environment.SetEnvironmentVariable("SORUNE_PROFILE_DIR", Folder);
    }

    public static string NewFolder()
    {
        var folder = Path.Combine(Folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
