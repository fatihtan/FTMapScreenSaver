using System.Globalization;

namespace FTMapScreenSaver;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Windows screen saver conventions:
        // /s            -> run full screen
        // /c            -> config
        // /p <HWND>     -> preview (Control Panel small preview window)
        // e.g: /p:123456 or /c:123 etc.
        var parsed = ScreenSaverArgs.Parse(args);

        switch (parsed.Mode)
        {
            case ScreenSaverMode.Config:
                Application.Run(new ConfigForm());
                return;

            case ScreenSaverMode.Preview:
                Application.Run(new ScreenSaverForm(isPreview: true, previewParentHandle: parsed.PreviewParentHandle));
                return;

            case ScreenSaverMode.Run:
            default:
                RunOnAllScreens();
                return;
        }
    }

    private static void RunOnAllScreens()
    {
        var forms = new List<Form>();

        foreach (var screen in Screen.AllScreens)
        {
            var form = new ScreenSaverForm(isPreview: false, previewParentHandle: IntPtr.Zero)
            {
                StartPosition = FormStartPosition.Manual,
                Bounds = screen.Bounds
            };
            forms.Add(form);
        }

        Application.Run(new MultiFormContext(forms));
    }
}

internal enum ScreenSaverMode { Run, Preview, Config }

internal sealed class ScreenSaverArgs
{
    public ScreenSaverMode Mode { get; init; }
    public IntPtr PreviewParentHandle { get; init; }

    public static ScreenSaverArgs Parse(string[] args)
    {
        if (args.Length == 0) return new ScreenSaverArgs { Mode = ScreenSaverMode.Run };

        string a0 = args[0].Trim().ToLowerInvariant();

        // Handle weird formats: "/p:12345"
        if (a0.StartsWith("/p") || a0.StartsWith("-p"))
        {
            var handle = ExtractHandle(args);
            return new ScreenSaverArgs { Mode = ScreenSaverMode.Preview, PreviewParentHandle = handle };
        }

        if (a0.StartsWith("/c") || a0.StartsWith("-c"))
            return new ScreenSaverArgs { Mode = ScreenSaverMode.Config };

        if (a0.StartsWith("/s") || a0.StartsWith("-s"))
            return new ScreenSaverArgs { Mode = ScreenSaverMode.Run };

        // Default: run
        return new ScreenSaverArgs { Mode = ScreenSaverMode.Run };
    }

    private static IntPtr ExtractHandle(string[] args)
    {
        // /p <HWND>
        if (args.Length >= 2 && long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h1))
            return new IntPtr(h1);

        // /p:123456
        var token = args.Length >= 1 ? args[0] : "";
        var idx = token.IndexOf(':');
        if (idx >= 0 && long.TryParse(token[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h2))
            return new IntPtr(h2);

        return IntPtr.Zero;
    }
}

internal sealed class MultiFormContext : ApplicationContext
{
    private int _openForms;

    public MultiFormContext(IReadOnlyList<Form> forms)
    {
        _openForms = forms.Count;

        foreach (var f in forms)
        {
            f.FormClosed += (_, __) =>
            {
                _openForms--;
                if (_openForms <= 0) ExitThread();
            };
            f.Show();
        }
    }
}
