using ClipClaude.UI;

namespace ClipClaude;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _path = new() { Width = 300 };
    private readonly TextBox _model = new() { Width = 200, PlaceholderText = "по умолчанию CLI (напр. sonnet, haiku)" };
    private readonly HotkeyBox _spellKey = new();
    private readonly HotkeyBox _translateKey = new();
    private readonly NumericUpDown _timeout = new() { Minimum = 5, Maximum = 3600, Width = 90 };
    private readonly NumericUpDown _maxLen = new() { Minimum = 100, Maximum = 1_000_000, Increment = 1000, Width = 110, ThousandsSeparator = true };
    private readonly CheckBox _autoCopy = new() { Text = "Сразу копировать результат в буфер", AutoSize = true };
    private readonly CheckBox _showWindow = new() { Text = "Показывать окно результата", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Запускать вместе с Windows", AutoSize = true };

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Настройки — ClipClaude";
        Icon = AppIcons.LoadLarge();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var browse = new Button { Text = "Обзор…", AutoSize = true };
        browse.Click += (_, _) => Browse();
        var check = new Button { Text = "Проверить", AutoSize = true };
        check.Click += async (_, _) => await CheckCli(check);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(14), Dock = DockStyle.Fill };
        void Row(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 4) });
            grid.Controls.Add(control);
        }
        Row("Путь к claude:", Flow(_path, browse, check));
        grid.Controls.Add(new Label());
        grid.Controls.Add(new Label { Text = "Пусто — искать claude.exe / claude.cmd в PATH", AutoSize = true, ForeColor = Theme.MutedFore });
        Row("Модель:", _model);
        Row("Орфография:", _spellKey);
        Row("Перевод:", _translateKey);
        Row("Таймаут, с:", _timeout);
        Row("Макс. длина текста:", _maxLen);
        grid.Controls.Add(new Label());
        grid.Controls.Add(Stack(_autoCopy, _showWindow, _autostart));

        var ok = new Button { Text = "Сохранить", AutoSize = true };
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        AcceptButton = ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        root.Controls.Add(grid);
        root.Controls.Add(buttons);
        Controls.Add(root);

        _path.Text = settings.ClaudePath;
        _model.Text = settings.Model;
        _spellKey.Text = settings.SpellcheckHotkey;
        _translateKey.Text = settings.TranslateHotkey;
        _timeout.Value = settings.TimeoutSeconds;
        _maxLen.Value = settings.MaxTextLength;
        _autoCopy.Checked = settings.AutoCopyResult;
        _showWindow.Checked = settings.ShowResultWindow;
        _autostart.Checked = Autostart.IsEnabled();

        Theme.Apply(this);
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        p.Controls.AddRange(controls);
        return p;
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 8, 0, 0) };
        p.Controls.AddRange(controls);
        return p;
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog { Filter = "Claude CLI (claude.exe;claude.cmd)|claude.exe;claude.cmd|Все файлы|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.FileName;
    }

    private async Task CheckCli(Button button)
    {
        button.Enabled = false;
        try
        {
            var path = _path.Text.Trim().Trim('"');
            var result = await ClaudeCliRunner.GetVersionAsync(path);
            var exe = ClaudeCliRunner.ResolveExecutable(path);
            if (result.Outcome == CliOutcome.Success)
                MessageBox.Show(this, $"{exe}\n\n{result.Output}", "Claude CLI найден", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (result.Outcome == CliOutcome.NotFound)
                MessageBox.Show(this, "Claude CLI не найден. Укажите путь к claude.exe или claude.cmd.", "ClipClaude", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, $"{exe}\n\nОшибка ({result.Outcome}):\n{result.Stderr}", "ClipClaude", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private void Save()
    {
        var path = _path.Text.Trim().Trim('"');
        if (path.Length > 0 && !File.Exists(path))
        {
            Warn("Файл claude по указанному пути не найден.", _path);
            return;
        }
        if (!AppSettings.IsValidModel(_model.Text.Trim()))
        {
            Warn("Имя модели может содержать только латинские буквы, цифры и символы . _ - : [ ]", _model);
            return;
        }
        if (!Hotkey.TryParse(_spellKey.Text, out var spell) || !Hotkey.TryParse(_translateKey.Text, out var tr))
        {
            Warn("Горячая клавиша должна содержать Ctrl, Alt или Win и обычную клавишу.", _spellKey);
            return;
        }
        if (spell == tr)
        {
            Warn("Горячие клавиши для двух действий совпадают.", _translateKey);
            return;
        }

        _settings.ClaudePath = path;
        _settings.Model = _model.Text.Trim();
        _settings.SpellcheckHotkey = spell.ToString();
        _settings.TranslateHotkey = tr.ToString();
        _settings.TimeoutSeconds = (int)_timeout.Value;
        _settings.MaxTextLength = (int)_maxLen.Value;
        _settings.AutoCopyResult = _autoCopy.Checked;
        _settings.ShowResultWindow = _showWindow.Checked;
        if (_autostart.Checked != Autostart.IsEnabled()) Autostart.Set(_autostart.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string message, Control focus)
    {
        MessageBox.Show(this, message, "ClipClaude", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }
}

/// <summary>Поле, в котором нажатое сочетание клавиш превращается в строку вида «Ctrl+Alt+S».</summary>
internal sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ReadOnly = true;
        Width = 160;
        ShortcutsEnabled = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.Tab) { e.Handled = false; return; }
        if (Hotkey.IsModifierKey(e.KeyCode)) return;
        var hk = new Hotkey(e.KeyCode, e.Control, e.Alt, e.Shift, false);
        if (e.Control || e.Alt) Text = hk.ToString();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Не даём Alt+клавише активировать мнемоники формы, пока фокус в поле.
        var key = keyData & Keys.KeyCode;
        if ((keyData & (Keys.Control | Keys.Alt)) != 0 && !Hotkey.IsModifierKey(key))
        {
            OnKeyDown(new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
