using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Shell;

/// <summary>
/// Converts an HBITMAP returned by IShellItemImageFactory into PNG bytes.
/// The bitmap is usually 32bpp premultiplied BGRA and bottom-up, so rows are
/// flipped on the way out.
/// </summary>
internal static class BitmapEncoder
{
    public static byte[]? ToPng(IntPtr hBitmap)
    {
        if (NativeMethods.GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0) return null;
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return null;

        int width = bitmap.Width;
        int height = bitmap.Height;

        var header = new BITMAPINFOHEADER
        {
            Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0
        };

        byte[] pixels = new byte[width * height * 4];
        IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return null;

        try
        {
            if (NativeMethods.GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }

        return PngWriter.EncodeArgb32(pixels, width, height);
    }
}
