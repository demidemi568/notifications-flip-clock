using System.Text.Json;

namespace FlipClock;

/// <summary>What the FlipClockBridge BetterDiscord plugin reports. Times are Unix milliseconds.</summary>
sealed class DiscordBridgeState
{
    public bool Running { get; set; }
    public long Heartbeat { get; set; }
    public int Mentions { get; set; }
    public long MentionsRaised { get; set; }
    public int Notifications { get; set; }
    public long LastNotification { get; set; }
    public long LastFocus { get; set; }

    public static DateTimeOffset Time(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, ms));
}

/// <summary>
/// Reads the file the FlipClockBridge plugin writes from inside Discord. While the plugin is running,
/// the Discord bubble comes from Discord's own unread counts instead of the Windows notification center.
/// </summary>
sealed class DiscordBridge
{
    // If the plugin hasn't rewritten its file for this long, Discord is closed or the plugin is off.
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    sealed class Source
    {
        public required string Path;
        public required bool Wrapped;   // BetterDiscord's config file keeps it under "state"
        public DateTime LastWrite;
        public DiscordBridgeState? State;
    }

    readonly Source[] _sources;

    public DiscordBridge()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _sources = new[]
        {
            new Source { Path = System.IO.Path.Combine(appData, "FlipClock", "discord-bridge.json"), Wrapped = false },
            new Source { Path = System.IO.Path.Combine(appData, "BetterDiscord", "plugins", "FlipClockBridge.config.json"), Wrapped = true },
        };
    }

    /// <summary>The newest report from the plugin, or null if there's none.</summary>
    public DiscordBridgeState? Current { get; private set; }

    /// <summary>True while the plugin is running in an open Discord.</summary>
    public bool IsLive =>
        Current is { Running: true } c && DateTimeOffset.Now - DiscordBridgeState.Time(c.Heartbeat) < StaleAfter;

    /// <summary>Re-reads whichever files changed. Cheap enough to call every second.</summary>
    public void Poll()
    {
        foreach (var src in _sources)
        {
            try
            {
                var info = new FileInfo(src.Path);
                if (!info.Exists)
                {
                    src.State = null;
                    continue;
                }
                if (info.LastWriteTimeUtc == src.LastWrite) continue;

                string json;
                using (var stream = new FileStream(src.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    json = reader.ReadToEnd();

                src.State = Parse(json, src.Wrapped);
                src.LastWrite = info.LastWriteTimeUtc;
            }
            catch
            {
                // Caught mid-write or locked; try again next poll.
            }
        }

        Current = _sources.Select(s => s.State).Where(s => s != null).MaxBy(s => s!.Heartbeat);
    }

    static DiscordBridgeState? Parse(string json, bool wrapped)
    {
        if (!wrapped) return JsonSerializer.Deserialize<DiscordBridgeState>(json, Options);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("state", out var state)
            ? state.Deserialize<DiscordBridgeState>(Options)
            : null;
    }

    /// <summary>
    /// Whether the Discord bubble should show, and its number. Unread @mentions / DMs show until they're read
    /// in Discord; other notifications show until Discord is focused. Either way, clearing the bubble on the
    /// clock hides it until something new arrives.
    /// </summary>
    public static int Unseen(DiscordBridgeState s, DateTimeOffset dismissed)
    {
        bool mentions = s.Mentions > 0 && DiscordBridgeState.Time(s.MentionsRaised) > dismissed;
        var seen = DiscordBridgeState.Time(s.LastFocus) > dismissed ? DiscordBridgeState.Time(s.LastFocus) : dismissed;
        bool notified = s.Notifications > 0 && DiscordBridgeState.Time(s.LastNotification) > seen;
        if (!mentions && !notified) return 0;
        return Math.Max(1, Math.Max(s.Mentions, s.Notifications));
    }
}
