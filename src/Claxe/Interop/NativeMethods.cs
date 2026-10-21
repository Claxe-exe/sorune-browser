using System.Runtime.InteropServices;

namespace Claxe.Interop;

internal static partial class NativeMethods
{
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCMOUSEMOVE = 0x00A0;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCMOUSELEAVE = 0x02A2;

    /// <summary>Reporting this for our maximize button is what makes Windows 11 show Snap Layouts.</summary>
    public const int HTMAXBUTTON = 9;

    public const int SM_CXSIZEFRAME = 32;
    public const int SM_CXPADDEDBORDER = 92;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeServerProcessId(nint pipe, out uint processId);

    /// <summary>Lets the already running Claxe bring its window to the front when we hand it a link.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    public static int GetXLParam(nint lParam) => unchecked((short)(long)lParam);

    public static int GetYLParam(nint lParam) => unchecked((short)((long)lParam >> 16));
}
