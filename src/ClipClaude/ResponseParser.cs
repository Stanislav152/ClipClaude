namespace ClipClaude;

public static class ResponseParser
{
    /// <summary>Обрезает пробелы по краям и случайно попавшие обрамляющие теги &lt;text&gt;.</summary>
    public static string Clean(string? output)
    {
        var s = (output ?? string.Empty).Trim();
        if (s.StartsWith("<text>", StringComparison.OrdinalIgnoreCase))
            s = s["<text>".Length..].TrimStart();
        if (s.EndsWith("</text>", StringComparison.OrdinalIgnoreCase))
            s = s[..^"</text>".Length].TrimEnd();
        return s;
    }
}
