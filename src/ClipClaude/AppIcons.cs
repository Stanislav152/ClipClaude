namespace ClipClaude;

internal static class AppIcons
{
    public const string App = "ClipClaude.app.ico";
    public const string Busy = "ClipClaude.busy.ico";

    public static Icon Load(string resourceName)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource {resourceName} not found");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public static Icon LoadLarge()
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream(App)!;
        return new Icon(stream, SystemInformation.IconSize);
    }
}
