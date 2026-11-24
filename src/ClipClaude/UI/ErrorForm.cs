namespace ClipClaude.UI;

/// <summary>Окно ошибки: краткий текст, раскрываемые детали (stderr) и, при необходимости, «Повторить».</summary>
internal sealed class ErrorForm : Form
{
    public ErrorForm(string message, string? details, bool canRetry, bool canOpenSettings)
    {
        Text = "ClipClaude — ошибка";
        Icon = AppIcons.LoadLarge();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 180);
        MinimumSize = new Size(420, 180);

        var text = new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
        };

        var detailsBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            Text = (details ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"),
            Visible = false,
        };

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(close);
        CancelButton = close;
        if (canRetry)
        {
            var retry = new Button { Text = "Повторить", AutoSize = true, DialogResult = DialogResult.Retry };
            buttons.Controls.Add(retry);
            AcceptButton = retry;
        }
        if (canOpenSettings)
            buttons.Controls.Add(new Button { Text = "Настройки…", AutoSize = true, DialogResult = DialogResult.Yes });
        if (!string.IsNullOrWhiteSpace(details))
        {
            var toggle = new Button { Text = "Подробности ▾", AutoSize = true };
            toggle.Click += (_, _) =>
            {
                detailsBox.Visible = !detailsBox.Visible;
                toggle.Text = detailsBox.Visible ? "Подробности ▴" : "Подробности ▾";
                if (detailsBox.Visible && ClientSize.Height < 360) ClientSize = new Size(ClientSize.Width, 380);
            };
            buttons.Controls.Add(toggle);
        }

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(detailsBox, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

        Theme.Apply(this);
        Shown += (_, _) => Theme.BringToFront(this);
    }
}
