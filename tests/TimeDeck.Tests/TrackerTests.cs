using TimeDeck.Core;

namespace TimeDeck.Tests;

public sealed class TrackerTests
{
    internal static DateTimeOffset At(string time) => DateTimeOffset.Parse("2026-10-23T" + time + "+00:00");
    internal static (AppState, Tracker) Make()
    {
        var state = AppState.CreateDefault();
        return (state, new Tracker(state, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Switch_closes_previous_activity_at_exact_start_of_next()
    {
        var (s, t) = Make();
        t.Press(s.Activities[0].Id, At("09:00"));
        t.Press(s.Activities[1].Id, At("10:00"));
        Assert.Single(s.Entries);
        Assert.Equal(At("10:00"), s.Entries[0].End);
        Assert.Equal(At("10:00"), s.Running!.Start);
        t.Press(s.Activities[1].Id, At("10:30"));
        Assert.Null(s.Running);
        Assert.Equal(TimeSpan.FromMinutes(30), s.Entries[1].End - s.Entries[1].Start);
    }
    [Fact]
    public void Delayed_tick_caps_timer_at_18_and_never_duplicates_entry()
    {
        var (s, t) = Make();
        t.Press(s.Activities[0].Id, At("17:00"));
        Assert.Equal(TickResult.AutoStopped, t.Tick(At("23:00")));
        Assert.Null(s.Running);
        Assert.Equal(At("18:00"), Assert.Single(s.Entries).End);
        t.Tick(At("23:30"));
        Assert.Single(s.Entries);
    }
    [Fact]
    public void Restart_uses_persisted_cutoff_before_reminding()
    {
        var (s, t) = Make(); t.Press(s.Activities[0].Id, At("17:00"));
        var restored = new Tracker(s, TimeZoneInfo.Utc);
        Assert.Equal(TickResult.AutoStopped, restored.Tick(At("23:00")));
        Assert.Equal(At("18:00"), s.Entries[0].End);
    }
    [Fact]
    public void Outside_hours_requires_confirmation_and_overtime_is_exempt()
    {
        var (s, t) = Make();
        Assert.Throws<OvertimeRequiredException>(() => t.Press(s.Activities[0].Id, At("19:00")));
        Assert.Null(s.Running);
        t.Press(s.Activities[0].Id, At("19:00"), true);
        Assert.True(s.Running!.Overtime);
        t.Tick(At("23:00"));
        Assert.NotNull(s.Running);
    }
    [Fact]
    public void Reminder_at_60_minutes_repeats_after_15_without_replay()
    {
        var (s, t) = Make(); t.Press(s.Activities[0].Id, At("09:00"));
        Assert.Equal(TickResult.None, t.Tick(At("09:59")));
        Assert.Equal(TickResult.Reminder, t.Tick(At("10:00")));
        Assert.True(s.Running!.ReminderPending);
        Assert.Equal(TickResult.None, t.Tick(At("10:14")));
        Assert.Equal(TickResult.Reminder, t.Tick(At("12:00")));
        Assert.Equal(TickResult.None, t.Tick(At("12:00")));
        t.Acknowledge(At("12:00"));
        Assert.False(s.Running.ReminderPending);
        Assert.Equal(At("09:00"), s.Running.Start);
        Assert.Equal(TickResult.None, t.Tick(At("12:59")));
        Assert.Equal(TickResult.Reminder, t.Tick(At("13:00")));
    }
    [Fact]
    public void Settings_past_finish_stop_now_without_retroactive_loss()
    {
        var (s, t) = Make(); t.Press(s.Activities[0].Id, At("09:00"));
        t.ChangeSettings(new Settings { WorkEnd = new TimeOnly(11, 0) }, At("12:00"));
        Assert.Equal(At("12:00"), Assert.Single(s.Entries).End);
    }
    [Fact]
    public void Reminder_interval_change_starts_fresh_period()
    {
        var (s, t) = Make(); t.Press(s.Activities[0].Id, At("09:00"));
        t.ChangeSettings(new Settings { ReminderMinutes = 30 }, At("09:45"));
        Assert.Equal(TickResult.None, t.Tick(At("10:14")));
        Assert.Equal(TickResult.Reminder, t.Tick(At("10:15")));
    }
    [Fact]
    public void Move_swaps_or_leaves_gap_and_cannot_use_fixed_controls()
    {
        var (s, t) = Make(); var first = s.Activities[0]; var second = s.Activities[1];
        t.Move(first.Id, 1); Assert.Equal(1, first.Slot); Assert.Equal(0, second.Slot);
        t.Move(first.Id, 28); Assert.Equal(28, first.Slot);
        Assert.DoesNotContain(s.Activities, a => a.Slot == 1);
        Assert.Throws<ArgumentException>(() => t.Move(first.Id, 29));
    }
    [Fact]
    public void Rename_updates_history_delete_preserves_label_and_running_timer()
    {
        var (s, t) = Make(); var a = s.Activities[0];
        t.Press(a.Id, At("09:00")); t.Stop(At("10:00")); t.Press(a.Id, At("11:00"));
        t.SaveActivity(a.Id, a.Slot, "Renamed", "★", "#224466", "Work");
        Assert.Equal("Renamed", s.Entries[0].Label); Assert.Equal("Renamed", s.Running!.Label);
        t.DeleteActivity(a.Id); t.Stop(At("12:00"));
        Assert.All(s.Entries, e => Assert.Equal("Renamed", e.Label));
    }
    [Fact]
    public void Duplicate_names_rejected_and_clock_rollback_never_creates_negative_entry()
    {
        var (s, t) = Make(); var a = s.Activities[0];
        Assert.Throws<ArgumentException>(() => t.SaveActivity(null, 28, a.Name, "★", "#224466", "Other"));
        t.Press(a.Id, At("10:00")); t.Stop(At("09:00"));
        Assert.Empty(s.Entries); Assert.Null(s.Running);
    }
}
