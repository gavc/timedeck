using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeDeck.Core;

namespace TimeDeck.Windows.UI;

internal sealed class ReportWindow : Window
{
    private readonly App app;
    private static readonly DateTime MinimumDay = new(1900, 1, 8);
    private static readonly DateTime MaximumDay = new(9998, 12, 24);
    private readonly DatePicker date = new() { Width = 155, DisplayDateStart = MinimumDay, DisplayDateEnd = MaximumDay };
    private readonly StackPanel summary = new() { Orientation = Orientation.Horizontal };
    private readonly Canvas timeline = new() { Height = 76, MinWidth = 300, ClipToBounds = true };
    private readonly StackPanel totals = new();
    private readonly StackPanel week = new();
    private readonly DataGrid entries = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 170, MaxHeight = 400, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = Theme.Brush("#DBE2EA"), RowHeight = 36, AlternatingRowBackground = Theme.Brush("#F5F8FA") };
    private readonly DispatcherTimer clock;
    private DateOnly Day => DateOnly.FromDateTime(date.SelectedDate ?? DateTime.Today);
    private sealed record DisplayRow(Guid? Id, string Time, string Activity, string Duration, string Status);
    public static void Open(Window owner, App app)
    {
        var existing = owner.OwnedWindows.OfType<ReportWindow>().FirstOrDefault();
        if (existing != null) { existing.Activate(); return; }
        new ReportWindow(owner, app).Show();
    }
    private ReportWindow(Window owner, App app)
    {
        this.app = app; Owner = owner; Title = "Time Deck report"; Width = Math.Min(1130, SystemParameters.WorkArea.Width - 24); Height = Math.Min(900, SystemParameters.WorkArea.Height - 24); MinWidth = 800; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new(28) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var heading = new DockPanel(); var close = Theme.Button("Back to deck", Close); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close); heading.Children.Add(Theme.Heading("Your time, clearly")); body.Children.Add(heading);
        body.Children.Add(Theme.Help("Review your day, spot long stretches and correct forgotten switches."));
        date.SelectedDate = DateTime.Today;
        body.Children.Add(Theme.Row(Theme.Button("←", () => Navigate(-1)), date, Theme.Button("→", () => Navigate(1)), Theme.Button("Today", () => date.SelectedDate = DateTime.Today), Theme.Button("Export day CSV", () => Export(false)), Theme.Button("Export week CSV", () => Export(true))));
        body.Children.Add(Card("DAY AT A GLANCE", summary));
        body.Children.Add(Card("YOUR DAY · LOCAL TIME", timeline));
        var breakdown = new Grid(); breakdown.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); breakdown.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = Card("WHERE THE TIME WENT", totals); left.Margin = new(0, 12, 8, 0); breakdown.Children.Add(left);
        var right = Card("THIS WEEK · MONDAY TO SUNDAY", week); right.Margin = new(8, 12, 0, 0); Grid.SetColumn(right, 1); breakdown.Children.Add(right); body.Children.Add(breakdown);
        entries.Columns.Add(new DataGridTextColumn { Header = "Start → End", Binding = new System.Windows.Data.Binding("Time"), Width = 190 });
        entries.Columns.Add(new DataGridTextColumn { Header = "Activity", Binding = new System.Windows.Data.Binding("Activity"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        entries.Columns.Add(new DataGridTextColumn { Header = "Duration", Binding = new System.Windows.Data.Binding("Duration"), Width = 110 });
        entries.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding("Status"), Width = 110 });
        var table = new StackPanel(); table.Children.Add(Theme.Row(Theme.Button("Add time", () => EntryDialog.Show(this, app, Day), true), Theme.Button("Edit selected", Edit), Theme.Button("Delete selected", Delete)));
        table.Children.Add(Theme.Help("Double-click a completed entry to edit. Running entries must be stopped first. Cross-midnight entries are shown as the portion on this day; editing opens the full entry.")); table.Children.Add(entries); body.Children.Add(Card("ENTRIES", table));
        date.SelectedDateChanged += (_, _) => Render(); timeline.SizeChanged += (_, _) => DrawTimeline(); entries.MouseDoubleClick += (_, _) => Edit();
        app.StateChanged += Render; clock = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => Render(), Dispatcher);
        Closed += (_, _) => { app.StateChanged -= Render; clock.Stop(); }; Render();
    }
    private static Border Card(string heading, UIElement content)
    {
        var body = new StackPanel(); var title = Theme.Text(heading, 11, "#66788A"); title.FontWeight = FontWeights.SemiBold; title.Margin = new(0, 0, 0, 14); body.Children.Add(title); body.Children.Add(content);
        return new Border { Background = Brushes.White, BorderBrush = Theme.Brush("#DEE5EC"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(18), Margin = new(0, 12, 0, 0), Child = body };
    }
    private List<ReportRow> Rows() => Reports.Day(app.State, Day, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
    private void Navigate(int offset) { var next = (date.SelectedDate ?? DateTime.Today).AddDays(offset); if (next >= MinimumDay && next <= MaximumDay) date.SelectedDate = next; }
    private string Color(Guid? id) => app.State.Activities.FirstOrDefault(a => a.Id == id)?.Color ?? "#5A6B7B";
    private static string Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, TimeZoneInfo.Local).ToString("h:mm tt");
    private void Render()
    {
        var selected = (entries.SelectedItem as DisplayRow)?.Id;
        var rows = Rows(); var total = TimeSpan.FromTicks(rows.Sum(r => r.Duration.Ticks));
        var switches = rows.Zip(rows.Skip(1)).Count(p => p.First.ActivityId != p.Second.ActivityId || p.First.Label != p.Second.Label);
        summary.Children.Clear();
        foreach (var (label, value) in new[] { ("Tracked", rows.Count > 0 ? Reports.Duration(total) : "0m"), ("Day span", rows.Count > 0 ? $"{Local(rows.Min(r => r.Start))} – {Local(rows.Max(r => r.End))}" : "—"), ("Task switches", switches.ToString()), ("Longest block", rows.Count > 0 ? Reports.Duration(rows.Max(r => r.Duration)) : "—") })
        { var metric = new StackPanel { Margin = new(0, 0, 38, 0) }; metric.Children.Add(Theme.Text(label, 12, "#66788A")); var v = Theme.Text(value, 22); v.FontWeight = FontWeights.SemiBold; metric.Children.Add(v); summary.Children.Add(metric); }
        totals.Children.Clear();
        var grouped = rows.GroupBy(r => (r.ActivityId, r.Label)).Select(g => new { g.Key, Duration = TimeSpan.FromTicks(g.Sum(r => r.Duration.Ticks)) }).OrderByDescending(g => g.Duration).ToList();
        if (grouped.Count == 0) totals.Children.Add(Theme.Help("No time tracked on this day."));
        foreach (var g in grouped)
        {
            var line = new DockPanel { Margin = new(0, 0, 0, 4) }; var value = Theme.Text(Reports.Duration(g.Duration) + $" · {g.Duration.TotalSeconds / total.TotalSeconds:0%}", 12, "#66788A"); DockPanel.SetDock(value, Dock.Right); line.Children.Add(value); line.Children.Add(Theme.Text(g.Key.Label, 13)); totals.Children.Add(line);
            totals.Children.Add(new ProgressBar { Value = g.Duration.TotalSeconds / total.TotalSeconds * 100, Height = 7, Foreground = Theme.Brush(Color(g.Key.ActivityId)), Background = Theme.Brush("#EDF1F5"), BorderThickness = new(0), Margin = new(0, 0, 0, 10) });
        }
        week.Children.Clear(); var monday = Day.AddDays(-((int)Day.DayOfWeek + 6) % 7);
        for (var i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i); var dayRows = Reports.Day(app.State, day, DateTimeOffset.UtcNow, TimeZoneInfo.Local); var sum = TimeSpan.FromTicks(dayRows.Sum(r => r.Duration.Ticks));
            var b = Theme.Button($"{day:ddd dd}     {(sum > TimeSpan.Zero ? Reports.Duration(sum) : "—")}", () => date.SelectedDate = day.ToDateTime(TimeOnly.MinValue)); b.Margin = new(0, 0, 0, 4); b.Padding = new(8, 5, 8, 5); b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.IsEnabled = day.ToDateTime(TimeOnly.MinValue) >= MinimumDay && day.ToDateTime(TimeOnly.MinValue) <= MaximumDay;
            if (day == Day) { b.Background = Theme.Brush("#E0F2EB"); b.FontWeight = FontWeights.SemiBold; }
            week.Children.Add(b);
        }
        var display = rows.Select(r => new DisplayRow(r.EntryId, $"{Local(r.Start)} → {(r.Live ? "now" : Local(r.End))}", r.Label, Reports.Duration(r.Duration), r.Live ? "● Running" : "Saved")).ToList();
        entries.ItemsSource = display; if (selected != null) entries.SelectedItem = display.FirstOrDefault(d => d.Id == selected);
        DrawTimeline(rows);
    }
    private void DrawTimeline(List<ReportRow>? rows = null)
    {
        timeline.Children.Clear(); rows ??= Rows(); var width = timeline.ActualWidth; if (width <= 0) return;
        if (rows.Count == 0) { timeline.Children.Add(Theme.Help("Start an activity to see your day here.")); return; }
        var zone = TimeZoneInfo.Local; var lo = Reports.Instant(Day, TimeOnly.MinValue, zone); var hi = Reports.Instant(Day.AddDays(1), TimeOnly.MinValue, zone); var span = (hi - lo).TotalSeconds;
        foreach (var r in rows)
        {
            var x = (r.Start - lo).TotalSeconds / span * width; var length = Math.Max(2, r.Duration.TotalSeconds / span * width);
            var rect = new Border { Width = length, Height = 34, CornerRadius = new(3), Background = Theme.Brush(Color(r.ActivityId)), ToolTip = $"{r.Label}: {Local(r.Start)}–{Local(r.End)} · {Reports.Duration(r.Duration)}" };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, 8); timeline.Children.Add(rect);
            if (length > 90) { rect.Child = new TextBlock { Text = r.Label, Foreground = Brushes.White, FontSize = 11, Margin = new(5, 7, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis }; }
        }
        for (var hour = 0; hour <= 24; hour += 3)
        {
            var instant = hour == 24 ? hi : Reports.Instant(Day, new TimeOnly(hour, 0), zone); var x = (instant - lo).TotalSeconds / span * width;
            var text = Theme.Text(hour == 24 ? "24:00" : $"{hour:00}:00", 10, "#66788A"); Canvas.SetLeft(text, Math.Clamp(x - 15, 0, Math.Max(0, width - 32))); Canvas.SetTop(text, 49); timeline.Children.Add(text);
        }
    }
    private void Edit()
    {
        if (entries.SelectedItem is not DisplayRow row) return;
        if (row.Id == null) { MessageBox.Show(this, "Stop the running timer before editing it.", "Time Deck"); return; }
        EntryDialog.Show(this, app, Day, row.Id);
    }
    private void Delete()
    {
        if (entries.SelectedItem is DisplayRow { Id: { } id } && Theme.Confirm(this, "Delete this entry? This removes the full entry, including any time on other days.")) app.Execute(t => t.State.Entries.RemoveAll(e => e.Id == id));
    }
    private void Export(bool wholeWeek)
    {
        var dialog = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = $"time-deck-{Day:yyyy-MM-dd}{(wholeWeek ? "-week" : "")}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            app.Reconcile(); var first = wholeWeek ? Day.AddDays(-((int)Day.DayOfWeek + 6) % 7) : Day;
            var rows = Enumerable.Range(0, wholeWeek ? 7 : 1).SelectMany(i => Reports.Day(app.State, first.AddDays(i), DateTimeOffset.UtcNow, TimeZoneInfo.Local));
            File.WriteAllText(dialog.FileName, Reports.Csv(rows, TimeZoneInfo.Local), new UTF8Encoding(true));
        }
        catch (Exception ex) { Theme.Error(this, ex); }
    }
}
