# Flip Clock

A big flip clock for a spare monitor on Windows, with notification bubbles for Discord and Telegram.

> **Unpolished, not built for wide release.** This was vibe coded with Claude for one person's desk setup, and it
> works for her. It hasn't been tested on lots of machines, so expect rough edges. Fork it and vibe code it to your
> standards from here. The code is small and commented, and an AI assistant can find its way around it easily.

<img width="720" height="405" alt="flip-clock-demo" src="https://github.com/user-attachments/assets/ff03156f-cb1a-44a6-926a-bd3ebcc76708" />

## Features

- Flip animation on each digit change, 12/24-hour, AM/PM, any installed font
- Fullscreen on any monitor (F11 or double-click), always-on-top, remembers its window position
- 18 built-in themes (17 dark ones in different colours, plus Light). Overwrite them, save your own, delete them
- Discord and Telegram bubbles with their logos, stacked down the right edge. The clock slides over to make room
- Click a bubble to clear it, using a small panel inside the clock (no popup windows)
- Optional unread count on each bubble
- Optional [BetterDiscord](https://betterdiscord.app) plugin that makes the Discord bubble much more reliable

## Download

Windows 10 or 11, 64-bit.

1. Grab `FlipClock-win-x64.zip` from [Releases](../../releases) and unzip it anywhere.
2. Double-click `FlipClock.exe`. It doesn't need .NET installed.
3. Windows SmartScreen will probably warn you, because the exe isn't code-signed. Click **More info → Run anyway**.
   If you'd rather not, build it yourself (below). Release exes are built by GitHub Actions from this source.
4. The first time it runs, Windows asks whether the app can read your notifications. Say yes, or there are no bubbles.

Pin it to the taskbar or Start: right-click `FlipClock.exe` → Show more options → Pin to taskbar.

## Controls

| Action | What it does |
| --- | --- |
| F11 or double-click | Fullscreen on the monitor the window is on |
| Esc | Close the clear panel, or leave fullscreen |
| Right-click | Settings, Fullscreen, Always on top, Exit |
| Click a bubble | A panel slides out: **Clear** or **Keep** (Enter = Clear, Esc or click elsewhere = Keep) |

## How the bubbles work

**Without the plugin**, the clock reads the Windows notification center. It only looks at *which app* a
notification came from and *when*. It never reads message text and never removes anything from the notification
center. A bubble stays until you clear it, even if the app has already removed its notification from Windows
(Discord does this). Untick **Keep bubbles until I clear them** in Settings to have bubbles go away along with
their Windows notification instead.

If a bubble never shows up, check:

1. The app's own desktop notifications are on (Discord: User Settings → Notifications).
2. Windows: Settings → System → Notifications → *the app* is on, **and** "Show notifications in notification
   center" is on. If that second switch is off, the banner pops up but never reaches the notification center.
3. Discord doesn't send notifications while its window is focused, or for muted servers and channels.
4. Discord in a browser sends notifications as the browser, not as Discord.

### Discord plugin (recommended)

Discord's Windows notifications are unreliable. The `BetterDiscord/FlipClockBridge.plugin.js` plugin tells the
clock directly from inside Discord.

1. Install [BetterDiscord](https://betterdiscord.app).
2. Discord → User Settings → Plugins → **Open Plugins Folder**, and copy `FlipClockBridge.plugin.js` into it.
3. Switch **FlipClockBridge** on.
4. Clock → right-click → Settings. Under Bubbles it should say **Discord plugin connected**.

With the plugin:

- **Unread @mentions, DMs and friend requests** show Discord's own red-badge number, until you read them in Discord.
- **Other notifications** (servers set to "All messages") show until you switch to Discord.
- **Clearing the bubble** on the clock hides it until something new arrives. It doesn't mark anything read in Discord.
- **If Discord is closed or the plugin is off**, the clock goes back to Windows notifications on its own.

The plugin only shares counts and times, never message text, through a file on your PC
(`%APPDATA%\FlipClock\discord-bridge.json`). Nothing goes over the internet.

> Discord's terms don't allow modified clients like BetterDiscord. Bans for it are rare, but it's your call.
> The plugin uses Discord's internal code, so a Discord update can break it until someone fixes it.
> The clock falls back to Windows notifications when that happens.

## Settings file

`%APPDATA%\FlipClock\settings.json` holds the window position, themes and bubble options.

To track another app, add an entry under `"Services"`. `Match` is text to look for in the notification's app
name, and apps without a built-in logo show their `Letter`:

```json
{ "Key": "signal", "Name": "Signal", "Letter": "S", "ColorHex": "#3A76F0", "Enabled": true, "Match": [ "signal" ] }
```

## Build it yourself

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```bat
dotnet publish -c Release -o publish
```

That makes a single `publish\FlipClock.exe`. Or double-click `Build FlipClock.bat`, which does the same and copies
the exe next to it. `dotnet run` works for quick testing, but it opens a console window as well.

To publish a release on your fork, push a tag (`git tag v1.0.0`, then `git push --tags`). The GitHub Actions
workflow builds the exe and attaches it to a release.

## Where things are (for your AI assistant)

| File | What's in it |
| --- | --- |
| `FlipClockView.cs` | All drawing: cards, flip animation, bubbles, the clear panel |
| `MainForm.cs` | Window, fullscreen, menu, polling, deciding which bubbles show |
| `SettingsForm.cs` | Settings window and theme library |
| `Settings.cs` | Settings model, built-in themes, load/save |
| `NotificationWatcher.cs` | Reads the Windows notification center (app name and time only) |
| `DiscordBridge.cs` | Reads what the BetterDiscord plugin reports |
| `BetterDiscord/FlipClockBridge.plugin.js` | The plugin |
| `Icons.cs` | Discord and Telegram logos as vector paths, plus a tiny SVG path parser |

## Credits

Discord and Telegram silhouettes are from [SVG Repo](https://www.svgrepo.com). The logos are trademarks of their
owners, and this project isn't affiliated with Discord or Telegram.

## License

MIT. Do what you like with it.
