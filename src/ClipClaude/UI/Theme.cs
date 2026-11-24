using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClipClaude.UI;

/// <summary>Светлая/тёмная тема по системной настройке Windows.</summary>
internal static class Theme
{
    public static readonly Font UiFont = new("Segoe UI", 9.75f);
    public static readonly Font TextFont = new("Segoe UI", 10.5f);

    public static bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static Color Back => IsDark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
    public static Color Fore => IsDark ? Color.FromArgb(235, 235, 235) : SystemColors.ControlText;
    public static Color InputBack => IsDark ? Color.FromArgb(45, 45, 45) : SystemColors.Window;
    public static Color ReadOnlyBack => IsDark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(245, 245, 245);
    public static Color MutedFore => IsDark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(96, 96, 96);
    public static Color Highlight => IsDark ? Color.FromArgb(92, 74, 18) : Color.FromArgb(255, 236, 153);

    public static void Apply(Form form)
    {
        form.Font = UiFont;
        bool dark = IsDark;
        if (!dark) return;

        form.BackColor = Back;
        form.ForeColor = Fore;
        ApplyRecursive(form.Controls);
        form.HandleCreated += (_, _) => UseDarkTitleBar(form.Handle);
        if (form.IsHandleCreated) UseDarkTitleBar(form.Handle);
    }

    private static void ApplyRecursive(Control.ControlCollection controls)
    {
        foreach (Control c in controls)
        {
            switch (c)
            {
                case TextBoxBase tb:
                    tb.BackColor = tb.ReadOnly ? ReadOnlyBack : InputBack;
                    tb.ForeColor = Fore;
                    break;
                case NumericUpDown or ComboBox:
                    c.BackColor = InputBack;
                    c.ForeColor = Fore;
                    break;
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
                    b.BackColor = Color.FromArgb(55, 55, 55);
                    b.ForeColor = Fore;
                    break;
                default:
                    c.BackColor = Back;
                    c.ForeColor = Fore;
                    break;
            }
            if (c.HasChildren) ApplyRecursive(c.Controls);
        }
    }

    private static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Показать окно поверх остальных и отдать ему фокус, не делая его постоянно TopMost.</summary>
    public static void BringToFront(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.TopMost = true;
        if (!form.Visible) form.Show();
        form.Activate();
        SetForegroundWindow(form.Handle);
        form.TopMost = false;
    }
}
