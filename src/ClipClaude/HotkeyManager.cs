using System.Runtime.InteropServices;

namespace ClipClaude;

public readonly record struct Hotkey(Keys Key, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        Keys key = Keys.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": win = true; break;
                default:
                    if (key != Keys.None) return false;
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Keys.D0 + (raw[0] - '0');
                    else if (!Enum.TryParse(raw, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        if (key == Keys.None || IsModifierKey(key)) return false;
        if (!ctrl && !alt && !win) return false; // глобальный хоткей без модификатора — плохая идея
        hotkey = new Hotkey(key, ctrl, alt, shift, win);
        return true;
    }

    public static bool IsModifierKey(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
        or Keys.LWin or Keys.RWin;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (Key - Keys.D0))).ToString() : Key.ToString());
        return string.Join("+", parts);
    }

    internal uint Modifiers =>
        (Alt ? 0x1u : 0) | (Ctrl ? 0x2u : 0) | (Shift ? 0x4u : 0) | (Win ? 0x8u : 0) | 0x4000u; // MOD_NOREPEAT
}

/// <summary>Глобальные горячие клавиши через RegisterHotKey; сообщения WM_HOTKEY ловит скрытое окно.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams());

    /// <returns>true, если регистрация удалась.</returns>
    public bool Register(Hotkey hotkey, Action handler)
    {
        int id = _nextId++;
        if (!RegisterHotKey(Handle, id, hotkey.Modifiers, (uint)hotkey.Key))
        {
            Log.Error($"RegisterHotKey failed for {hotkey}, win32 error {Marshal.GetLastWin32Error()}");
            return false;
        }
        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys) UnregisterHotKey(Handle, id);
        _handlers.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && _handlers.TryGetValue((int)m.WParam, out var handler))
        {
            handler();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
