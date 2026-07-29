namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// A borderless, top-most window that fills the primary screen and animates a moving high-contrast
/// pattern on its own UI thread. Used only by the diagnostics END-TO-END phase: it makes the real
/// desktop genuinely change so the real capture path (DXGI or GDI) has real work to measure. It
/// flashes on screen for the few seconds of that phase and is then closed.
/// </summary>
internal sealed class MotionWindow
{
    private Thread? _thread;
    private PatternForm? _form;
    private readonly ManualResetEventSlim _ready = new(false);

    public void Start()
    {
        _thread = new Thread(() =>
        {
            _form = new PatternForm();
            _form.Shown += (_, _) => _ready.Set();
            System.Windows.Forms.Application.Run(_form);
        })
        {
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(3000);
    }

    public void Stop()
    {
        try
        {
            _form?.BeginInvoke(new Action(() =>
            {
                _form.Close();
                System.Windows.Forms.Application.ExitThread();
            }));
        }
        catch { /* ignore — thread may already be gone */ }
        _thread?.Join(3000);
    }

    private sealed class PatternForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer;
        private int _frame;

        public PatternForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;

            _timer = new System.Windows.Forms.Timer { Interval = 15 };
            _timer.Tick += (_, _) => { _frame++; Invalidate(); };
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height, bar = 64;
            int offset = (_frame * 24) % (bar * 2);

            for (int x = -bar * 2 + offset; x < w; x += bar * 2)
            {
                g.FillRectangle(Brushes.White, x, 0, bar, h);
                g.FillRectangle(Brushes.Black, x + bar, 0, bar, h);
            }

            // Moving colour blocks so chroma is exercised too.
            using var red = new SolidBrush(Color.FromArgb(220, 40, 40));
            using var blue = new SolidBrush(Color.FromArgb(40, 90, 220));
            int by = (_frame * 8) % Math.Max(1, h - 120);
            g.FillRectangle(red, 40, by, 220, 100);
            g.FillRectangle(blue, w - 280, h - 120 - by, 220, 100);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
