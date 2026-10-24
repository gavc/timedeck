using System.Globalization;
using System.Text;

namespace TimeDeck.Core;

public sealed record ReportRow(Guid? EntryId, Guid? ActivityId, string Label, DateTimeOffset Start, DateTimeOffset End, bool Live)
{
    public TimeSpan Duration => End - Start;
}
public static class Reports
{
    public static DateTimeOffset ResolveEntryTime(DateOnly date, TimeOnly time, TimeZoneInfo zone, DateTimeOffset? original = null, bool? earlierOccurrence = null)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (original is { } prior && earlierOccurrence == null)
        {
            var priorLocal = TimeZoneInfo.ConvertTime(prior, zone).DateTime;
            if (priorLocal.Ticks / TimeSpan.TicksPerSecond == local.Ticks / TimeSpan.TicksPerSecond) return prior;
        }
        if (zone.IsInvalidTime(local)) throw new ArgumentException("This time does not exist because the clocks change. Choose another time.");
        if (zone.IsAmbiguousTime(local))
        {
            if (earlierOccurrence == null) throw new ArgumentException("This time occurs twice when the clocks change. Choose the first or second occurrence.");
            var choices = zone.GetAmbiguousTimeOffsets(local).Select(offset => new DateTimeOffset(local, offset).ToUniversalTime()).Order().ToArray();
            return earlierOccurrence.Value ? choices[0] : choices[1];
        }
        return Instant(date, time, zone);
    }
    public static DateTimeOffset Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // Use first valid wall time in a spring-forward gap, and standard offset at a fall-back ambiguity.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
    public static List<ReportRow> Day(AppState state, DateOnly date, DateTimeOffset now, TimeZoneInfo zone)
    {
        var lo = Instant(date, TimeOnly.MinValue, zone); var hi = Instant(date.AddDays(1), TimeOnly.MinValue, zone);
        var rows = state.Entries.Select(e => new ReportRow(e.Id, e.ActivityId, state.Activities.FirstOrDefault(a => a.Id == e.ActivityId)?.Name ?? e.Label, e.Start, e.End, false)).ToList();
        if (state.Running is { } run)
            rows.Add(new(null, run.ActivityId, run.Label, run.Start, run.Cutoff is { } end && now > end ? end : now, true));
        return rows.Where(r => r.Start < hi && r.End > lo).Select(r => r with { Start = r.Start < lo ? lo : r.Start, End = r.End > hi ? hi : r.End }).Where(r => r.End > r.Start).OrderBy(r => r.Start).ToList();
    }
    public static string Csv(IEnumerable<ReportRow> rows, TimeZoneInfo zone)
    {
        var text = new StringBuilder("Date,Start,End,Activity,Minutes\r\n");
        foreach (var r in rows.Where(r => !r.Live))
        {
            var start = TimeZoneInfo.ConvertTime(r.Start, zone); var end = TimeZoneInfo.ConvertTime(r.End, zone);
            var label = r.Label;
            if (label.TrimStart().StartsWith('=') || label.TrimStart().StartsWith('+') || label.TrimStart().StartsWith('-') || label.TrimStart().StartsWith('@') || label.StartsWith('\t') || label.StartsWith('\r')) label = "'" + label;
            text.AppendLine(string.Join(',', start.ToString("yyyy-MM-dd"), start.ToString("HH:mm:sszzz"), end.ToString("HH:mm:sszzz"), "\"" + label.Replace("\"", "\"\"") + "\"", r.Duration.TotalMinutes.ToString("0.####", CultureInfo.InvariantCulture)));
        }
        return text.ToString();
    }
    public static string Duration(TimeSpan duration) => duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m" : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}m" : "<1m";
}
