using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Platform;

public sealed record TrayMenuItem(int Id, string Text, bool Checked = false, bool Enabled = true, bool SeparatorBefore = false);

/// <summary>
/// Minimal Win32 tray icon. Deliberately avoids WinForms so the WPF app keeps a
/// single UI stack and does not pay the extra framework load at startup.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public const int WmTrayCallback = 0x8000 + 42;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;

    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_NULL = 0x0000;

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_UNCHECKED = 0x0000;
    private const uint MF_GRAYED = 0x0001;

    private readonly IntPtr _window;
    private IntPtr _icon;
    private bool _added;
    private string _tooltip = "Desktop Orbit";

    public TrayIcon(IntPtr window)
    {
        _window = window;
        _icon = PickIcon();
    }

    /// <summary>
    /// Prefers the icon embedded in this executable, then orbit.ico next to it, and
    /// finally the drawn ring so the tray never ends up blank.
    /// </summary>
    private static IntPtr PickIcon()
    {
        IntPtr embedded = ExtractAppIcon();
        if (embedded != IntPtr.Zero) return embedded;

        IntPtr fromFile = LoadIconFile(IconFilePath);
        if (fromFile != IntPtr.Zero) return fromFile;

        return CreateRingIcon(32);
    }

    /// <summary>
    /// Preferred source: the icon embedded in this executable by the build. A
    /// single-file publish leaves no .ico next to the exe, so extracting from the
    /// image keeps the tray icon correct without shipping a second file.
    /// </summary>
    private static IntPtr ExtractAppIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return IntPtr.Zero;

            return ExtractIconEx(exe, 0, out IntPtr large, out _, 1) > 0 && large != IntPtr.Zero
                ? large
                : IntPtr.Zero;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Development fallback: load orbit.ico straight from the output folder.
    /// </summary>
    private static IntPtr LoadIconFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return IntPtr.Zero;

            IntPtr handle = LoadImage(IntPtr.Zero, path, 1 /* IMAGE_ICON */, 0, 0, 0x10 /* LR_LOADFROMFILE */);
            return handle == IntPtr.Zero ? IntPtr.Zero : handle;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static string IconFilePath => Path.Combine(AppContext.BaseDirectory, "orbit.ico");

    public event Action? Activated;
    public event Action? MenuRequested;
    public event Action<int>? CommandInvoked;

    public bool Add()
    {
        if (_added) return true;

        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WmTrayCallback;
        data.hIcon = _icon;
        data.szTip = _tooltip;

        _added = ShellNotifyIcon(NIM_ADD, ref data);
        return _added;
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (!_added) return;
        var data = CreateData();
        data.uFlags = NIF_TIP;
        data.szTip = _tooltip;
        ShellNotifyIcon(NIM_MODIFY, ref data);
    }

    public bool HandleMessage(int message, IntPtr lParam)
    {
        if (message != WmTrayCallback) return false;

        int mouseMessage = lParam.ToInt32();
        if (mouseMessage is WM_LBUTTONUP or 0x0203) // LBUTTONDBLCLK
        {
            Activated?.Invoke();
            return true;
        }

        if (mouseMessage is WM_RBUTTONUP or WM_CONTEXTMENU)
        {
            MenuRequested?.Invoke();
            return true;
        }

        return false;
    }

    public void ShowContextMenu(IReadOnlyList<TrayMenuItem>? items = null)
    {
        if (items is null || items.Count == 0) return;

        SetForegroundWindow(_window);
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            foreach (TrayMenuItem item in items)
            {
                if (item.SeparatorBefore)
                {
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                }

                uint flags = MF_STRING;
                flags |= item.Checked ? MF_CHECKED : MF_UNCHECKED;
                if (!item.Enabled) flags |= MF_GRAYED;

                AppendMenu(menu, flags, (IntPtr)item.Id, item.Text);
            }

            GetCursorPos(out POINT point);
            int command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _window, IntPtr.Zero);
            if (command > 0)
            {
                CommandInvoked?.Invoke(command);
            }
        }
        finally
        {
            DestroyMenu(menu);
            PostMessage(_window, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window,
        uID = 1,
        szTip = _tooltip
    };

    private static IntPtr CreateRingIcon(int size)
    {
        byte[] pixels = new byte[size * size * 4];
        double center = (size - 1) / 2.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center;
                double dy = y - center;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                double ring = Band(distance, 9.5, 14.0, 1.1);
                double core = Band(distance, 0.0, 4.6, 1.0);

                double alpha = Math.Max(ring, core);
                if (alpha <= 0.01) continue;

                byte r = core > ring ? (byte)0x39 : (byte)0x7C;
                byte g = core > ring ? (byte)0xC6 : (byte)0x5C;
                byte b = core > ring ? (byte)0xFF : (byte)0xFF;

                int index = (y * size + x) * 4;
                double a = Math.Min(1.0, alpha);
                pixels[index] = (byte)(b * a);
                pixels[index + 1] = (byte)(g * a);
                pixels[index + 2] = (byte)(r * a);
                pixels[index + 3] = (byte)(a * 255);
            }
        }

        byte[] mask = new byte[size * size / 8];
        IntPtr colorBitmap = CreateBitmap(size, size, 1, 32, pixels);
        IntPtr maskBitmap = CreateBitmap(size, size, 1, 1, mask);
        if (colorBitmap == IntPtr.Zero || maskBitmap == IntPtr.Zero)
        {
            if (colorBitmap != IntPtr.Zero) DeleteObject(colorBitmap);
            if (maskBitmap != IntPtr.Zero) DeleteObject(maskBitmap);
            return IntPtr.Zero;
        }

        var info = new ICONINFO
        {
            IsIcon = true,
            MaskBitmap = maskBitmap,
            ColorBitmap = colorBitmap
        };

        IntPtr icon = CreateIconIndirect(ref info);
        DeleteObject(colorBitmap);
        DeleteObject(maskBitmap);
        return icon;
    }

    private static double Band(double value, double inner, double outer, double feather)
    {
        double rise = Clamp((value - inner) / feather);
        double fall = Clamp((outer - value) / feather);
        return Math.Min(rise, fall);
    }

    private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool ShellNotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr itemId, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint flags);

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData();
            ShellNotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
