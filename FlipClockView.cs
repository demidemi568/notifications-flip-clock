using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WinTimer = System.Windows.Forms.Timer;

namespace FlipClock;

sealed record BadgeInfo(string Key, string Name, string Letter, string Icon, Color Color, int Count);

/// <summary>Draws the flip clock and the notification bubbles. Everything is custom-painted.</summary>
sealed class FlipClockView : Control
{
    const double FlipSeconds = 0.55;
    const double PopSeconds = 0.30;
    const double ReserveSeconds = 0.35;
    const double ConfirmFadeSeconds = 0.18;
    const double ConfirmTimeoutSeconds = 8;

    static readonly Color AlertRed = Color.FromArgb(255, 59, 48);

    AppSettings _s = new();

    // What each of the four cards shows now, what it showed before, and when its flip started (0 = idle).
    readonly char[] _shown = { ' ', ' ', ' ', ' ' };
    readonly char[] _from = { ' ', ' ', ' ', ' ' };
    readonly long[] _start = new long[4];
    string _ampm = "";

    List<BadgeInfo> _badges = new();
    readonly Dictionary<string, long> _badgeAppeared = new();
    readonly List<(string Key, RectangleF Rect)> _badgeRects = new();
    string _status = "";

    // The small "Clear this bubble?" panel drawn next to a bubble after it's clicked (null = closed).
    string? _confirmKey;
    long _confirmStart;
    RectangleF _confirmRect, _clearRect, _keepRect;
    string? _hover;   // "clear", "keep" or null

    // How much of the right-hand column is set aside for bubbles (0 = none, 1 = full), animated.
    float _reserveFrom, _reserveTo;
    long _reserveStart;

    Font? _digitFont;
    string _digitFontKey = "";
    RectangleF _refBounds;
    string _refKey = "";

    // Ticks slowly when nothing moves, quickly while something is animating.
    readonly WinTimer _timer = new() { Interval = 250 };

    /// <summary>Raised when the user confirms clearing a bubble in the inline panel.</summary>
    public event Action<string>? BadgeCleared;

    public FlipClockView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _timer.Tick += (_, _) => Tick(true);
        _timer.Start();
        Tick(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _digitFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ public API

    public void ApplySettings(AppSettings s)
    {
        _s = s;
        _digitFontKey = "";
        _refKey = "";
        Tick(false);   // snap to the right digits without a flip (e.g. after switching 12/24 hour)
        Invalidate();
    }

    public void SetStatus(string text)
    {
        if (text == _status) return;
        _status = text;
        Invalidate();
    }

    public void SetBadges(IEnumerable<BadgeInfo> badges)
    {
        var list = badges.ToList();
        if (list.SequenceEqual(_badges)) return;

        long ts = Stopwatch.GetTimestamp();
        foreach (var b in list)
            if (!_badgeAppeared.ContainsKey(b.Key)) _badgeAppeared[b.Key] = ts;
        foreach (var gone in _badgeAppeared.Keys.Where(k => list.All(b => b.Key != k)).ToList())
            _badgeAppeared.Remove(gone);

        // Make room for the bubble column when the first bubble appears, give it back when the last one goes.
        float target = list.Count > 0 ? 1f : 0f;
        if (target != _reserveTo)
        {
            _reserveFrom = ReserveFactor(ts);
            _reserveTo = target;
            _reserveStart = ts;
        }

        _badges = list;
        if (_confirmKey != null && list.All(b => b.Key != _confirmKey)) CloseConfirm();
        Tick(true);
        Invalidate();
    }

    /// <summary>True over a bubble or the clear panel, so double-clicks there don't toggle fullscreen.</summary>
    public bool IsOverInteractive(Point p) =>
        HitBadge(p) != null || (_confirmKey != null && _confirmRect.Contains(p));

    public bool IsConfirmOpen => _confirmKey != null;

    /// <summary>Closes the clear panel if it's open. Returns true if it was.</summary>
    public bool CancelConfirm()
    {
        if (_confirmKey == null) return false;
        CloseConfirm();
        return true;
    }

    /// <summary>Clears the bubble the panel is asking about (Enter key).</summary>
    public void AcceptConfirm()
    {
        var key = _confirmKey;
        if (key == null) return;
        CloseConfirm();
        BadgeCleared?.Invoke(key);
    }

    void OpenConfirm(string key)
    {
        _confirmKey = key;
        _confirmStart = Stopwatch.GetTimestamp();
        _hover = null;
        Tick(true);
        Invalidate();
    }

    void CloseConfirm()
    {
        _confirmKey = null;
        _hover = null;
        _confirmRect = _clearRect = _keepRect = RectangleF.Empty;
        Cursor = Cursors.Default;
        Invalidate();
    }

    // ------------------------------------------------------------------ time + animation

    static double Elapsed(long now, long then) => (now - then) / (double)Stopwatch.Frequency;

    void Tick(bool animate)
    {
        var now = DateTime.Now;
        int h = now.Hour;
        string ampm = h < 12 ? "AM" : "PM";
        if (!_s.Use24Hour)
        {
            h %= 12;
            if (h == 0) h = 12;
        }
        string hh = h.ToString("00");
        if (!_s.Use24Hour && !_s.ShowLeadingZero && hh[0] == '0') hh = " " + hh[1];
        string text = hh + now.Minute.ToString("00");

        long ts = Stopwatch.GetTimestamp();
        bool dirty = ampm != _ampm;
        _ampm = ampm;

        for (int i = 0; i < 4; i++)
        {
            if (!animate) _start[i] = 0;
            if (_shown[i] == text[i]) continue;
            _from[i] = animate ? _shown[i] : text[i];
            _shown[i] = text[i];
            _start[i] = animate ? ts : 0;
            dirty = true;
        }

        // The clear panel fades in, and closes by itself if left alone.
        if (_confirmKey != null && Elapsed(ts, _confirmStart) > ConfirmTimeoutSeconds) CloseConfirm();

        bool animating = _start.Any(t => t != 0)
                      || _reserveStart != 0
                      || _badgeAppeared.Values.Any(t => Elapsed(ts, t) < PopSeconds)
                      || (_confirmKey != null && Elapsed(ts, _confirmStart) < ConfirmFadeSeconds);
        int interval = animating ? 16 : 250;
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (dirty || animating) Invalidate();
    }

    double Progress(int i, long ts)
    {
        if (_start[i] == 0) return -1;
        double t = Elapsed(ts, _start[i]) / FlipSeconds;
        if (t >= 1)
        {
            _start[i] = 0;
            return -1;
        }
        return t;
    }

    float ReserveFactor(long ts)
    {
        if (_reserveStart == 0) return _reserveTo;
        double t = Elapsed(ts, _reserveStart) / ReserveSeconds;
        if (t >= 1)
        {
            _reserveStart = 0;
            return _reserveTo;
        }
        float eased = (float)(t * t * (3 - 2 * t));   // smoothstep
        return _reserveFrom + (_reserveTo - _reserveFrom) * eased;
    }

    static float BadgeRadius(float H) => Math.Clamp(H * 0.075f, 18f, 90f);

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_s.BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        float W = ClientSize.Width, H = ClientSize.Height;
        if (W < 40 || H < 40) return;

        long ts = Stopwatch.GetTimestamp();

        // The bubble column on the right is 3 bubble-radii wide. The clock lives in whatever is left.
        float badgeR = BadgeRadius(H);
        float reserve = ReserveFactor(ts);
        float availW = Math.Max(40f, W - 3f * badgeR * reserve);

        // Layout, in units of one card's width: four cards, a small gap inside each pair, a wider gap for the colon.
        const float gapIn = 0.07f, gapColon = 0.5f, aspect = 1.4f;
        float units = 4 + 2 * gapIn + gapColon;
        float u = Math.Min(availW * 0.88f / units, H * 0.62f / aspect);
        float cw = u, ch = u * aspect;
        float x0 = (availW - units * u) / 2f, y0 = (H - ch) / 2f;
        float[] xs =
        {
            x0,
            x0 + cw + gapIn * u,
            x0 + 2 * cw + gapIn * u + gapColon * u,
            x0 + 3 * cw + 2 * gapIn * u + gapColon * u,
        };

        for (int i = 0; i < 4; i++) DrawCard(g, new RectangleF(xs[i], y0, cw, ch), i, ts);

        // Colon
        float colonX = x0 + 2 * cw + gapIn * u + gapColon * u / 2f;
        float d = u * 0.13f;
        using (var br = new SolidBrush(Color.FromArgb(230, _s.DigitColor)))
        {
            g.FillEllipse(br, colonX - d / 2, y0 + ch * 0.36f - d / 2, d, d);
            g.FillEllipse(br, colonX - d / 2, y0 + ch * 0.64f - d / 2, d, d);
        }

        // AM / PM
        if (!_s.Use24Hour && _s.ShowAmPm)
        {
            using var f = MakeFont(_s.FontName, u * 0.17f, true);
            using var br = new SolidBrush(Color.FromArgb(150, _s.DigitColor));
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(_ampm, f, br, xs[3] + cw, y0 + ch + u * 0.06f, sf);
        }

        DrawBadges(g, W, H, badgeR, ts);

        if (!string.IsNullOrEmpty(_status))
        {
            using var f = MakeFont("Segoe UI", Math.Max(11f, H * 0.018f), false);
            using var br = new SolidBrush(Color.FromArgb(150, _s.DigitColor));
            g.DrawString(_status, f, br, 12, H - f.Height - 10);
        }
    }

    void DrawCard(Graphics g, RectangleF card, int i, long ts)
    {
        double p = Progress(i, ts);
        char cur = _shown[i];

        if (p < 0)
        {
            DrawHalf(g, card, cur, top: true, scaleY: 1f, shade: 0f);
            DrawHalf(g, card, cur, top: false, scaleY: 1f, shade: 0f);
        }
        else
        {
            char old = _from[i];
            // Underneath: new digit's top half and old digit's bottom half.
            DrawHalf(g, card, cur, top: true, scaleY: 1f, shade: 0f);
            DrawHalf(g, card, old, top: false, scaleY: 1f, shade: 0f);

            if (p < 0.5)
            {
                // Old top half folds down toward the hinge.
                float s = (float)Math.Cos(p * Math.PI);
                DrawHalf(g, card, old, top: true, scaleY: s, shade: 0.5f * (1f - s));
            }
            else
            {
                // New bottom half unfolds from the hinge.
                float s = (float)-Math.Cos(p * Math.PI);
                DrawHalf(g, card, cur, top: false, scaleY: s, shade: 0.5f * (1f - s));
            }
        }

        // Hinge line
        float midY = card.Top + card.Height / 2f;
        float th = Math.Max(1.5f, card.Height * 0.012f);
        using var hinge = new SolidBrush(_s.HingeColor);
        g.FillRectangle(hinge, card.Left, midY - th / 2f, card.Width, th);
    }

    void DrawHalf(Graphics g, RectangleF card, char c, bool top, float scaleY, float shade)
    {
        float midY = card.Top + card.Height / 2f;
        var half = top
            ? new RectangleF(card.Left, card.Top, card.Width, card.Height / 2f)
            : new RectangleF(card.Left, midY, card.Width, card.Height / 2f);

        var state = g.Save();
        // Scale vertically around the hinge.
        g.TranslateTransform(0, midY);
        g.ScaleTransform(1, Math.Max(scaleY, 0.0001f));
        g.TranslateTransform(0, -midY);
        g.SetClip(half);

        using (var path = RoundedRect(card, card.Width * 0.09f))
        using (var fill = new SolidBrush(_s.CardColor))
            g.FillPath(fill, path);

        if (c != ' ') DrawGlyph(g, card, c);

        if (shade > 0f)
        {
            int alpha = (int)(255 * Math.Clamp(shade, 0f, 1f));
            using var sb = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillRectangle(sb, half);
        }

        g.Restore(state);
    }

    /// <summary>
    /// Draws a digit as an outline path, centred on the card by its actual ink.
    /// (DrawString with a layout box silently draws nothing when the box is shorter than one line of text.)
    /// </summary>
    void DrawGlyph(Graphics g, RectangleF card, char c)
    {
        var font = GetDigitFont();
        float em = card.Height * 0.74f;
        int style = (int)font.Style;

        // Vertical centring uses the height of a "0" so every digit sits on the same baseline.
        string key = $"{_digitFontKey}|{(int)Math.Round(em)}";
        if (key != _refKey)
        {
            using var refPath = new GraphicsPath();
            refPath.AddString("0", font.FontFamily, style, em, PointF.Empty, StringFormat.GenericTypographic);
            _refBounds = refPath.GetBounds();
            _refKey = key;
        }

        using var path = new GraphicsPath();
        path.AddString(c.ToString(), font.FontFamily, style, em, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds();
        if (b.Width <= 0f || b.Height <= 0f) return;

        float dx = card.Left + card.Width / 2f - (b.Left + b.Width / 2f);
        float dy = card.Top + card.Height / 2f - (_refBounds.Top + _refBounds.Height / 2f);
        using var move = new Matrix();
        move.Translate(dx, dy);
        path.Transform(move);

        using var br = new SolidBrush(_s.DigitColor);
        g.FillPath(br, path);
    }

    void DrawBadges(Graphics g, float W, float H, float r, long ts)
    {
        _badgeRects.Clear();
        int n = _badges.Count;
        if (n == 0) return;

        // A column of bubbles against the right edge, vertically centred.
        float margin = r * 0.5f, gap = r * 0.4f;
        float total = n * 2 * r + (n - 1) * gap;
        float top = (H - total) / 2f;
        float cx = W - margin - r;

        for (int i = 0; i < n; i++)
        {
            var b = _badges[i];
            float cy = top + r + i * (2 * r + gap);

            double t = _badgeAppeared.TryGetValue(b.Key, out var t0) ? Elapsed(ts, t0) / PopSeconds : 1;
            float scale = t >= 1 ? 1f : EaseOutBack((float)t);
            float outer = r * scale;

            // Red ring on the outside, coloured icon inside it.
            float ringW = Math.Max(2f, outer * 0.16f);
            float ringR = outer - ringW / 2f;
            float iconR = Math.Max(1f, outer - ringW - outer * 0.04f);

            using (var pen = new Pen(AlertRed, ringW))
                g.DrawEllipse(pen, cx - ringR, cy - ringR, ringR * 2, ringR * 2);

            var iconRect = new RectangleF(cx - iconR, cy - iconR, iconR * 2, iconR * 2);
            using (var fill = new SolidBrush(b.Color)) g.FillEllipse(fill, iconRect);

            // White silhouette (or the letter, for apps without one).
            if (Icons.Has(b.Icon))
            {
                Icons.Draw(g, b.Icon, cx, cy, iconR, Brushes.White);
            }
            else
            {
                using var f = MakeFont("Segoe UI", Math.Max(6f, iconR * 1.0f), true);
                using var sf = new StringFormat(StringFormat.GenericTypographic)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                var textRect = new RectangleF(iconRect.Left, iconRect.Top + iconR * 0.06f, iconRect.Width, iconRect.Height);
                g.DrawString(b.Letter, f, Brushes.White, textRect, sf);
            }

            // Optional count in a small red pip on the bubble's top-right.
            if (_s.ShowCount && scale > 0.5f)
            {
                string count = b.Count > 99 ? "99+" : b.Count.ToString();
                using var f = MakeFont("Segoe UI", Math.Max(7f, outer * 0.36f), true);
                var size = g.MeasureString(count, f, PointF.Empty, StringFormat.GenericTypographic);
                float ph = outer * 0.62f;
                float pw = Math.Max(ph, size.Width + ph * 0.55f);
                var pip = new RectangleF(cx + outer * 0.78f - pw / 2f, cy - outer * 0.78f - ph / 2f, pw, ph);
                using (var path = RoundedRect(pip, ph / 2f))
                {
                    using var fill = new SolidBrush(AlertRed);
                    using var edge = new Pen(_s.BackgroundColor, Math.Max(1.5f, ph * 0.1f));
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                }
                using var sf = new StringFormat(StringFormat.GenericTypographic)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString(count, f, Brushes.White, new RectangleF(pip.Left, pip.Top + ph * 0.03f, pip.Width, pip.Height), sf);
            }

            _badgeRects.Add((b.Key, new RectangleF(cx - r, cy - r, 2 * r, 2 * r)));
        }

        if (_confirmKey != null) DrawConfirm(g, W, H, r, ts);
    }

    /// <summary>The inline "Clear the Discord bubble?" panel, drawn to the left of its bubble.</summary>
    void DrawConfirm(Graphics g, float W, float H, float r, long ts)
    {
        int index = _badges.FindIndex(b => b.Key == _confirmKey);
        if (index < 0) return;
        var badge = _badges[index];
        var bubble = _badgeRects[index].Rect;
        float cy = bubble.Top + bubble.Height / 2f;

        double t = Math.Clamp(Elapsed(ts, _confirmStart) / ConfirmFadeSeconds, 0, 1);
        float ease = (float)(1 - Math.Pow(1 - t, 3));
        int alpha = (int)(255 * ease);

        float titlePx = Math.Clamp(r * 0.42f, 13f, 34f);
        using var titleFont = MakeFont("Segoe UI", titlePx, true);
        using var noteFont = MakeFont("Segoe UI", titlePx * 0.72f, false);
        using var buttonFont = MakeFont("Segoe UI", titlePx * 0.8f, true);
        var tf = StringFormat.GenericTypographic;

        string title = $"Clear {badge.Name}?";
        const string note = "Only clears this bubble. Windows notifications stay.";
        var titleSize = g.MeasureString(title, titleFont, PointF.Empty, tf);
        var noteSize = g.MeasureString(note, noteFont, PointF.Empty, tf);

        float pad = titlePx * 0.9f;
        float btnH = titlePx * 2.0f;
        float btnW = Math.Max(g.MeasureString("Clear", buttonFont, PointF.Empty, tf).Width,
                              g.MeasureString("Keep", buttonFont, PointF.Empty, tf).Width) + titlePx * 2.2f;
        float btnGap = titlePx * 0.6f;

        float contentW = Math.Max(Math.Max(titleSize.Width, noteSize.Width), 2 * btnW + btnGap);
        float pw = contentW + 2 * pad;
        float ph = pad + titleSize.Height + titlePx * 0.35f + noteSize.Height + titlePx * 0.9f + btnH + pad;

        // Sits to the left of the bubble; slides in a little as it fades in.
        float right = bubble.Left - r * 0.35f + (1 - ease) * r * 0.4f;
        float left = Math.Max(8f, right - pw);
        float top = Math.Clamp(cy - ph / 2f, 8f, Math.Max(8f, H - ph - 8f));
        _confirmRect = new RectangleF(left, top, pw, ph);

        Color panel = Blend(_s.CardColor, _s.BackgroundColor, 0.15f);
        Color text = _s.DigitColor;

        using (var path = RoundedRect(_confirmRect, titlePx * 0.7f))
        {
            using (var shadowPath = RoundedRect(new RectangleF(left + 3, top + 5, pw, ph), titlePx * 0.7f))
            using (var shadow = new SolidBrush(Color.FromArgb(alpha * 90 / 255, 0, 0, 0)))
                g.FillPath(shadow, shadowPath);
            using var fill = new SolidBrush(Color.FromArgb(alpha * 245 / 255, panel));
            using var border = new Pen(Color.FromArgb(alpha * 70 / 255, text), Math.Max(1f, titlePx * 0.07f));
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        // A thin pointer toward the bubble.
        float tipX = right + r * 0.25f;
        float baseX = left + pw - 1;
        if (tipX > baseX)
        {
            using var arrow = new SolidBrush(Color.FromArgb(alpha * 245 / 255, panel));
            float ay = Math.Clamp(cy, top + titlePx, top + ph - titlePx);
            g.FillPolygon(arrow, new[] { new PointF(baseX, ay - titlePx * 0.45f), new PointF(tipX, ay), new PointF(baseX, ay + titlePx * 0.45f) });
        }

        float x = left + pad, y = top + pad;
        using (var br = new SolidBrush(Color.FromArgb(alpha, text)))
            g.DrawString(title, titleFont, br, x, y, tf);
        y += titleSize.Height + titlePx * 0.35f;
        using (var br = new SolidBrush(Color.FromArgb(alpha * 170 / 255, text)))
            g.DrawString(note, noteFont, br, x, y, tf);
        y += noteSize.Height + titlePx * 0.9f;

        float bx = left + pw - pad - 2 * btnW - btnGap;
        _keepRect = new RectangleF(bx, y, btnW, btnH);
        _clearRect = new RectangleF(bx + btnW + btnGap, y, btnW, btnH);

        using var center = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        // Keep: outlined.
        using (var path = RoundedRect(_keepRect, btnH / 2f))
        {
            if (_hover == "keep")
                using (var hl = new SolidBrush(Color.FromArgb(alpha * 40 / 255, text))) g.FillPath(hl, path);
            using var pen = new Pen(Color.FromArgb(alpha * 120 / 255, text), Math.Max(1f, titlePx * 0.08f));
            g.DrawPath(pen, path);
            using var br = new SolidBrush(Color.FromArgb(alpha, text));
            g.DrawString("Keep", buttonFont, br, _keepRect, center);
        }

        // Clear: solid red.
        using (var path = RoundedRect(_clearRect, btnH / 2f))
        {
            var red = _hover == "clear" ? Blend(AlertRed, Color.White, 0.15f) : AlertRed;
            using var fill = new SolidBrush(Color.FromArgb(alpha, red));
            g.FillPath(fill, path);
            using var br = new SolidBrush(Color.FromArgb(alpha, Color.White));
            g.DrawString("Clear", buttonFont, br, _clearRect, center);
        }
    }

    static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    Font GetDigitFont()
    {
        string key = $"{_s.FontName}|{_s.FontBold}";
        if (_digitFont == null || key != _digitFontKey)
        {
            _digitFont?.Dispose();
            _digitFont = MakeFont(_s.FontName, 100f, _s.FontBold);
            _digitFontKey = key;
            _refKey = "";
        }
        return _digitFont;
    }

    static Font MakeFont(string name, float px, bool bold)
    {
        try
        {
            return new Font(name, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            // The font doesn't support the requested style.
            return new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    // ------------------------------------------------------------------ mouse

    string? HitBadge(Point p)
    {
        foreach (var (key, rect) in _badgeRects)
        {
            float rad = rect.Width / 2f;
            float dx = p.X - (rect.Left + rad), dy = p.Y - (rect.Top + rad);
            if (dx * dx + dy * dy <= rad * rad) return key;
        }
        return null;
    }

    string? HitConfirm(Point p)
    {
        if (_confirmKey == null) return null;
        if (_clearRect.Contains(p)) return "clear";
        if (_keepRect.Contains(p)) return "keep";
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = HitConfirm(e.Location);
        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
        }
        Cursor = hover != null || HitBadge(e.Location) != null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null)
        {
            _hover = null;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        if (_confirmKey != null)
        {
            switch (HitConfirm(e.Location))
            {
                case "clear": AcceptConfirm(); return;
                case "keep": CloseConfirm(); return;
            }
            if (_confirmRect.Contains(e.Location)) return;   // clicks on the panel itself do nothing
        }

        var key = HitBadge(e.Location);
        if (key != null && key != _confirmKey) OpenConfirm(key);   // clicking the same bubble again closes it
        else CloseConfirm();
    }
}
