using System.Drawing.Text;

namespace FlipClock;

/// <summary>
/// Edits the shared AppSettings object directly so changes preview live. The caller handles Cancel.
/// Theme library changes (overwrite, save new, delete) are saved straight away and survive Cancel.
/// </summary>
sealed class SettingsForm : Form
{
    readonly AppSettings _s;
    readonly Action _apply;
    readonly Rectangle _anchor;
    readonly string _bridgeStatus;
    readonly List<Action> _repaintColorButtons = new();
    readonly ComboBox _themes = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
    readonly Label _themeStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    readonly Button _overwrite = new() { Text = "Overwrite theme" };
    readonly Button _delete = new() { Text = "Delete theme" };
    readonly TextBox _newName = new() { PlaceholderText = "New theme name" };
    readonly Button _saveNew = new() { Text = "Save as new" };
    Control _columns = null!, _buttons = null!;   // built in OnLoad
    bool _loading = true;

    List<Theme> Themes => _s.Themes ??= AppSettings.BuiltInThemes().ToList();

    // Pixel sizes below are at 100% display scaling. S() scales them for the monitor this window is on.
    // Only valid once the window exists, which is why the controls are built in OnLoad.
    int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    /// <param name="anchor">Screen bounds of the clock window; the dialog opens centred on it, on that monitor.</param>
    /// <param name="bridgeStatus">One line about the Discord plugin, shown under the bubble options.</param>
    public SettingsForm(AppSettings s, Action apply, Rectangle anchor, string bridgeStatus)
    {
        _bridgeStatus = bridgeStatus;
        _s = s;
        _apply = apply;
        _anchor = anchor;

        Text = "Flip Clock settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Create the window on the clock's monitor, so it gets that monitor's scaling rather than the main monitor's.
        StartPosition = FormStartPosition.Manual;
        Size = new Size(100, 100);
        Location = new Point(anchor.Left + anchor.Width / 2 - 50, anchor.Top + anchor.Height / 2 - 50);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SuspendLayout();
        // A pixel-sized font matched to this monitor's scaling, so text and the sizes below always agree.
        Font = new Font("Segoe UI", 12f * DeviceDpi / 96f, GraphicsUnit.Pixel);
        BuildUi();
        ResumeLayout(true);
        FitToScreen();
        _loading = false;
    }

    void BuildUi()
    {
        var left = Column();
        var right = Column();

        // ---- Theme library (left)
        left.Controls.Add(Header("Theme", first: true));

        _themes.Width = S(200);
        _themes.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _themes.SelectedIndex < 0) return;
            _s.ApplyTheme(Themes[_themes.SelectedIndex]);
            foreach (var repaint in _repaintColorButtons) repaint();
            UpdateThemeControls();
            _apply();
        };
        left.Controls.Add(Row("Theme", _themes));

        _themeStatus.MaximumSize = new Size(S(260), 0);   // wrap instead of widening the dialog
        _themeStatus.MinimumSize = new Size(0, S(32));     // room for two lines, so the layout doesn't jump
        left.Controls.Add(Row("", _themeStatus));

        SizeButton(_overwrite);
        SizeButton(_delete);
        _overwrite.Click += (_, _) => OverwriteSelected();
        _delete.Click += (_, _) => DeleteSelected();
        left.Controls.Add(Row("", _overwrite, _delete));

        _newName.Width = S(140);
        _newName.TextChanged += (_, _) => UpdateThemeControls();
        _newName.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            SaveAsNew();
            e.SuppressKeyPress = true;   // don't trigger the dialog's Save button
        };
        SizeButton(_saveNew);
        _saveNew.Click += (_, _) => SaveAsNew();
        left.Controls.Add(Row("", _newName, _saveNew));

        var restore = new LinkLabel { Text = "Restore the built-in themes", AutoSize = true };
        restore.LinkClicked += (_, _) => RestoreBuiltIns();
        left.Controls.Add(Row("", restore));

        // ---- Colours (left)
        left.Controls.Add(Header("Colours"));
        left.Controls.Add(Row("Background", ColorButton(() => _s.Background, v => _s.Background = v)));
        left.Controls.Add(Row("Cards", ColorButton(() => _s.Card, v => _s.Card = v)));
        left.Controls.Add(Row("Digits", ColorButton(() => _s.Digit, v => _s.Digit = v)));
        left.Controls.Add(Row("Hinge line", ColorButton(() => _s.Hinge, v => _s.Hinge = v)));

        // ---- Clock (right)
        right.Controls.Add(Header("Clock", first: true));
        var fonts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(200), MaxDropDownItems = 20 };
        using (var installed = new InstalledFontCollection())
            foreach (var family in installed.Families) fonts.Items.Add(family.Name);
        int fontIndex = fonts.Items.IndexOf(_s.FontName);
        if (fontIndex >= 0) fonts.SelectedIndex = fontIndex;
        fonts.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || fonts.SelectedItem is not string name) return;
            _s.FontName = name;
            _apply();
        };
        right.Controls.Add(Row("Font", fonts));
        right.Controls.Add(Check("Bold digits", _s.FontBold, v => _s.FontBold = v));
        right.Controls.Add(Check("24-hour time", _s.Use24Hour, v => _s.Use24Hour = v));
        right.Controls.Add(Check("Leading zero in 12-hour time (09:05)", _s.ShowLeadingZero, v => _s.ShowLeadingZero = v));
        right.Controls.Add(Check("Show AM / PM", _s.ShowAmPm, v => _s.ShowAmPm = v));
        right.Controls.Add(Check("Always on top", _s.AlwaysOnTop, v => _s.AlwaysOnTop = v));

        // ---- Bubbles (right)
        right.Controls.Add(Header("Bubbles"));
        foreach (var svc in _s.Services)
        {
            var captured = svc;
            right.Controls.Add(Check($"{svc.Name} bubble", svc.Enabled, v => captured.Enabled = v));
        }
        right.Controls.Add(Check("Show how many notifications on each bubble", _s.ShowCount, v => _s.ShowCount = v));
        right.Controls.Add(Check("Keep bubbles until I clear them", _s.KeepBubbles, v => _s.KeepBubbles = v));
        right.Controls.Add(new Label
        {
            Text = "Recommended. Discord removes its notifications from Windows after a few seconds, "
                 + "so without this its bubble can vanish before you see it.",
            AutoSize = true,
            MaximumSize = new Size(S(320), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(S(18), 0, 0, S(4)),
        });
        right.Controls.Add(new Label
        {
            Text = _bridgeStatus,
            AutoSize = true,
            MaximumSize = new Size(S(340), 0),
            Margin = new Padding(0, S(8), 0, S(4)),
        });

        var columns = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(S(14), S(12)),
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        right.Margin = new Padding(S(28), 0, 0, 0);
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);

        // Scrolls only if the screen is too small for everything (it normally isn't).
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, S(14), 0) };
        scroller.Controls.Add(columns);
        _columns = columns;

        // ---- Save / Cancel, pinned to the bottom right
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        SizeButton(save, 90);
        SizeButton(cancel, 90);
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(S(10), S(8), S(10), S(12)),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        _buttons = buttons;
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(scroller);
        Controls.Add(buttons);

        FillThemeList();
    }

    /// <summary>Sizes the window to its contents, but never bigger than the monitor; then centres it on the clock.</summary>
    void FitToScreen()
    {
        var content = _columns.GetPreferredSize(Size.Empty);
        int wantW = _columns.Left + content.Width + S(14);
        int wantH = _columns.Top + content.Height + S(8) + _buttons.GetPreferredSize(Size.Empty).Height;

        var wa = Screen.FromRectangle(_anchor).WorkingArea;
        int maxW = wa.Width * 9 / 10, maxH = wa.Height * 9 / 10;
        if (wantH > maxH) wantW += SystemInformation.VerticalScrollBarWidth;
        ClientSize = new Size(Math.Min(wantW, maxW), Math.Min(wantH, maxH));

        int x = _anchor.Left + (_anchor.Width - Width) / 2;
        int y = _anchor.Top + (_anchor.Height - Height) / 2;
        Location = new Point(Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - Width)),
                             Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height)));
    }

    // ------------------------------------------------------------------ theme library

    void FillThemeList(int select = -2)
    {
        bool wasLoading = _loading;
        _loading = true;
        _themes.BeginUpdate();
        _themes.Items.Clear();
        foreach (var t in Themes) _themes.Items.Add(t.Name);
        _themes.EndUpdate();
        _themes.SelectedIndex = select >= -1 ? select : Themes.FindIndex(_s.Matches);
        _loading = wasLoading;
        UpdateThemeControls();
    }

    Theme? Selected => _themes.SelectedIndex >= 0 && _themes.SelectedIndex < Themes.Count ? Themes[_themes.SelectedIndex] : null;

    void UpdateThemeControls()
    {
        var t = Selected;
        bool edited = t != null && !_s.Matches(t);

        _themeStatus.Text = t == null
            ? "Custom colours. Name them below to keep them as a theme."
            : edited ? $"Colours changed. \"Overwrite theme\" saves them into {t.Name}." : "";

        _overwrite.Enabled = edited;
        _delete.Enabled = t != null && Themes.Count > 1;

        string name = _newName.Text.Trim();
        _saveNew.Enabled = name.Length > 0;
        _saveNew.Text = Themes.Any(x => AppSettings.Same(x.Name, name)) ? "Replace theme" : "Save as new";
    }

    void OverwriteSelected()
    {
        var t = Selected;
        if (t == null) return;
        t.Background = _s.Background; t.Card = _s.Card; t.Digit = _s.Digit; t.Hinge = _s.Hinge;
        _s.Save();
        UpdateThemeControls();
    }

    void DeleteSelected()
    {
        int i = _themes.SelectedIndex;
        if (i < 0 || Themes.Count <= 1) return;
        Themes.RemoveAt(i);
        _s.Save();
        // The colours stay as they are; they just aren't a named theme any more.
        FillThemeList(-1);
    }

    void SaveAsNew()
    {
        string name = _newName.Text.Trim();
        if (name.Length == 0) return;

        int i = Themes.FindIndex(x => AppSettings.Same(x.Name, name));
        var theme = new Theme(name, _s.Background, _s.Card, _s.Digit, _s.Hinge);
        if (i >= 0) Themes[i] = theme;
        else
        {
            Themes.Add(theme);
            i = Themes.Count - 1;
        }
        _s.Save();
        _newName.Text = "";
        FillThemeList(i);
    }

    /// <summary>Puts back any deleted built-in themes and resets edited ones. User-made themes are kept.</summary>
    void RestoreBuiltIns()
    {
        foreach (var b in AppSettings.BuiltInThemes())
        {
            int i = Themes.FindIndex(x => AppSettings.Same(x.Name, b.Name));
            if (i >= 0) Themes[i] = b;
            else Themes.Add(b);
        }
        _s.Save();
        FillThemeList();
    }

    // ------------------------------------------------------------------ layout helpers

    static FlowLayoutPanel Column() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = Padding.Empty,
    };

    Label Header(string text, bool first = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Font, FontStyle.Bold),
        Margin = new Padding(0, first ? 0 : S(10), 0, S(4)),
    };

    FlowLayoutPanel Row(string label, params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, S(1), 0, S(1)),
        };
        row.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            MinimumSize = new Size(S(90), S(26)),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        foreach (var c in controls)
        {
            if (c is Label or LinkLabel) c.Margin = new Padding(c.Margin.Left, S(5), c.Margin.Right, c.Margin.Bottom);
            row.Controls.Add(c);
        }
        return row;
    }

    /// <summary>Buttons size themselves to their text, so nothing is cut off at any display scaling.</summary>
    void SizeButton(Button b, int minWidth = 0)
    {
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Padding = new Padding(S(6), S(1), S(6), S(1));
        b.MinimumSize = new Size(S(minWidth), S(26));
    }

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, S(3), 0, S(3)) };
        box.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            set(box.Checked);
            _apply();
        };
        return box;
    }

    Button ColorButton(Func<string> get, Action<string> set)
    {
        var button = new Button
        {
            Text = "Change…",
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
        };
        SizeButton(button, 110);
        button.FlatAppearance.BorderColor = SystemColors.ControlDark;

        void Repaint()
        {
            var c = AppSettings.ParseColor(get(), Color.Gray);
            button.BackColor = c;
            button.ForeColor = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 150 ? Color.Black : Color.White;
        }

        Repaint();
        _repaintColorButtons.Add(Repaint);

        button.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = AppSettings.ParseColor(get(), Color.Gray) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(AppSettings.Hex(dlg.Color));
            Repaint();
            UpdateThemeControls();
            _apply();
        };
        return button;
    }
}
