using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimeDeck.Core;
using TimeDeck.Windows.Services;

namespace TimeDeck.Windows.UI;

internal static class SettingsWindow
{
    public static void Show(Window owner, App app)
    {
        var window = Theme.Dialog("Time Deck settings", owner, 580, 800);
        var body = new StackPanel { Margin = new(28) }; window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var settings = Backup.Clone(app.State).Settings;
        body.Children.Add(Theme.Heading("Settings")); body.Children.Add(Theme.Help("Your workday, reminders and local backups."));
        var start = new TextBox { Text = settings.WorkStart.ToString("HH:mm") }; var end = new TextBox { Text = settings.WorkEnd.ToString("HH:mm") };
        Theme.Field(body, "Workday start · 24-hour time", start); Theme.Field(body, "Workday finish · 24-hour time", end);
        var auto = new CheckBox { Content = "Automatically stop tracking at workday finish", IsChecked = settings.AutoStop }; body.Children.Add(auto);
        var remind = new CheckBox { Content = "Remind me to check the running activity", IsChecked = settings.Reminders }; body.Children.Add(remind);
        var minutes = new TextBox { Text = settings.ReminderMinutes.ToString() }; Theme.Field(body, "Reminder interval · minutes", minutes);
        body.Children.Add(Theme.Help("Unanswered check-ins repeat every 15 minutes. Still working starts a fresh reminder period. Sleep counts as tracked time up to your cutoff; alerts resume when Windows wakes."));
        var status = Theme.Help(app.Notifications.Status);
        body.Children.Add(Theme.Button("Test notification", () => { app.Notifications.Test(); status.Text = app.Notifications.Status; })); body.Children.Add(status);
        var startup = new CheckBox { Content = "Start with Windows, in the system tray", IsChecked = settings.StartWithWindows }; body.Children.Add(startup);
        var error = Theme.Text("", 12, "#A9413C"); body.Children.Add(error);
        body.Children.Add(Theme.Row(Theme.Button("Save settings", () =>
        {
            if (!TimeOnly.TryParse(start.Text, out var from) || !TimeOnly.TryParse(end.Text, out var to) || !int.TryParse(minutes.Text, out var interval)) { error.Text = "Enter valid times (such as 06:00) and a whole number of minutes."; return; }
            var next = new Settings { WorkStart = from, WorkEnd = to, AutoStop = auto.IsChecked == true, Reminders = remind.IsChecked == true, ReminderMinutes = interval, StartWithWindows = startup.IsChecked == true, TrayExplained = app.State.Settings.TrayExplained };
            var oldStartup = app.State.Settings.StartWithWindows;
            try
            {
                next.Validate();
                if (next.StartWithWindows != oldStartup) StartupService.SetEnabled(next.StartWithWindows);
                if (app.Execute(t => t.ChangeSettings(next, DateTimeOffset.UtcNow))) window.Close();
                else if (next.StartWithWindows != oldStartup) StartupService.SetEnabled(oldStartup);
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }, true), Theme.Button("Cancel", window.Close)));
        body.Children.Add(new Separator { Margin = new(0, 22, 0, 16) });
        body.Children.Add(Theme.Text("Backups", 18));
        body.Children.Add(Theme.Help("Data is saved automatically on this PC. Export a backup to keep a separate copy."));
        body.Children.Add(Theme.Row(Theme.Button("Export backup", () =>
        {
            var dialog = new SaveFileDialog { Filter = "Time Deck backup (*.json)|*.json", FileName = "time-deck-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json" };
            if (dialog.ShowDialog(window) != true) return;
            try { app.Reconcile(); File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(app.State, Backup.Json), new UTF8Encoding(false)); MessageBox.Show(window, "Backup saved.", "Time Deck"); } catch (Exception ex) { Theme.Error(window, ex); }
        }), Theme.Button("Restore backup", () => Restore(window, app))));
        body.Children.Add(Theme.Help(app.DataDirectory)); window.ShowDialog();
    }
    private static void Restore(Window owner, App app)
    {
        if (app.State.Running != null) { MessageBox.Show(owner, "Stop tracking before restoring a backup.", "Time Deck"); return; }
        var dialog = new OpenFileDialog { Filter = "Time Deck backup (*.json)|*.json" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            var preview = Backup.Parse(File.ReadAllText(dialog.FileName));
            var message = $"Replace your data with this backup?\n\n{preview.Activities.Count} activities · {preview.EntryCount} entries\n\nA safety copy of your current data will be kept.";
            if (preview.HasRunningTimer) message += "\n\nThe backup contains a running timer. It will be stopped at its cutoff or now, whichever is earlier.";
            if (Theme.Confirm(owner, message, "Restore backup") && app.Restore(preview.SelectActivities(preview.Activities.Select(a => a.Id)))) owner.Close();
        }
        catch (Exception ex) { Theme.Error(owner, ex); }
    }
}
