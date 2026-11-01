using WinTimer = System.Windows.Forms.Timer;
using Windows.UI.Notifications.Management;

namespace FlipClock;

sealed class MainForm : Form
{
    readonly AppSettings _s = AppSettings.Load();
    readonly FlipClockView _view = new() { Dock = DockStyle.Fill };
    readonly NotificationWatcher _watcher = new();
    readonly DiscordBridge _bridge = new();
    // Every second: some apps (Discord) remove their notification a few seconds after showing it.
    readonly WinTimer _pollTimer = new() { Interval = 1000 };
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _fullscreenItem = new("Fullscreen (F11)");
    readonly ToolStripMenuItem _topItem = new("Always on top");

    Dictionary<string, List<DateTimeOffset>> _latest = new();
    bool _polling;
    bool _fullscreen;
    Rectangle _windowedBounds;

    public MainForm()
    {
        Text = "Flip Clock";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* keep the default icon */ }
        BackColor = _s.BackgroundColor;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;

        Controls.Add(_view);
        _view.ApplySettings(_s);
        _view.BadgeCleared += OnBadgeCleared;
        _view.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && !_view.IsOverInteractive(e.Location)) ToggleFullscreen();
        };

        BuildMenu();
        _view.ContextMenuStrip = _menu;

        // Restore the last window position, or start at half the primary screen if it's missing or off-screen.
        var saved = new Rectangle(_s.X, _s.Y, _s.Width, _s.Height);
        if (_s.Width >= 200 && _s.Height >= 150 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(saved)))
        {
            _windowedBounds = saved;
        }
        else
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            _windowedBounds = new Rectangle(wa.Left + wa.Width / 4, wa.Top + wa.Height / 4, wa.Width / 2, wa.Height / 2);
        }
        Bounds = _windowedBounds;
        TopMost = _s.AlwaysOnTop;

        _pollTimer.Tick += async (_, _) => await PollAsync();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_s.Fullscreen) SetFullscreen(true);
        try
        {
            await PollAsync();   // first poll also asks Windows for notification access if needed
        }
        finally
        {
            _pollTimer.Start();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var b = _fullscreen ? _windowedBounds : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
        _s.X = b.X; _s.Y = b.Y; _s.Width = b.Width; _s.Height = b.Height;
        _s.Fullscreen = _fullscreen;
        _s.Save();
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ window

    void BuildMenu()
    {
        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings();
        _fullscreenItem.Click += (_, _) => ToggleFullscreen();
        _topItem.Click += (_, _) =>
        {
            _s.AlwaysOnTop = !_s.AlwaysOnTop;
            TopMost = _s.AlwaysOnTop;
            _s.Save();
        };
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Close();

        _menu.Items.AddRange(new ToolStripItem[] { settings, _fullscreenItem, _topItem, new ToolStripSeparator(), exit });
        _menu.Opening += (_, _) =>
        {
            _fullscreenItem.Checked = _fullscreen;
            _topItem.Checked = _s.AlwaysOnTop;
        };
    }

    void ToggleFullscreen() => SetFullscreen(!_fullscreen);

    void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        if (on)
        {
            _windowedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var screen = Screen.FromRectangle(_windowedBounds);
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = screen.Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _windowedBounds;
        }
        _fullscreen = on;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && _view.CancelConfirm())
        {
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _view.IsConfirmOpen)
        {
            _view.AcceptConfirm();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && _fullscreen)
        {
            SetFullscreen(false);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ settings

    void ApplyAll()
    {
        BackColor = _s.BackgroundColor;
        TopMost = _s.AlwaysOnTop;
        _view.ApplySettings(_s);
        RefreshBadges();
    }

    void OpenSettings()
    {
        var backup = _s.Clone();
        using var dlg = new SettingsForm(_s, ApplyAll, Bounds, BridgeStatus());   // changes preview live
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _s.Save();
        }
        else
        {
            _s.CopyAppearanceFrom(backup);
            ApplyAll();
        }
    }

    // ------------------------------------------------------------------ notifications / badges

    async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            _bridge.Poll();
            var services = _s.Services.Where(x => x.Enabled).ToList();
            var result = await _watcher.PollAsync(services);
            if (result != null)
            {
                _latest = result;
                if (_s.KeepBubbles && Remember(result)) _s.Save();
            }

            _view.SetStatus(_watcher.Status switch
            {
                UserNotificationListenerAccessStatus.Allowed =>
                    _watcher.LastError == null ? "" : "Couldn't read notifications just now.",
                UserNotificationListenerAccessStatus.Denied =>
                    "Windows denied notification access, so the badges can't update.",
                _ => "Waiting for notification access…",
            });

            RefreshBadges();
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>Adds newly seen notifications to the saved "not cleared yet" list. Returns true if anything was added.</summary>
    bool Remember(Dictionary<string, List<DateTimeOffset>> live)
    {
        bool changed = false;
        foreach (var (key, times) in live)
        {
            if (UsesBridge(key)) continue;   // the plugin is the source of truth for Discord while it runs
            _s.DismissedAt.TryGetValue(key, out var dismissed);
            if (!_s.Unseen.TryGetValue(key, out var kept)) _s.Unseen[key] = kept = new();
            foreach (var t in times)
            {
                if (t <= dismissed || kept.Contains(t)) continue;
                kept.Add(t);
                changed = true;
            }
            if (kept.Count > 500)
            {
                kept.Sort();
                kept.RemoveRange(0, kept.Count - 500);
            }
        }
        return changed;
    }

    /// <summary>Discord's bubble comes from the BetterDiscord plugin while it's running, else from Windows notifications.</summary>
    bool UsesBridge(string key) => key == "discord" && _bridge.IsLive;

    string BridgeStatus()
    {
        if (_bridge.IsLive) return "Discord plugin connected. The Discord bubble comes straight from Discord.";
        if (_bridge.Current != null) return "Discord plugin not running right now (Discord closed or plugin off). Using Windows notifications.";
        return "Discord plugin not installed. Using Windows notifications. See README for the more reliable plugin.";
    }

    void RefreshBadges()
    {
        var badges = new List<BadgeInfo>();
        foreach (var svc in _s.Services.Where(x => x.Enabled))
        {
            if (UsesBridge(svc.Key))
            {
                _s.DismissedAt.TryGetValue(svc.Key, out var cleared);
                int count = DiscordBridge.Unseen(_bridge.Current!, cleared);
                if (count > 0)
                    badges.Add(new BadgeInfo(svc.Key, svc.Name, svc.Letter, svc.Icon, AppSettings.ParseColor(svc.ColorHex, Color.Gray), count));
                continue;
            }

            IEnumerable<DateTimeOffset> times = _latest.TryGetValue(svc.Key, out var live) ? live : Array.Empty<DateTimeOffset>();
            if (_s.KeepBubbles && _s.Unseen.TryGetValue(svc.Key, out var kept)) times = times.Concat(kept).Distinct();
            _s.DismissedAt.TryGetValue(svc.Key, out var dismissed);   // default = long ago
            int unseen = times.Count(t => t > dismissed);
            if (unseen > 0)
                badges.Add(new BadgeInfo(svc.Key, svc.Name, svc.Letter, svc.Icon, AppSettings.ParseColor(svc.ColorHex, Color.Gray), unseen));
        }
        _view.SetBadges(badges);
    }

    // The user confirmed in the clock's own "Clear Discord?" panel.
    void OnBadgeCleared(string key)
    {
        // Anything up to now (or the newest current notification, if its clock is ahead) counts as seen.
        var mark = DateTimeOffset.Now;
        if (_latest.TryGetValue(key, out var times) && times.Count > 0)
        {
            var newest = times.Max();
            if (newest > mark) mark = newest;
        }
        _s.DismissedAt[key] = mark;
        _s.Unseen.Remove(key);
        _s.Save();
        RefreshBadges();
    }
}
