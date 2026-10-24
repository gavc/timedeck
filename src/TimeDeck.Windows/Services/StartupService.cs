using Microsoft.Win32;

namespace TimeDeck.Windows.Services;

internal static class StartupService
{
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("TimeDeck", "\"" + Environment.ProcessPath + "\" --tray");
        else key.DeleteValue("TimeDeck", false);
    }
}
