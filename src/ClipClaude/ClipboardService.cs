using System.Runtime.InteropServices;

namespace ClipClaude;

internal enum ClipboardReadStatus
{
    Ok,
    NoText,
    Busy,
}

/// <summary>Работа с буфером обмена (только из UI/STA-потока), до 3 попыток с паузой 100 мс.</summary>
internal static class ClipboardService
{
    private const int Attempts = 3;

    public static ClipboardReadStatus TryGetText(out string text)
    {
        text = "";
        for (int i = 0; i < Attempts; i++)
        {
            try
            {
                if (!Clipboard.ContainsText()) return ClipboardReadStatus.NoText;
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
                return string.IsNullOrWhiteSpace(text) ? ClipboardReadStatus.NoText : ClipboardReadStatus.Ok;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return ClipboardReadStatus.Busy;
    }

    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < Attempts; i++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        Log.Error("Clipboard write failed");
        return false;
    }
}
