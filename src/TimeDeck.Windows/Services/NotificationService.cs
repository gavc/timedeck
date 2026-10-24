using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TimeDeck.Core;

namespace TimeDeck.Windows.Services;

public sealed class NotificationService(Action activate) : IDisposable
{
    private bool registered;
    public string Status { get; private set; } = "Notifications have not been initialised.";
    public void Initialize()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += Invoked;
            AppNotificationManager.Default.Register(); registered = true;
            Status = "Windows notifications are ready. Do Not Disturb and Windows notification settings can silence alerts.";
        }
        catch (Exception ex) { Status = "Windows notifications could not be enabled: " + ex.Message + " In-app check-ins remain available."; }
    }
    private void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => activate();
    public void ShowReminder(RunningTimer timer, DateTimeOffset now) => Send("Still working on " + timer.Label + "?", $"You have been tracking for {Reports.Duration(now - timer.Start)}. Open Time Deck to confirm, change activity or stop.", "activity-check-in");
    public void ShowInfo(string title, string body) => Send(title, body, "workday");
    public void Test() => Send("Time Deck notification test", "Notifications are working. Click this notification to open your deck.", "test");
    private void Send(string title, string body, string tag)
    {
        if (!registered) return;
        try
        {
            var toast = new AppNotificationBuilder().AddArgument("action", "open").AddText(title).AddText(body).BuildNotification();
            toast.Tag = tag; toast.Group = "time-deck";
            AppNotificationManager.Default.Show(toast);
            Status = toast.Id == 0 ? "Windows did not accept the notification. Check Settings → System → Notifications." : "Notification sent to Windows. If it did not appear, check Windows Notifications and Do Not Disturb.";
        }
        catch (Exception ex) { Status = "Notification failed: " + ex.Message + " In-app check-ins remain available."; }
    }
    public void ClearReminder()
    {
        if (registered) try { _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync("activity-check-in", "time-deck"); } catch (Exception) {/* The in-app check-in has already been cleared. */}
    }
    public void Dispose()
    {
        if (!registered) return;
        try { AppNotificationManager.Default.NotificationInvoked -= Invoked; AppNotificationManager.Default.Unregister(); } catch (Exception) { }
    }
}
