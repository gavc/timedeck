using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TimeDeck.Core;

namespace TimeDeck.Windows.UI;

internal static class EntryDialog
{
    public static void Show(Window owner, App app, DateOnly day, Guid? entryId = null)
    {
        var entry = entryId.HasValue ? app.State.Entries.Single(e => e.Id == entryId) : null;
        var window = Theme.Dialog(entry == null ? "Add time" : "Correct entry", owner, 540, 740);
        var body = new StackPanel { Margin = new(28) }; window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(Theme.Heading(entry == null ? "Add time manually" : "Correct your time"));
        body.Children.Add(Theme.Help("Enter local dates and times. Entries can span midnight; reports split them by day."));
        var names = new ComboBox { IsEditable = true, ItemsSource = app.State.Activities.Select(a => a.Name).ToList(), Text = entry?.Label ?? "" }; Theme.Field(body, "Activity", names);
        var from = entry == null ? day.ToDateTime(new(9, 0)) : TimeZoneInfo.ConvertTime(entry.Start, TimeZoneInfo.Local).DateTime;
        var to = entry == null ? from.AddHours(1) : TimeZoneInfo.ConvertTime(entry.End, TimeZoneInfo.Local).DateTime;
        var startDate = new DatePicker { SelectedDate = from.Date }; var endDate = new DatePicker { SelectedDate = to.Date };
        var startTime = new TextBox { Text = from.ToString("HH:mm:ss") }; var endTime = new TextBox { Text = to.ToString("HH:mm:ss") };
        Theme.Field(body, "Start date", startDate); Theme.Field(body, "Start time · 24-hour time", startTime);
        Theme.Field(body, "End date", endDate); Theme.Field(body, "End time · 24-hour time", endTime);
        var choices = new[] { "Preserve original time / choose if ambiguous", "First occurrence · before clocks turn back", "Second occurrence · after clocks turn back" };
        var startOccurrence = new ComboBox { ItemsSource = choices, SelectedIndex = 0 }; var endOccurrence = new ComboBox { ItemsSource = choices, SelectedIndex = 0 };
        var startFold = new StackPanel(); var endFold = new StackPanel();
        Theme.Field(startFold, "Start time when clocks turn back", startOccurrence); Theme.Field(endFold, "End time when clocks turn back", endOccurrence);
        body.Children.Add(startFold); body.Children.Add(endFold);
        void UpdateFoldControls()
        {
            startFold.Visibility = startDate.SelectedDate is { } sd && TimeOnly.TryParse(startTime.Text, out var st) && TimeZoneInfo.Local.IsAmbiguousTime(sd.Date + st.ToTimeSpan()) ? Visibility.Visible : Visibility.Collapsed;
            endFold.Visibility = endDate.SelectedDate is { } ed && TimeOnly.TryParse(endTime.Text, out var et) && TimeZoneInfo.Local.IsAmbiguousTime(ed.Date + et.ToTimeSpan()) ? Visibility.Visible : Visibility.Collapsed;
        }
        startDate.SelectedDateChanged += (_, _) => UpdateFoldControls(); endDate.SelectedDateChanged += (_, _) => UpdateFoldControls();
        startTime.TextChanged += (_, _) => UpdateFoldControls(); endTime.TextChanged += (_, _) => UpdateFoldControls(); UpdateFoldControls();
        var error = Theme.Text("", 12, "#A9413C"); body.Children.Add(error);
        body.Children.Add(Theme.Row(Theme.Button("Save entry", () =>
        {
            try
            {
                if (startDate.SelectedDate == null || endDate.SelectedDate == null || !TimeOnly.TryParse(startTime.Text, CultureInfo.CurrentCulture, out var st) || !TimeOnly.TryParse(endTime.Text, CultureInfo.CurrentCulture, out var et)) throw new ArgumentException("Enter valid dates and times.");
                var sd = DateOnly.FromDateTime(startDate.SelectedDate.Value); var ed = DateOnly.FromDateTime(endDate.SelectedDate.Value);
                bool? startFirst = startOccurrence.SelectedIndex == 0 ? null : startOccurrence.SelectedIndex == 1;
                bool? endFirst = endOccurrence.SelectedIndex == 0 ? null : endOccurrence.SelectedIndex == 1;
                var begin = Reports.ResolveEntryTime(sd, st, TimeZoneInfo.Local, entry?.Start, startFirst); var finish = Reports.ResolveEntryTime(ed, et, TimeZoneInfo.Local, entry?.End, endFirst);
                try { if (app.Execute(t => t.SaveEntry(entryId, names.Text, begin, finish, false), false)) window.Close(); }
                catch (OverlapException ex) { if (Theme.Confirm(window, ex.Message, "Overlapping time") && app.Execute(t => t.SaveEntry(entryId, names.Text, begin, finish, true))) window.Close(); }
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }, true), Theme.Button("Cancel", window.Close)));
        window.ShowDialog();
    }
}
