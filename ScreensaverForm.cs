using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FTMapScreenSaver;

public sealed class ScreenSaverForm : Form
{
    private readonly bool _isPreview;
    private readonly IntPtr _previewParentHandle;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly DiskMapSimulator _sim;

    private Bitmap? _mapBitmap;
    private Rectangle _statusRect;
    private Rectangle _mapRect;

    private Point _lastMousePos;
    private bool _firstMouse = true;

    public ScreenSaverForm(bool isPreview, IntPtr previewParentHandle)
    {
        _isPreview = isPreview;
        _previewParentHandle = previewParentHandle;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        BackColor = Color.Black;

        _sim = new DiskMapSimulator(seed: Environment.TickCount);

        _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _timer.Tick += (_, __) =>
        {
            StepAndPaint();
            Invalidate();
        };

        MouseMove += (_, e) => HandleExitOnInput(e.Location);
        MouseDown += (_, __) => { if (!_isPreview) Close(); };
        KeyDown += (_, __) => { if (!_isPreview) Close(); };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_isPreview && _previewParentHandle != IntPtr.Zero)
        {
            // Embed into Control Panel preview window
            NativeMethods.SetParent(Handle, _previewParentHandle);
            if (NativeMethods.GetClientRect(_previewParentHandle, out var r))
            {
                var w = Math.Max(1, r.Right - r.Left);
                var h = Math.Max(1, r.Bottom - r.Top);
                NativeMethods.MoveWindow(Handle, 0, 0, w, h, true);
            }
        }
        else
        {
            TopMost = true;
            Cursor.Hide();
        }

        RebuildSurfaces();
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _timer.Stop();
        Cursor.Show();
        _mapBitmap?.Dispose();
        _mapBitmap = null;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildSurfaces();
    }

    private void HandleExitOnInput(Point p)
    {
        if (_isPreview) return;

        if (_firstMouse)
        {
            _firstMouse = false;
            _lastMousePos = p;
            return;
        }

        // Ignore tiny jitter; exit on real movement
        var dx = Math.Abs(p.X - _lastMousePos.X);
        var dy = Math.Abs(p.Y - _lastMousePos.Y);
        if (dx + dy > 8) Close();
    }

    private void RebuildSurfaces()
    {
        _mapBitmap?.Dispose();

        int margin = _isPreview ? 4 : 10;
        int statusHeight = _isPreview ? 20 : 28;

        _statusRect = new Rectangle(0, 0, Math.Max(1, Width), statusHeight);

        int mapX = margin;
        int mapY = statusHeight + margin;
        int mapW = Math.Max(1, Width - (margin * 2));
        int mapH = Math.Max(1, Height - statusHeight - (margin * 2));

        _mapRect = new Rectangle(mapX, mapY, mapW, mapH);

        _mapBitmap = new Bitmap(mapW, mapH, PixelFormat.Format32bppArgb);
        _sim.Reset(mapW, mapH);

        FullRedraw();
    }

    private void FullRedraw()
    {
        if (_mapBitmap is null) return;

        var rect = new Rectangle(0, 0, _mapBitmap.Width, _mapBitmap.Height);
        var data = _mapBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                byte* basePtr = (byte*)data.Scan0;
                int stride = data.Stride;

                int w = _mapBitmap.Width;
                int h = _mapBitmap.Height;

                for (int y = 0; y < h; y++)
                {
                    byte* rowPtr = basePtr + (y * stride);
                    int rowStart = y * w;

                    for (int x = 0; x < w; x++)
                    {
                        var t = _sim.GetCellByIndex(rowStart + x);
                        var c = DiskMapPalette.GetColor(t);
                        int off = x * 4;
                        rowPtr[off + 0] = c.B;
                        rowPtr[off + 1] = c.G;
                        rowPtr[off + 2] = c.R;
                        rowPtr[off + 3] = 255;
                    }
                }
            }
        }
        finally
        {
            _mapBitmap.UnlockBits(data);
        }
    }

    private void StepAndPaint()
    {
        if (_mapBitmap is null) return;

        // More steps per tick gives that "stuff is moving" vibe
        var changes = _sim.Step(steps: _isPreview ? 80 : 260);
        if (changes.Count == 0) return;

        ApplySegments(changes);
    }

    private void ApplySegments(List<SegmentChange> segments)
    {
        if (_mapBitmap is null) return;

        int w = _mapBitmap.Width;
        int h = _mapBitmap.Height;

        var rect = new Rectangle(0, 0, w, h);
        var data = _mapBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                byte* basePtr = (byte*)data.Scan0;
                int stride = data.Stride;
                int total = w * h;

                foreach (var seg in segments)
                {
                    var c = DiskMapPalette.GetColor(seg.NewType);

                    int start = Math.Max(0, seg.StartIndex);
                    int endExclusive = Math.Min(total, start + Math.Max(0, seg.Length));

                    for (int idx = start; idx < endExclusive; idx++)
                    {
                        int y = idx / w;
                        int x = idx - (y * w);

                        byte* p = basePtr + (y * stride) + (x * 4);
                        p[0] = c.B;
                        p[1] = c.G;
                        p[2] = c.R;
                        p[3] = 255;
                    }
                }
            }
        }
        finally
        {
            _mapBitmap.UnlockBits(data);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        // Status bar
        using (var bg = new SolidBrush(Color.FromArgb(24, 24, 24)))
            e.Graphics.FillRectangle(bg, _statusRect);

        using (var sep = new Pen(Color.FromArgb(64, 64, 64)))
            e.Graphics.DrawLine(sep, 0, _statusRect.Bottom - 1, Width, _statusRect.Bottom - 1);

        var lines = _sim.GetStatusLines();
        using var font = new Font("Consolas", _isPreview ? 7.5f : 10f, FontStyle.Regular);
        using var fg = new SolidBrush(Color.FromArgb(235, 235, 235));

        float y = _isPreview ? 2 : 4;
        for (int i = 0; i < lines.Count; i++)
        {
            e.Graphics.DrawString(lines[i], font, fg, new PointF(8, y));
            y += _isPreview ? 9 : 14;
            if (y > _statusRect.Bottom - 2) break;
        }

        if (_mapBitmap is not null)
        {
            e.Graphics.DrawImageUnscaled(_mapBitmap, _mapRect.Location);
            using var border = new Pen(Color.FromArgb(48, 48, 48));
            e.Graphics.DrawRectangle(border, _mapRect);
        }
    }
}
