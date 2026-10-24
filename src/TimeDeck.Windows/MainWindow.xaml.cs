using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TimeDeck.Core;
using TimeDeck.Windows.UI;

namespace TimeDeck.Windows;

public sealed partial class MainWindow : Window
{
    private static readonly Thickness BorderThicknessActive = new(3);
    private static readonly Thickness BorderThicknessNormal = new(1);
    private readonly App app;
    private bool editing;
    private Guid? dragId;
    private Point dragOrigin;
    private readonly Dictionary<int, (Button Button, TextBlock Total)> tiles = [];
    public MainWindow(App app)
    {
        this.app = app; InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        Closing += OnClosing;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && editing) { editing = false; Render(); } };
        Render();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (app.Quitting) return;
        e.Cancel = true;
        foreach (var window in OwnedWindows.Cast<Window>().ToArray()) window.Close();
        if (!app.State.Settings.TrayExplained)
        {
            MessageBox.Show(this, "Time Deck will keep running in the system tray. Timers, reminders and automatic stopping remain active. Use the tray menu to exit.", "Running in the background", MessageBoxButton.OK, MessageBoxImage.Information);
            app.Execute(t => t.State.Settings.TrayExplained = true);
        }
        Hide();
    }
    public void Render()
    {
        Deck.Children.Clear(); tiles.Clear();
        for (var slot = 0; slot < 32; slot++)
        {
            var position = slot;
            var activity = app.State.Activities.FirstOrDefault(a => a.Slot == slot);
            string name = slot switch { 29 => "Report", 30 => editing ? "Done editing" : "Edit", 31 => "Settings", _ => activity?.Name ?? (editing ? "Add activity" : "Empty") };
            string icon = slot switch { 29 => "▤", 30 => editing ? "✓" : "✎", 31 => "⚙", _ => activity?.Icon ?? (editing ? "+" : "·") };
            var b = new Button { Style = (Style)FindResource("Tile"), Background = Theme.Brush(activity?.Color ?? (slot >= 29 ? "#303D4D" : "#242D39")), AllowDrop = editing && slot < 29, Tag = activity?.Id };
            AutomationProperties.SetName(b, $"Slot {slot + 1}: {name}"); AutomationProperties.SetAutomationId(b, "Slot" + (slot + 1));
            b.ToolTip = activity != null ? $"{activity.Name} · {activity.Category}" : slot < 29 ? editing ? "Add an activity here" : "Choose Edit to add an activity" : name;
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = icon, FontSize = 29, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 7), FontFamily = new("Segoe UI Emoji") });
            content.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 36 });
            var total = new TextBlock { FontSize = 11, TextAlignment = TextAlignment.Center, Opacity = .8, Margin = new(0, 5, 0, 0) };
            content.Children.Add(total); b.Content = content;
            if (slot < 29)
            {
                tiles[slot] = (b, total);
                b.Click += (_, _) => { if (editing) ActivityDialog.Show(this, app, position); else if (activity != null) Press(activity.Id); };
                b.PreviewMouseLeftButtonDown += (_, e) => { dragOrigin = e.GetPosition(this); dragId = activity?.Id; };
                b.PreviewMouseMove += (_, e) =>
                {
                    if (!editing || dragId == null || e.LeftButton != MouseButtonState.Pressed) return;
                    var point = e.GetPosition(this);
                    if (Math.Abs(point.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    var id = dragId.Value; dragId = null; DragDrop.DoDragDrop(b, new DataObject("TimeDeck.Activity", id.ToString()), DragDropEffects.Move); e.Handled = true;
                };
                b.DragOver += (_, e) => { e.Effects = editing && e.Data.GetDataPresent("TimeDeck.Activity") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                b.Drop += (_, e) => { if (editing && Guid.TryParse(e.Data.GetData("TimeDeck.Activity") as string, out var id)) app.Execute(t => t.Move(id, position)); e.Handled = true; };
            }
            else b.Click += (_, _) => { switch (position) { case 29: ReportWindow.Open(this, app); break; case 30: editing = !editing; Render(); break; case 31: SettingsWindow.Show(this, app); break; } };
            Deck.Children.Add(b);
        }
        DeckHint.Text = editing ? "EDIT MODE · Drag to move or swap · Click to customise · Use the destination slot field to move with the keyboard" : "Click to start · Click again to stop · Choose another activity to switch";
        Refresh();
    }
    private void Press(Guid id)
    {
        app.Reconcile();
        try { app.Execute(t => t.Press(id, DateTimeOffset.UtcNow), false); }
        catch (OvertimeRequiredException ex) { if (Theme.Confirm(this, ex.Message, "Outside work hours")) app.Execute(t => t.Press(id, DateTimeOffset.UtcNow, true)); }
        catch (Exception ex) { Theme.Error(this, ex); }
    }
    public void Refresh()
    {
        if (!IsVisible) return;
        var now = DateTimeOffset.UtcNow; var s = app.State; var run = s.Running;
        var day = DateOnly.FromDateTime(DateTime.Now); var rows = Reports.Day(s, day, now, TimeZoneInfo.Local);
        TodayTotal.Text = rows.Count == 0 ? "0m" : Reports.Duration(TimeSpan.FromTicks(rows.Sum(r => r.Duration.Ticks)));
        ModeLabel.Text = editing ? "EDITING YOUR DECK" : run == null ? "READY TO TRACK" : run.Overtime ? "TRACKING · OVERTIME" : "NOW TRACKING";
        CurrentActivity.Text = run?.Label ?? (editing ? "Make this deck yours" : "Choose an activity");
        var elapsed = run == null ? TimeSpan.Zero : now - run.Start; if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        Elapsed.Text = run == null ? "" : $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        StopButton.Visibility = run == null ? Visibility.Collapsed : Visibility.Visible;

        Dictionary<Guid, long>? ticksByActivity = null;
        if (rows.Count > 0)
        {
            ticksByActivity = new Dictionary<Guid, long>();
            foreach (var r in rows)
            {
                if (r.ActivityId is { } actId)
                {
                    ticksByActivity[actId] = ticksByActivity.GetValueOrDefault(actId) + r.Duration.Ticks;
                }
            }
        }

        foreach (var pair in tiles)
        {
            var a = s.Activities.FirstOrDefault(a => a.Slot == pair.Key); var active = a != null && run?.ActivityId == a.Id;
            pair.Value.Button.BorderBrush = Theme.Brush(active ? "#FFFFFF" : editing ? "#A8B8CA" : "#414A56");
            pair.Value.Button.BorderThickness = active ? BorderThicknessActive : BorderThicknessNormal;
            var ticks = (a != null && ticksByActivity != null && ticksByActivity.TryGetValue(a.Id, out var t)) ? t : 0;
            pair.Value.Total.Text = ticks > 0 ? Reports.Duration(TimeSpan.FromTicks(ticks)) : a == null ? "" : "—";
        }
        ReminderPanel.Visibility = run?.ReminderPending == true && s.Settings.Reminders ? Visibility.Visible : Visibility.Collapsed;
        ReminderText.Text = run == null ? "" : $"Still working on {run.Label}? You've been tracking for {Reports.Duration(elapsed)}.";
        WorkHours.Text = $"Work hours {s.Settings.WorkStart:h:mm tt}–{s.Settings.WorkEnd:h:mm tt}  ·  " + (s.Settings.AutoStop ? "Automatic stop on" : "Automatic stop off") + $"  ·  Reminders {(s.Settings.Reminders ? $"every {s.Settings.ReminderMinutes} min" : "off")}";
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => app.Execute(t => t.Stop(DateTimeOffset.UtcNow));
    private void StillWorking_Click(object sender, RoutedEventArgs e) => app.Execute(t => t.Acknowledge(DateTimeOffset.UtcNow));
    private void ChangeActivity_Click(object sender, RoutedEventArgs e) { editing = false; app.Execute(t => t.Acknowledge(DateTimeOffset.UtcNow, true)); }
}
