using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FlipClock;

/// <summary>
/// Reads the Windows notification center and reports, per chat app, when its notifications were created.
/// It only looks at app names and timestamps. It never reads message text and never clears anything.
/// </summary>
sealed class NotificationWatcher
{
    readonly UserNotificationListener _listener = UserNotificationListener.Current;

    public UserNotificationListenerAccessStatus Status { get; private set; } = UserNotificationListenerAccessStatus.Unspecified;
    public string? LastError { get; private set; }

    /// <summary>Returns null if notifications couldn't be read (no access, or an error).</summary>
    public async Task<Dictionary<string, List<DateTimeOffset>>?> PollAsync(IReadOnlyList<ServiceConfig> services)
    {
        try
        {
            Status = _listener.GetAccessStatus();
            if (Status == UserNotificationListenerAccessStatus.Unspecified)
            {
                // Must be called on the UI thread, which is where PollAsync is called from.
                Status = await _listener.RequestAccessAsync();
            }
            if (Status != UserNotificationListenerAccessStatus.Allowed) return null;

            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);

            var result = new Dictionary<string, List<DateTimeOffset>>();
            foreach (var s in services) result[s.Key] = new List<DateTimeOffset>();

            foreach (var n in notifications)
            {
                string name = "", id = "";
                try
                {
                    name = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                    id = n.AppInfo?.AppUserModelId ?? "";
                }
                catch { /* some notifications have no app info; skip matching for those */ }

                foreach (var s in services)
                {
                    if (s.Match.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)
                                      || id.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        result[s.Key].Add(n.CreationTime);
                        break;
                    }
                }
            }

            LastError = null;
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }
}
