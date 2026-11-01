using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlipClock;

/// <summary>One chat app that can get a badge. Notifications are matched by app name / app id.</summary>
sealed class ServiceConfig
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Letter { get; set; } = "";
    // Built-in silhouette to draw in the bubble ("discord", "telegram"). Empty = draw Letter instead.
    public string Icon { get; set; } = "";
    public string ColorHex { get; set; } = "#888888";
    public bool Enabled { get; set; } = true;
    // Case-insensitive text to look for in the notification's app name or app id.
    public List<string> Match { get; set; } = new();
}

/// <summary>A named set of clock colours. Saved in settings.json so they can be overwritten, added and deleted.</summary>
sealed class Theme
{
    public string Name { get; set; } = "";
    public string Background { get; set; } = "#000000";
    public string Card { get; set; } = "#1F1F1F";
    public string Digit { get; set; } = "#EDEDED";
    public string Hinge { get; set; } = "#000000";

    public Theme() { }
    public Theme(string name, string bg, string card, string digit, string hinge)
    {
        Name = name; Background = bg; Card = card; Digit = digit; Hinge = hinge;
    }
}

sealed class AppSettings
{
    // ---- Appearance ----
    public string Background { get; set; } = "#000000";
    public string Card { get; set; } = "#3D2145";
    public string Digit { get; set; } = "#E3B3E6";
    public string Hinge { get; set; } = "#000000";
    public string FontName { get; set; } = "Segoe UI";
    public bool FontBold { get; set; } = true;

    // Saved themes. Starts as the built-in list; the user can overwrite, add and delete.
    public List<Theme>? Themes { get; set; }

    // ---- Clock ----
    public bool Use24Hour { get; set; }
    public bool ShowLeadingZero { get; set; }   // 12-hour mode only: "09:05" instead of " 9:05"
    public bool ShowAmPm { get; set; } = true;

    // ---- Badges ----
    public bool ShowCount { get; set; }         // number inside the bubble instead of the letter
    public List<ServiceConfig> Services { get; set; } = DefaultServices();
    // Per service: notifications created at or before this moment don't light the badge.
    public Dictionary<string, DateTimeOffset> DismissedAt { get; set; } = new();
    // Keep a bubble until it's clicked, even after the app removes its notification from Windows.
    // Discord takes its notifications back out of the notification center after a few seconds.
    public bool KeepBubbles { get; set; } = true;
    // Per service: notifications seen but not cleared yet (only used with KeepBubbles).
    public Dictionary<string, List<DateTimeOffset>> Unseen { get; set; } = new();

    // ---- Window ----
    public bool AlwaysOnTop { get; set; }
    public bool Fullscreen { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore] public Color BackgroundColor => ParseColor(Background, Color.Black);
    [JsonIgnore] public Color CardColor => ParseColor(Card, Color.FromArgb(0x3D, 0x21, 0x45));
    [JsonIgnore] public Color DigitColor => ParseColor(Digit, Color.FromArgb(0xE3, 0xB3, 0xE6));
    [JsonIgnore] public Color HingeColor => ParseColor(Hinge, Color.Black);

    // Built-in themes. Most are dark: near-black background, a deep tinted card and a light digit of the same hue.
    public static Theme[] BuiltInThemes() => new Theme[]
    {
        new("Plum",          "#000000", "#3D2145", "#E3B3E6", "#000000"),
        new("Classic dark",  "#000000", "#1F1F1F", "#EDEDED", "#000000"),
        new("Graphite",      "#0C0D0F", "#24272C", "#C9CED6", "#0C0D0F"),
        new("Crimson",       "#080001", "#3A1016", "#FF9EA8", "#080001"),
        new("Rose",          "#080306", "#3E1A2C", "#FFB3D1", "#080306"),
        new("Ember",         "#0A0300", "#3B1A0C", "#FFA77A", "#0A0300"),
        new("Amber",         "#0B0600", "#1A1008", "#FFB000", "#000000"),
        new("Gold",          "#070600", "#2E2810", "#F2D675", "#070600"),
        new("Lime",          "#030700", "#1F2E10", "#C8F07A", "#030700"),
        new("Emerald",       "#000704", "#0F3324", "#86E8B6", "#000704"),
        new("Teal",          "#000707", "#0E3133", "#7FE3E0", "#000707"),
        new("Ocean",         "#00050A", "#0C2A40", "#8FD3FF", "#00050A"),
        new("Midnight",      "#050A18", "#0F1B33", "#9CC4FF", "#050A18"),
        new("Indigo",        "#03030C", "#1E1F4D", "#B3B8FF", "#03030C"),
        new("Violet",        "#050210", "#2C1A4A", "#CDB5FF", "#050210"),
        new("Magenta",       "#08000A", "#40123F", "#FF9CF2", "#08000A"),
        new("Terminal",      "#000000", "#0A1A0A", "#33FF66", "#000000"),
        new("Light",         "#E9E4D8", "#FAF7F0", "#262626", "#BDB5A3"),
    };

    static List<ServiceConfig> DefaultServices() => new()
    {
        new ServiceConfig { Key = "discord",  Name = "Discord",  Letter = "D", Icon = "discord", ColorHex = "#5865F2", Match = { "discord" } },
        new ServiceConfig { Key = "telegram", Name = "Telegram", Letter = "T", Icon = "telegram", ColorHex = "#2AABEE", Match = { "telegram" } },
    };

    public static Color ParseColor(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- Load / save ----
    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipClock");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (s.Services == null || s.Services.Count == 0) s.Services = DefaultServices();
                    s.DismissedAt ??= new();
                    s.Unseen ??= new();
                    s.Themes ??= BuiltInThemes().ToList();
                    // Older settings files predate the silhouettes; give the two built-in services theirs.
                    foreach (var svc in s.Services)
                        if (string.IsNullOrEmpty(svc.Icon) && (svc.Key == "discord" || svc.Key == "telegram"))
                            svc.Icon = svc.Key;

                    // The first build saved its grey/white defaults. Anyone still on exactly those
                    // colors moves to the plum theme; anything customised is left alone.
                    if (Same(s.Background, "#000000") && Same(s.Card, "#1F1F1F")
                        && Same(s.Digit, "#EDEDED") && Same(s.Hinge, "#000000"))
                    {
                        s.Card = "#3D2145";
                        s.Digit = "#E3B3E6";
                    }
                    return s;
                }
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings { Themes = BuiltInThemes().ToList() };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Options))!;

    public bool Matches(Theme t) =>
        Same(t.Background, Background) && Same(t.Card, Card) && Same(t.Digit, Digit) && Same(t.Hinge, Hinge);

    public void ApplyTheme(Theme t)
    {
        Background = t.Background; Card = t.Card; Digit = t.Digit; Hinge = t.Hinge;
    }

    /// <summary>Copies look-and-feel options back from a backup (used when the settings dialog is cancelled).</summary>
    public void CopyAppearanceFrom(AppSettings o)
    {
        Background = o.Background; Card = o.Card; Digit = o.Digit; Hinge = o.Hinge;
        FontName = o.FontName; FontBold = o.FontBold;
        Use24Hour = o.Use24Hour; ShowLeadingZero = o.ShowLeadingZero; ShowAmPm = o.ShowAmPm;
        ShowCount = o.ShowCount; KeepBubbles = o.KeepBubbles; AlwaysOnTop = o.AlwaysOnTop;
        foreach (var svc in Services)
        {
            var other = o.Services.FirstOrDefault(x => x.Key == svc.Key);
            if (other != null) svc.Enabled = other.Enabled;
        }
    }
}
