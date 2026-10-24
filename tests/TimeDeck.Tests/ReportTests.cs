using TimeDeck.Core;
using static TimeDeck.Tests.TrackerTests;

namespace TimeDeck.Tests;

public sealed class ReportTests
{
    [Theory]
    [InlineData("th-TH")]
    [InlineData("fi-FI")]
    public void Csv_dates_and_times_are_invariant_across_regional_settings(string culture)
    {
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        var row = new ReportRow(Guid.NewGuid(), null, "Work", At("09:00"), At("10:00"), false);
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            var csv = Reports.Csv([row], TimeZoneInfo.Utc);
            Assert.Contains("2026-10-23,09:00:00+00:00,10:00:00+00:00,\"Work\",60", csv);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void Editing_unchanged_fold_time_preserves_original_instant_and_fractional_seconds()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var original = DateTimeOffset.Parse("2026-11-01T01:10:00.123-04:00");
        Assert.Equal(original, Reports.ResolveEntryTime(new(2026, 11, 1), new(1, 10), zone, original));
        Assert.Throws<ArgumentException>(() => Reports.ResolveEntryTime(new(2026, 11, 1), new(1, 20), zone, original));
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T05:20Z"), Reports.ResolveEntryTime(new(2026, 11, 1), new(1, 20), zone, original, true));
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:20Z"), Reports.ResolveEntryTime(new(2026, 11, 1), new(1, 20), zone, original, false));
    }
    [Fact]
    public void Manual_name_case_links_to_canonical_activity_label()
    {
        var (s, t) = Make(); t.SaveEntry(null, "email", At("09:00"), At("10:00"), false);
        Assert.Equal("Email", Assert.Single(s.Entries).Label);
        Assert.Equal(s.Activities[1].Id, s.Entries[0].ActivityId);
    }
    [Fact]
    public void Midnight_slices_include_previous_day_entries()
    {
        var s = AppState.CreateDefault();
        s.Entries.Add(new Entry { Label = "Night", Start = At("23:30"), End = At("23:30").AddHours(2) });
        var rows = Reports.Day(s, new DateOnly(2026, 10, 24), At("23:30").AddDays(1), TimeZoneInfo.Utc);
        Assert.Equal(TimeSpan.FromMinutes(90), Assert.Single(rows).Duration);
    }
    [Fact]
    public void Dst_day_is_23_hours_and_not_assumed_24()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var s = AppState.CreateDefault();
        s.Entries.Add(new Entry { Label = "DST", Start = DateTimeOffset.Parse("2026-03-08T05:00Z"), End = DateTimeOffset.Parse("2026-03-09T04:00Z") });
        Assert.Equal(TimeSpan.FromHours(23), Assert.Single(Reports.Day(s, new(2026, 3, 8), DateTimeOffset.Parse("2026-03-10T00:00Z"), zone)).Duration);
    }
    [Fact]
    public void Csv_excludes_live_rows_and_protects_formula_and_quotes()
    {
        var (s, t) = Make();
        s.Entries.Add(new Entry { Label = "=SUM(1,2)\"", Start = At("09:00"), End = At("10:00") });
        t.Press(s.Activities[0].Id, At("10:00"));
        var csv = Reports.Csv(Reports.Day(s, new(2026, 10, 23), At("11:00"), TimeZoneInfo.Utc), TimeZoneInfo.Utc);
        Assert.Contains("\"'=SUM(1,2)\"\"\"", csv);
        Assert.Equal(2, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
    [Theory]
    [InlineData("=SUM(1,2)", "'=SUM(1,2)")]
    [InlineData("+12345", "'+12345")]
    [InlineData("-cmd|' /C calc'!A0", "'-cmd|' /C calc'!A0")]
    [InlineData("@SUM(A1:A2)", "'@SUM(A1:A2)")]
    [InlineData("\tTabPrefix", "'\tTabPrefix")]
    [InlineData("\rReturnPrefix", "'\rReturnPrefix")]
    [InlineData("Normal Activity", "Normal Activity")]
    public void Csv_protects_formula_injection_and_leaves_safe_labels(string input, string expected)
    {
        var row = new ReportRow(Guid.NewGuid(), null, input, At("09:00"), At("10:00"), false);
        var csv = Reports.Csv([row], TimeZoneInfo.Utc);
        Assert.Contains("\"" + expected.Replace("\"", "\"\"") + "\"", csv);
    }
    [Fact]
    public void Manual_entry_requires_positive_range_and_overlap_confirmation()
    {
        var (s, t) = Make();
        t.SaveEntry(null, "Manual", At("09:00"), At("10:00"), false);
        Assert.Throws<OverlapException>(() => t.SaveEntry(null, "Overlap", At("09:30"), At("10:30"), false));
        t.SaveEntry(null, "Overlap", At("09:30"), At("10:30"), true);
        Assert.Equal(2, s.Entries.Count);
        Assert.Throws<ArgumentException>(() => t.SaveEntry(null, "Bad", At("10:00"), At("09:00"), true));
    }
}
