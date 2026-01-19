namespace FTMapScreenSaver;

public sealed class ConfigForm : Form
{
    public ConfigForm()
    {
        Text = "FT Map Screen Saver - Config";
        Width = 520;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;

        var lbl = new Label
        {
            AutoSize = true,
            Left = 16,
            Top = 16,
            Text = "Currently visual-only with default settings."
        };

        var btn = new Button
        {
            Text = "Close",
            Left = 16,
            Top = 60,
            Width = 120
        };
        btn.Click += (_, __) => Close();

        Controls.Add(lbl);
        Controls.Add(btn);
    }
}
