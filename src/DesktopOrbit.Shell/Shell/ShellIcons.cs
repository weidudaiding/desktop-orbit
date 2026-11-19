using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Shell;

public static class ShellIcons
{
    private static readonly Guid ShellItemImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static byte[]? TryGetPng(string path, int pixelSize, bool thumbnail) =>
        TryGetPng(path, pixelSize, thumbnail, out _);

    public static byte[]? TryGetPng(string path, int pixelSize, bool thumbnail, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || pixelSize <= 0) return null;

        bool initialized = false;
        try
        {
            int hr = CoInitializeEx(IntPtr.Zero, 0x2);
            if (hr >= 0) initialized = true;
            else if (hr != unchecked((int)0x80010106)) error = $"CoInitializeEx failed 0x{hr:X8}";

            Guid interfaceId = ShellItemImageFactoryId;
            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref interfaceId, out object item);

            if (item is not IShellItemImageFactory factory)
            {
                error = "no IShellItemImageFactory";
                Release(item);
                return null;
            }

            int flags = NativeMethods.SIIGBF_BIGGERSIZEOK | (thumbnail ? NativeMethods.SIIGBF_RESIZETOFIT : NativeMethods.SIIGBF_ICONONLY);
            var size = new SIZE { CX = pixelSize, CY = pixelSize };

            try
            {
                factory.GetImage(size, flags, out IntPtr hBitmap);
                if (hBitmap == IntPtr.Zero)
                {
                    error = "GetImage returned null HBITMAP";
                    return null;
                }

                try
                {
                    byte[]? png = BitmapEncoder.ToPng(hBitmap);
                    if (png is null) error = "bitmap encode failed";
                    return png;
                }
                finally
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
            }
            finally
            {
                Release(item);
            }
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            return null;
        }
        finally
        {
            if (initialized) CoUninitialize();
        }
    }

    private static void Release(object item)
    {
        if (Marshal.IsComObject(item))
        {
            Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int apartment);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
