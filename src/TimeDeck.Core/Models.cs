namespace TimeDeck.Core;

public sealed class Activity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "★";
    public string Color { get; set; } = "#2F7F6A";
    public string Category { get; set; } = "Other";
}
public sealed class Entry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActivityId { get; set; }
    public string Label { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}
public sealed class RunningTimer
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public Guid? ActivityId { get; set; }
    public string Label { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? Cutoff { get; set; }
    public DateTimeOffset NextReminder { get; set; }
    public bool ReminderPending { get; set; }
    public bool Overtime { get; set; }
}
public sealed class Settings
{
    public TimeOnly WorkStart { get; set; } = new(6, 0);
    public TimeOnly WorkEnd { get; set; } = new(18, 0);
    public bool AutoStop { get; set; } = true;
    public bool Reminders { get; set; } = true;
    public int ReminderMinutes { get; set; } = 60;
    public bool StartWithWindows { get; set; }
    public bool TrayExplained { get; set; }
    public void Validate()
    {
        if (WorkStart >= WorkEnd) throw new ArgumentException("Workday start must be before finish (overnight schedules are not supported).");
        if (ReminderMinutes is < 1 or > 1440) throw new ArgumentException("Reminder interval must be between 1 and 1440 minutes.");
    }
}
public sealed class AppState
{
    public int Version { get; set; } = 1;
    public List<Activity> Activities { get; set; } = [];
    public List<Entry> Entries { get; set; } = [];
    public Settings Settings { get; set; } = new();
    public RunningTimer? Running { get; set; }
    public static AppState CreateDefault()
    {
        string[] names = ["HR", "Email", "Meeting", "One on One", "Focus Work", "Morning Tea", "Lunch", "Break", "FreshService", "AV", "Printing", "VC", "Licensing"];
        string[] icons = ["👥", "✉️", "🗓️", "💬", "🎯", "☕", "🍽️", "🌿", "🎫", "📽️", "🖨️", "📹", "🔑"];
        string[] colors = ["#2F7F6A", "#C7772B", "#4A6FB5", "#B5486A", "#8A6BBD", "#B08F1F", "#3F8F9C", "#6F7F2F", "#D1584A", "#5A6B7B"];
        var s = new AppState();
        for (var i = 0; i < names.Length; i++) s.Activities.Add(new Activity { Slot = i, Name = names[i], Icon = icons[i], Color = colors[i % colors.Length], Category = i >= 8 ? "IT Support" : i >= 5 ? "Breaks" : "Work" });
        return s;
    }
}
