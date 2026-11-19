using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Desktop;

public enum BlankSpotResult
{
    Blank,
    OnIcon,
    Unsupported,
    Failed
}

/// <summary>
/// Uses LVM_HITTEST against Explorer's desktop list view to tell a blank area
/// apart from an icon. The struct must live in Explorer's address space, so this
/// performs a cross-process buffer round trip. If anything is blocked (AV/EDR,
/// hardened Explorer) it permanently degrades to Unsupported and the caller
/// falls back to time/movement heuristics.
/// </summary>
public sealed class DesktopHitTester
{
    private readonly DesktopLocator _locator;
    private volatile bool _probeFailed;

    public DesktopHitTester(DesktopLocator locator)
    {
        _locator = locator;
    }

    public bool IsPreciseAvailable => !_probeFailed;

    public BlankSpotResult QueryBlankSpot(int screenX, int screenY)
    {
        if (_probeFailed) return BlankSpotResult.Unsupported;

        IntPtr listView = _locator.FindDesktopListView();
        if (listView == IntPtr.Zero) return BlankSpotResult.Unsupported;

        var point = new POINT(screenX, screenY);
        if (!ScreenToClient(listView, ref point))
        {
            return BlankSpotResult.Failed;
        }

        NativeMethods.GetWindowThreadProcessId(listView, out uint pid);
        if (pid == 0) return BlankSpotResult.Failed;

        IntPtr process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
            false,
            pid);

        if (process == IntPtr.Zero)
        {
            _probeFailed = true;
            return BlankSpotResult.Unsupported;
        }

        int structSize = Marshal.SizeOf<LVHITTESTINFO>();
        IntPtr remote = IntPtr.Zero;
        IntPtr local = IntPtr.Zero;

        try
        {
            remote = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, (IntPtr)structSize,
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);

            if (remote == IntPtr.Zero)
            {
                _probeFailed = true;
                return BlankSpotResult.Unsupported;
            }

            var request = new LVHITTESTINFO { Pt = point, Flags = 0, Item = -1, SubItem = -1, Group = -1 };

            local = Marshal.AllocHGlobal(structSize);
            Marshal.StructureToPtr(request, local, false);

            byte[] payload = new byte[structSize];
            Marshal.Copy(local, payload, 0, structSize);

            if (!NativeMethods.WriteProcessMemory(process, remote, payload, (IntPtr)structSize, out _))
            {
                return BlankSpotResult.Failed;
            }

            NativeMethods.SendMessageTimeout(listView, NativeMethods.LVM_HITTEST, IntPtr.Zero, remote, 2u, 250u, out _);

            byte[] response = new byte[structSize];
            if (!NativeMethods.ReadProcessMemory(process, remote, response, (IntPtr)structSize, out _))
            {
                return BlankSpotResult.Failed;
            }

            Marshal.Copy(response, 0, local, structSize);
            LVHITTESTINFO info = Marshal.PtrToStructure<LVHITTESTINFO>(local);
            return info.Item < 0 ? BlankSpotResult.Blank : BlankSpotResult.OnIcon;
        }
        catch (Exception)
        {
            _probeFailed = true;
            return BlankSpotResult.Unsupported;
        }
        finally
        {
            if (local != IntPtr.Zero) Marshal.FreeHGlobal(local);
            if (remote != IntPtr.Zero) NativeMethods.VirtualFreeEx(process, remote, IntPtr.Zero, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(process);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);
}
