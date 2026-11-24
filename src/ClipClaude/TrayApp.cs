using ClipClaude.UI;

namespace ClipClaude;

internal sealed class TrayApp : ApplicationContext
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly Icon _idleIcon = AppIcons.Load(AppIcons.App);
    private readonly Icon _busyIcon = AppIcons.Load(AppIcons.Busy);
    private readonly HotkeyManager _hotkeys = new();
    private readonly ToolStripMenuItem _spellItem;
    private readonly ToolStripMenuItem _translateItem;
    private readonly ToolStripMenuItem _cancelItem;
    private readonly ToolStripMenuItem _lastItem;
    private readonly ToolStripMenuItem _autostartItem;

    private CancellationTokenSource? _running;
    private ResultData? _last;
    private ResultForm? _resultForm;
    private SettingsForm? _settingsForm;

    public TrayApp()
    {
        _settings = _store.Load();

        _spellItem = new ToolStripMenuItem("Проверить орфографию", null, (_, _) => Run(ClaudeAction.Spellcheck));
        _translateItem = new ToolStripMenuItem("Перевести на английский", null, (_, _) => Run(ClaudeAction.Translate));
        _cancelItem = new ToolStripMenuItem("Отменить запрос", null, (_, _) => _running?.Cancel()) { Visible = false };
        _lastItem = new ToolStripMenuItem("Показать последний результат", null, (_, _) => ShowLast()) { Enabled = false };
        _autostartItem = new ToolStripMenuItem("Запускать вместе с Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = Autostart.IsEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _spellItem,
            _translateItem,
            _cancelItem,
            _lastItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Настройки…", null, (_, _) => OpenSettings()),
            _autostartItem,
            new ToolStripMenuItem("О программе", null, (_, _) => ShowAbout()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Выход", null, (_, _) => ExitThread()),
        });
        menu.Opening += (_, _) => _autostartItem.Checked = Autostart.IsEnabled();

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "ClipClaude — готов",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowLast(); };

        Log.Info("Started");
        var failed = RegisterHotkeys();
        if (failed.Count > 0)
        {
            // После запуска цикла сообщений, чтобы иконка уже была в трее.
            var t = new System.Windows.Forms.Timer { Interval = 500 };
            t.Tick += (_, _) => { t.Dispose(); ReportHotkeyConflict(failed); };
            t.Start();
        }
    }

    // ---------- горячие клавиши ----------

    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();
        Register(_settings.SpellcheckHotkey, ClaudeAction.Spellcheck, _spellItem, "Проверить орфографию", failed);
        Register(_settings.TranslateHotkey, ClaudeAction.Translate, _translateItem, "Перевести на английский", failed);
        return failed;
    }

    private void Register(string text, ClaudeAction action, ToolStripMenuItem item, string caption, List<string> failed)
    {
        item.Text = caption;
        if (!Hotkey.TryParse(text, out var hk))
        {
            failed.Add($"{caption}: некорректное сочетание «{text}»");
            return;
        }
        if (_hotkeys.Register(hk, () => Run(action)))
            item.Text = $"{caption} ({hk})";
        else
            failed.Add($"{caption}: {hk} занято другим приложением");
    }

    private void ReportHotkeyConflict(List<string> failed)
    {
        var answer = MessageBox.Show(
            "Не удалось зарегистрировать горячие клавиши:\n\n" + string.Join("\n", failed) +
            "\n\nОткрыть настройки, чтобы выбрать другие?",
            "ClipClaude", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes) OpenSettings();
    }

    // ---------- основной сценарий ----------

    private void Run(ClaudeAction action)
    {
        if (_running is not null)
        {
            Notify("Запрос уже выполняется — дождитесь результата.", ToolTipIcon.Info);
            return;
        }

        switch (ClipboardService.TryGetText(out var text))
        {
            case ClipboardReadStatus.NoText:
                Notify("В буфере обмена нет текста.", ToolTipIcon.Info);
                return;
            case ClipboardReadStatus.Busy:
                Notify("Буфер обмена занят другим приложением. Попробуйте ещё раз.", ToolTipIcon.Warning);
                return;
        }

        if (text.Length > _settings.MaxTextLength)
        {
            var answer = MessageBox.Show(
                $"Текст длиннее {_settings.MaxTextLength:N0} символов ({text.Length:N0}).\n\n" +
                $"Обработать первые {_settings.MaxTextLength:N0} символов?",
                "ClipClaude", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
            int cut = _settings.MaxTextLength;
            if (char.IsHighSurrogate(text[cut - 1])) cut--; // не разрезаем эмодзи
            text = text[..cut];
        }

        _ = ExecuteAsync(action, text);
    }

    private async Task ExecuteAsync(ClaudeAction action, string text)
    {
        if (_running is not null)
        {
            Notify("Запрос уже выполняется — дождитесь результата.", ToolTipIcon.Info);
            return;
        }
        _running = new CancellationTokenSource();
        SetBusy(action);
        Log.Info($"Request {action}, length={text.Length}");

        CliResult result;
        try
        {
            var prompt = Prompts.Build(action, text, _settings);
            var token = _running.Token;
            result = await Task.Run(() => ClaudeCliRunner.RunPromptAsync(_settings, prompt, token));
        }
        catch (Exception ex)
        {
            Log.Error("Request failed", ex);
            result = new CliResult(CliOutcome.StartFailed, "", ex.Message, null, TimeSpan.Zero);
        }
        finally
        {
            _running.Dispose();
            _running = null;
            SetIdle();
        }

        HandleResult(action, text, result);
    }

    private void HandleResult(ClaudeAction action, string text, CliResult r)
    {
        switch (r.Outcome)
        {
            case CliOutcome.Success:
                _last = new ResultData(action, text, r.Output, r.Elapsed);
                _lastItem.Enabled = true;
                bool copied = _settings.AutoCopyResult && ClipboardService.TrySetText(r.Output);
                if (_settings.ShowResultWindow)
                    ShowResult(_last);
                else
                    Notify(copied ? "Готово, результат в буфере." : "Готово. Откройте результат из меню.", ToolTipIcon.Info);
                break;

            case CliOutcome.Cancelled:
                Notify("Запрос отменён.", ToolTipIcon.Info);
                break;

            case CliOutcome.Timeout:
                ShowError($"Claude не ответил за {_settings.TimeoutSeconds} с.", r.Stderr, action, text);
                break;

            case CliOutcome.NotFound:
                ShowError(
                    "Claude Code CLI не найден.\n\n" +
                    "Установите Claude Code (https://claude.com/claude-code), выполните в терминале «claude» и войдите в аккаунт. " +
                    "Если CLI уже установлен, укажите путь к claude.exe или claude.cmd в настройках.",
                    null, action, text, canOpenSettings: true);
                break;

            case CliOutcome.NotAuthorized:
                ShowError("Claude CLI не авторизован.\n\nОткройте терминал, выполните «claude» и войдите в аккаунт.",
                    r.Stderr, action, text);
                break;

            case CliOutcome.EmptyOutput:
                ShowError("Claude вернул пустой ответ.", r.Stderr, action, text);
                break;

            case CliOutcome.StartFailed:
                ShowError("Не удалось запустить Claude CLI.", r.Stderr, action, text, canOpenSettings: true);
                break;

            default:
                ShowError($"Claude CLI завершился с ошибкой (код {r.ExitCode}).", r.Stderr, action, text);
                break;
        }
    }

    private void ShowError(string message, string? details, ClaudeAction action, string text, bool canOpenSettings = false)
    {
        using var form = new ErrorForm(message, details, canRetry: true, canOpenSettings);
        switch (form.ShowDialog())
        {
            case DialogResult.Retry:
                _ = ExecuteAsync(action, text);
                break;
            case DialogResult.Yes:
                OpenSettings();
                break;
        }
    }

    private void ShowResult(ResultData data)
    {
        _resultForm?.Close();
        var form = new ResultForm(data, _settings, bounds =>
        {
            _settings.ResultWindow = bounds;
            _store.Save(_settings);
        });
        form.RetryRequested += d => _ = ExecuteAsync(d.Action, d.Source);
        form.FormClosed += (_, _) =>
        {
            if (_resultForm == form) _resultForm = null;
            form.Dispose();
        };
        _resultForm = form;
        Theme.BringToFront(form);
    }

    private void ShowLast()
    {
        if (_resultForm is { IsDisposed: false })
            Theme.BringToFront(_resultForm);
        else if (_last is not null)
            ShowResult(_last);
    }

    private void SetBusy(ClaudeAction action)
    {
        _tray.Icon = _busyIcon;
        _tray.Text = action == ClaudeAction.Spellcheck ? "ClipClaude — проверяю…" : "ClipClaude — перевожу…";
        _spellItem.Enabled = _translateItem.Enabled = false;
        _cancelItem.Visible = true;
    }

    private void SetIdle()
    {
        _tray.Icon = _idleIcon;
        _tray.Text = "ClipClaude — готов";
        _spellItem.Enabled = _translateItem.Enabled = true;
        _cancelItem.Visible = false;
    }

    private void Notify(string text, ToolTipIcon icon) => _tray.ShowBalloonTip(3000, "ClipClaude", text, icon);

    // ---------- меню ----------

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            Theme.BringToFront(_settingsForm);
            return;
        }

        // Пока открыты настройки, глобальные хоткеи сняты — иначе их нельзя «нажать» в поле ввода.
        _hotkeys.UnregisterAll();
        using (_settingsForm = new SettingsForm(_settings))
        {
            _settingsForm.Shown += (_, _) => Theme.BringToFront(_settingsForm);
            if (_settingsForm.ShowDialog() == DialogResult.OK)
                _store.Save(_settings);
        }
        _settingsForm = null;
        _autostartItem.Checked = Autostart.IsEnabled();

        var failed = RegisterHotkeys();
        if (failed.Count > 0) ReportHotkeyConflict(failed);
    }

    private void ToggleAutostart()
    {
        Autostart.Set(!Autostart.IsEnabled());
        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private static void ShowAbout()
    {
        var version = typeof(TrayApp).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        MessageBox.Show(
            $"ClipClaude {version}\n\n" +
            "Проверка орфографии и перевод текста из буфера обмена через Claude Code CLI.\n\n" +
            "Текст отправляется только в локальный CLI и не сохраняется на диск.",
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void ExitThreadCore()
    {
        _running?.Cancel();
        _resultForm?.Close();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Log.Info("Exited");
        base.ExitThreadCore();
    }
}
