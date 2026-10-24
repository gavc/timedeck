using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeDeck.Windows.UI;

internal static class Theme
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush Brush(string color)
    {
        if (!BrushCache.TryGetValue(color, out var brush))
        {
            brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            BrushCache[color] = brush;
        }
        return brush;
    }
    public static TextBlock Text(string text, double size = 14, string color = "#202C39") => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) };
    public static TextBlock Help(string text) => new() { Text = text, Foreground = Brush("#66788A"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 16) };
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text }; if (primary) b.Style = (Style)System.Windows.Application.Current.FindResource("Primary");
        b.Click += (_, _) => action(); return b;
    }
    public static StackPanel Row(params ReadOnlySpan<UIElement> children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
        foreach (var c in children) row.Children.Add(c);
        return row;
    }
    public static void Field(Panel panel, string label, UIElement control) { System.Windows.Automation.AutomationProperties.SetName(control, label); panel.Children.Add(Text(label, 12, "#66788A")); panel.Children.Add(control); }
    public static Window Dialog(string title, Window owner, double width = 500, double height = 600) => new() { Title = title, Owner = owner, Width = Math.Min(width, SystemParameters.WorkArea.Width - 24), Height = Math.Min(height, SystemParameters.WorkArea.Height - 24), MinWidth = Math.Min(width, SystemParameters.WorkArea.Width - 24), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
    public static bool Confirm(Window owner, string text, string title = "Time Deck") => MessageBox.Show(owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public static void Error(Window owner, Exception ex) => MessageBox.Show(owner, ex.Message, "Time Deck", MessageBoxButton.OK, MessageBoxImage.Warning);
}
