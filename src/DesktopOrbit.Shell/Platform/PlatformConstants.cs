namespace DesktopOrbit.Shell.Platform;

/// <summary>
/// Public constants the UI layer needs without opening up the whole interop
/// surface of NativeMethods.
/// </summary>
public static class PlatformConstants
{
    public const int VkLeftButton = 0x01;
    public const int VkRightButton = 0x02;
    public const int VkMiddleButton = 0x04;
    public const int WmHotkey = 0x0312;

    public static IntPtr MessageWindowParent => new(-3);
}
