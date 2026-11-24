namespace ClipClaude.UI;

public sealed record ResultData(ClaudeAction Action, string Source, string Result, TimeSpan Elapsed);

internal sealed class ResultForm : Form
{
    private readonly RichTextBox _result;
    private readonly AppSettings _settings;
    private readonly Action<WindowBounds> _saveBounds;

    public ResultData Data { get; }

    /// <summary>Нажата «Повторить»: TrayApp перезапускает действие с тем же исходным текстом.</summary>
    public event Action<ResultData>? RetryRequested;

    public ResultForm(ResultData data, AppSettings settings, Action<WindowBounds> saveBounds)
    {
        Data = data;
        _settings = settings;
        _saveBounds = saveBounds;

        Text = Prompts.Title(data.Action) + " — ClipClaude";
        Icon = AppIcons.LoadLarge();
        ShowInTaskbar = true;
        KeyPreview = true;
        MinimumSize = new Size(480, 360);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(820, 620);

        var source = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = Theme.TextFont,
            Text = data.Source.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };

        _result = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = Theme.TextFont,
            DetectUrls = false,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        var status = new Label { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        var copy = new Button { Text = "Копировать", AutoSize = true };
        var copyClose = new Button { Text = "Копировать и закрыть", AutoSize = true };
        var retry = new Button { Text = "Повторить", AutoSize = true };
        var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.Cancel };
        copy.Click += (_, _) => Copy();
        copyClose.Click += (_, _) => { if (Copy()) Close(); };
        retry.Click += (_, _) => { RetryRequested?.Invoke(Data); };
        close.Click += (_, _) => Close();
        CancelButton = close;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
        };
        buttons.Controls.AddRange(new Control[] { close, retry, copyClose, copy });

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Исходный текст", AutoSize = true, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
        layout.Controls.Add(source, 0, 1);
        layout.Controls.Add(new Label { Text = "Результат (можно отредактировать)", AutoSize = true, Margin = new Padding(0, 10, 0, 4) }, 0, 2);
        layout.Controls.Add(_result, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        layout.Controls.Add(status, 0, 5);
        Controls.Add(layout);

        Theme.Apply(this);
        _result.BackColor = Theme.InputBack;
        _result.ForeColor = Theme.Fore;
        status.ForeColor = Theme.MutedFore;

        _result.Text = data.Result.Replace("\r\n", "\n");
        var statusText = $"Время выполнения: {data.Elapsed.TotalSeconds:F1} с";
        if (data.Action == ClaudeAction.Spellcheck)
            statusText = HighlightChanges() + "   ·   " + statusText;
        status.Text = statusText;

        ApplySavedBounds();
        Shown += (_, _) => { _result.SelectionStart = 0; _result.SelectionLength = 0; _result.Focus(); };
    }

    private string HighlightChanges()
    {
        // RichTextBox хранит переводы строк как \n — индексы совпадают с нормализованным текстом.
        var original = Data.Source.Replace("\r\n", "\n");
        var diff = DiffHighlighter.Compute(original, _result.Text);
        foreach (var (start, length) in diff.ChangedRanges)
        {
            _result.Select(start, length);
            _result.SelectionBackColor = Theme.Highlight;
        }
        _result.Select(0, 0);
        return diff.ChangeCount == 0 ? "Ошибок не найдено" : $"Исправлений: {diff.ChangeCount}";
    }

    private bool Copy()
    {
        var text = _result.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        if (ClipboardService.TrySetText(text)) return true;
        MessageBox.Show(this, "Не удалось записать в буфер обмена: он занят другим приложением.", "ClipClaude",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Control)
        {
            e.Handled = e.SuppressKeyPress = true;
            if (Copy()) Close();
            return;
        }
        base.OnKeyDown(e);
    }

    private void ApplySavedBounds()
    {
        var b = _settings.ResultWindow;
        if (b is null || b.Width < MinimumSize.Width || b.Height < MinimumSize.Height) return;
        var rect = new Rectangle(b.X, b.Y, b.Width, b.Height);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = rect;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _saveBounds(new WindowBounds { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height });
        base.OnFormClosing(e);
    }
}
