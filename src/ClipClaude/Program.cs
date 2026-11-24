namespace ClipClaude;

internal static class Program
{
    private const string MutexName = @"Local\ClipClaude.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        ApplicationConfiguration.Initialize();

        if (!createdNew)
        {
            ShowAlreadyRunning();
            return;
        }

        Log.Init();
        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.Run(new TrayApp());
    }

    private static void ShowAlreadyRunning()
    {
        using var icon = new NotifyIcon { Icon = AppIcons.Load(AppIcons.App), Text = "ClipClaude", Visible = true };
        icon.ShowBalloonTip(3000, "ClipClaude", "Уже запущено", ToolTipIcon.Info);
        using var timer = new System.Windows.Forms.Timer { Interval = 3500 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
        icon.Visible = false;
    }
}
