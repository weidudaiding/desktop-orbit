using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Platform;

public sealed class HotkeyRegistration : IDisposable
{
    public const int HotkeyId = 0xB0B1;

    private readonly IntPtr _window;
    private bool _registered;

    public HotkeyRegistration(IntPtr window)
    {
        _window = window;
    }

    public bool IsRegistered => _registered;

    public bool TryRegister(string chord)
    {
        Unregister();

        if (!HotkeyParser.TryParse(chord, out uint modifiers, out uint virtualKey))
        {
            return false;
        }

        _registered = NativeMethods.RegisterHotKey(_window, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_window, HotkeyId);
        _registered = false;
    }

    public void Dispose() => Unregister();
}

public static class HotkeyParser
{
    private static readonly Dictionary<string, uint> Keys = BuildKeyTable();

    public static bool TryParse(string? chord, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(chord)) return false;

        string[] parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (string part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= NativeMethods.MOD_ALT; continue;
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; continue;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; continue;
                case "win" or "meta": modifiers |= NativeMethods.MOD_WIN; continue;
            }

            if (!Keys.TryGetValue(part, out uint vk)) return false;
            virtualKey = vk;
        }

        return virtualKey != 0;
    }

    private static Dictionary<string, uint> BuildKeyTable()
    {
        var table = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        for (char c = 'A'; c <= 'Z'; c++)
        {
            table[c.ToString()] = c;
        }

        for (char c = '0'; c <= '9'; c++)
        {
            table[c.ToString()] = c;
        }

        for (int i = 1; i <= 24; i++)
        {
            table["f" + i] = (uint)(0x70 + i - 1);
        }

        table["space"] = 0x20;
        table["enter"] = 0x0D;
        table["esc"] = 0x1B;
        table["tab"] = 0x09;
        table["backspace"] = 0x08;
        table["insert"] = 0x2D;
        table["delete"] = 0x2E;
        table["home"] = 0x24;
        table["end"] = 0x23;
        table["pageup"] = 0x21;
        table["pagedown"] = 0x22;
        table["left"] = 0x25;
        table["up"] = 0x26;
        table["right"] = 0x27;
        table["down"] = 0x28;
        table["`"] = 0xC0;
        table["-"] = 0xBD;
        table["="] = 0xBB;
        table["["] = 0xDB;
        table["]"] = 0xDD;
        table["\\"] = 0xDC;
        table[";"] = 0xBA;
        table["'"] = 0xDE;
        table[","] = 0xBC;
        table["."] = 0xBE;
        table["/"] = 0xBF;

        return table;
    }
}
