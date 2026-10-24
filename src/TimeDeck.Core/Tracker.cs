namespace TimeDeck.Core;

public enum TickResult { None, Reminder, AutoStopped }
public sealed class OvertimeRequiredException() : Exception("This is outside your work hours. Start an overtime timer without an automatic cutoff?");
public sealed class OverlapException() : Exception("This entry overlaps another entry or the running timer. Save the overlap anyway?");

public sealed class Tracker(AppState state, TimeZoneInfo zone)
{
    public AppState State { get; } = state;
    public TimeZoneInfo Zone { get; private set; } = zone;
    public bool ReconcileCutoff(DateTimeOffset now)
    {
        if (State.Running?.Cutoff is { } end && now >= end) { Stop(end); return true; }
        return false;
    }
    public void Press(Guid activityId, DateTimeOffset now, bool overtimeConfirmed = false)
    {
        ReconcileCutoff(now);
        var activity = State.Activities.Single(a => a.Id == activityId);
        if (State.Running?.ActivityId == activityId) { Stop(now); return; }
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var outside = State.Settings.AutoStop && (time < State.Settings.WorkStart || time >= State.Settings.WorkEnd);
        if (outside && !overtimeConfirmed) throw new OvertimeRequiredException();
        Stop(now);
        State.Running = new RunningTimer
        {
            ActivityId = activity.Id,
            Label = activity.Name,
            Start = now.ToUniversalTime(),
            Overtime = outside,
            Cutoff = State.Settings.AutoStop && !outside ? Finish(now) : null,
            NextReminder = now.AddMinutes(State.Settings.ReminderMinutes).ToUniversalTime()
        };
    }
    public void Stop(DateTimeOffset now)
    {
        if (State.Running is not { } run) return;
        var end = run.Cutoff is { } cutoff && now > cutoff ? cutoff : now;
        if (end > run.Start) State.Entries.Add(new Entry { ActivityId = run.ActivityId, Label = run.Label, Start = run.Start, End = end.ToUniversalTime() });
        State.Running = null;
    }
    public TickResult Tick(DateTimeOffset now)
    {
        if (ReconcileCutoff(now)) return TickResult.AutoStopped;
        if (State.Running is not { } run || !State.Settings.Reminders) return TickResult.None;
        if (now < run.NextReminder) return TickResult.None;
        run.ReminderPending = true;
        run.NextReminder = now.AddMinutes(15).ToUniversalTime();
        return TickResult.Reminder;
    }
    public void Acknowledge(DateTimeOffset now, bool changeActivity = false)
    {
        if (State.Running is not { } run) return;
        run.ReminderPending = false;
        run.NextReminder = now.AddMinutes(changeActivity ? 15 : State.Settings.ReminderMinutes).ToUniversalTime();
    }
    public void ChangeSettings(Settings settings, DateTimeOffset now)
    {
        settings.Validate(); ReconcileCutoff(now);
        State.Settings = settings;
        if (State.Running is not { } run) return;
        if (!run.Overtime && settings.AutoStop)
        {
            var finish = Finish(now);
            if (finish <= now) { run.Cutoff = null; Stop(now); return; }
            run.Cutoff = finish;
        }
        else run.Cutoff = null;
        run.ReminderPending = false;
        run.NextReminder = now.AddMinutes(settings.ReminderMinutes).ToUniversalTime();
    }
    public void UpdateTimeZone(TimeZoneInfo zone, DateTimeOffset now)
    {
        ReconcileCutoff(now); Zone = zone;
        if (State.Running is { Overtime: false } run && State.Settings.AutoStop)
        {
            var finish = Finish(now);
            run.Cutoff = finish > now ? finish : now;
            ReconcileCutoff(now);
        }
    }
    private DateTimeOffset Finish(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
        return Reports.Instant(date, State.Settings.WorkEnd, Zone);
    }
    public void Move(Guid id, int destination)
    {
        if (destination is < 0 or >= 29) throw new ArgumentException("Choose an activity slot between 1 and 29.");
        var a = State.Activities.Single(a => a.Id == id);
        var b = State.Activities.FirstOrDefault(a => a.Slot == destination);
        if (b != null) b.Slot = a.Slot;
        a.Slot = destination;
    }
    public Activity SaveActivity(Guid? id, int slot, string name, string icon, string color, string category)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("Enter a unique activity name (1–80 characters).");
        if (State.Activities.Any(a => a.Id != id && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("An activity already has this name.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Use a colour such as #2F7F6A.");
        if (slot is < 0 or >= 29) throw new ArgumentException("Choose a slot between 1 and 29.");
        var a = id.HasValue ? State.Activities.Single(a => a.Id == id) : new Activity { Slot = slot };
        if (!id.HasValue)
        {
            if (State.Activities.Any(a => a.Slot == slot)) throw new ArgumentException("This slot is occupied.");
            State.Activities.Add(a);
        }
        else if (a.Slot != slot) Move(a.Id, slot);
        a.Name = name; a.Icon = string.IsNullOrWhiteSpace(icon) ? "★" : icon.Trim(); a.Color = color; a.Category = category;
        foreach (var entry in State.Entries.Where(e => e.ActivityId == a.Id)) entry.Label = name;
        if (State.Running?.ActivityId == a.Id) State.Running.Label = name;
        return a;
    }
    public void DeleteActivity(Guid id)
    {
        foreach (var e in State.Entries.Where(e => e.ActivityId == id)) e.ActivityId = null;
        if (State.Running?.ActivityId == id) State.Running.ActivityId = null;
        State.Activities.RemoveAll(a => a.Id == id);
    }
    public Entry SaveEntry(Guid? id, string label, DateTimeOffset start, DateTimeOffset end, bool allowOverlap)
    {
        label = label.Trim();
        if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || end <= start) throw new ArgumentException("Enter an activity name and an end time after the start time.");
        if (!allowOverlap && (State.Entries.Any(e => e.Id != id && e.Start < end && e.End > start) ||
            State.Running is { } run && run.Start < end && (run.Cutoff ?? DateTimeOffset.MaxValue) > start)) throw new OverlapException();
        var e = id.HasValue ? State.Entries.Single(e => e.Id == id) : new Entry();
        var activity = State.Activities.FirstOrDefault(a => string.Equals(a.Name, label, StringComparison.OrdinalIgnoreCase));
        e.Label = activity?.Name ?? label; e.ActivityId = activity?.Id;
        e.Start = start.ToUniversalTime(); e.End = end.ToUniversalTime();
        if (!id.HasValue) State.Entries.Add(e);
        return e;
    }
}
