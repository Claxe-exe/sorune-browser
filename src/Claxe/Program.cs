using Velopack;

namespace Claxe;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: when started by the installer/updater (install, update, uninstall hooks)
        // Velopack does its work here and exits. Normal launches continue below.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
