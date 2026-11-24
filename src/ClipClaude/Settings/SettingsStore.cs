using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClipClaude;

public sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class AppSettings
{
    public string ClaudePath { get; set; } = "";
    public string Model { get; set; } = "";
    public string SpellcheckHotkey { get; set; } = "Ctrl+Alt+S";
    public string TranslateHotkey { get; set; } = "Ctrl+Alt+T";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxTextLength { get; set; } = 20000;
    public bool AutoCopyResult { get; set; }
    public bool ShowResultWindow { get; set; } = true;
    public string? SpellcheckPrompt { get; set; }
    public string? TranslatePrompt { get; set; }
    public WindowBounds? ResultWindow { get; set; }

    private static readonly Regex ModelPattern = new(@"^[A-Za-z0-9._:\[\]\-]*$", RegexOptions.CultureInvariant);

    /// <summary>Имя модели передаётся в командную строку, поэтому допускаются только безопасные символы.</summary>
    public static bool IsValidModel(string? model) => ModelPattern.IsMatch(model ?? "");

    public void Normalize()
    {
        ClaudePath = (ClaudePath ?? "").Trim().Trim('"');
        Model = (Model ?? "").Trim();
        if (!IsValidModel(Model)) Model = "";
        SpellcheckHotkey ??= "Ctrl+Alt+S";
        TranslateHotkey ??= "Ctrl+Alt+T";
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 3600);
        MaxTextLength = Math.Clamp(MaxTextLength, 100, 1_000_000);
    }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipClaude", "settings.json");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Settings load failed, using defaults", ex);
            settings = new AppSettings();
        }
        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Settings save failed", ex);
        }
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipClaude";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Autostart change failed", ex);
            MessageBox.Show("Не удалось изменить автозапуск: " + ex.Message, "ClipClaude",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
