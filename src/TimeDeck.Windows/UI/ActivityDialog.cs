using System.Windows;
using System.Windows.Controls;
using TimeDeck.Core;

namespace TimeDeck.Windows.UI;

internal static class ActivityDialog
{
    public static void Show(Window owner, App app, int slot)
    {
        var a = app.State.Activities.FirstOrDefault(a => a.Slot == slot);
        var window = Theme.Dialog(a == null ? "Add activity" : "Edit activity", owner, 510, 720);
        var body = new StackPanel { Margin = new(26) }; window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(Theme.Heading(a == null ? "A new activity" : "Make it yours"));
        body.Children.Add(Theme.Help("Choose a name, icon and colour. Moving to an occupied slot swaps the two buttons."));
        var name = new TextBox { Text = a?.Name ?? "", MaxLength = 80 }; Theme.Field(body, "Activity name", name);
        var icon = new TextBox { Text = a?.Icon ?? "★", MaxLength = 30 }; Theme.Field(body, "Icon · paste an emoji or use Windows + .", icon);
        var icons = new WrapPanel { Margin = new(0, 0, 0, 12) };
        foreach (var glyph in new[] { "👥", "✉️", "🗓️", "💬", "🎯", "☕", "🍽️", "🌿", "🎫", "💻", "🖨️", "🔑", "📞", "📚", "🛠️", "★" })
        { var b = Theme.Button(glyph, () => icon.Text = glyph); b.FontSize = 21; b.Padding = new(7, 4, 7, 4); b.Margin = new(0, 0, 5, 5); icons.Children.Add(b); }
        body.Children.Add(icons);
        var color = new TextBox { Text = a?.Color ?? "#2F7F6A", MaxLength = 7 }; Theme.Field(body, "Colour · #RRGGBB", color);
        var swatches = new WrapPanel { Margin = new(0, 0, 0, 12) };
        foreach (var c in new[] { "#2F7F6A", "#C7772B", "#4A6FB5", "#B5486A", "#8A6BBD", "#B08F1F", "#3F8F9C", "#6F7F2F", "#D1584A", "#5A6B7B" })
        { var b = Theme.Button("", () => color.Text = c); b.Background = Theme.Brush(c); b.Width = 30; b.Height = 27; b.Padding = new(0); b.ToolTip = c; swatches.Children.Add(b); }
        body.Children.Add(swatches);
        var category = new ComboBox { IsEditable = true, ItemsSource = new[] { "Work", "IT Support", "Breaks", "Other" }, Text = a?.Category ?? "Other" }; Theme.Field(body, "Category", category);
        var destination = new ComboBox { ItemsSource = Enumerable.Range(1, 29).Select(n => $"Slot {n}  ·  row {(n - 1) / 8 + 1}, column {(n - 1) % 8 + 1}").ToArray(), SelectedIndex = slot, IsEnabled = a != null }; Theme.Field(body, "Destination slot", destination);
        var error = Theme.Text("", 12, "#A9413C"); body.Children.Add(error);
        body.Children.Add(Theme.Row(Theme.Button("Save activity", () =>
        {
            try { if (app.Execute(t => t.SaveActivity(a?.Id, destination.SelectedIndex, name.Text, icon.Text, color.Text, category.Text), false)) window.Close(); }
            catch (Exception ex) { error.Text = ex.Message; }
        }, true), Theme.Button("Cancel", window.Close)));
        if (a != null) body.Children.Add(Theme.Row(Theme.Button("Delete activity", () => { if (Theme.Confirm(window, "Delete this button? Past entries are kept.") && app.Execute(t => t.DeleteActivity(a.Id))) window.Close(); })));
        window.Loaded += (_, _) => name.Focus(); window.ShowDialog();
    }
}
