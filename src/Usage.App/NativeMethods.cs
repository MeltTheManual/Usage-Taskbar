using System.Runtime.InteropServices;
using System.Text;

namespace Usage.App;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTopmost = 0x00000008;

    public static readonly nint HwndTopmost = new(-1);

    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpHideWindow = 0x0080;
    public const uint SwpNoZOrder = 0x0004;

    public const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// True when the foreground window covers a whole monitor, which is our cue to get out of the way.
    /// A topmost chip drawing over a game or a full-screen video would be worse than briefly missing.
    /// </summary>
    public static bool ForegroundIsFullScreen()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == 0)
            {
                return false;
            }

            var name = new StringBuilder(256);
            GetClassName(foreground, name, name.Capacity);
            var className = name.ToString();

            // The desktop and the shell itself are always "full screen" and are not something to hide from.
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            if (!GetWindowRect(foreground, out var window))
            {
                return false;
            }

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            return window.Left <= info.rcMonitor.Left
                && window.Top <= info.rcMonitor.Top
                && window.Right >= info.rcMonitor.Right
                && window.Bottom >= info.rcMonitor.Bottom;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
