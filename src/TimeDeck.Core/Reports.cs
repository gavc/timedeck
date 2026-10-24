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
        var lo = Instant(date, TimeOnly.MinValue, zone);
        var hi = Instant(date.AddDays(1), TimeOnly.MinValue, zone);
        var rows = new List<ReportRow>();

        foreach (var e in state.Entries)
        {
            if (e.Start < hi && e.End > lo)
            {
                var start = e.Start < lo ? lo : e.Start;
                var end = e.End > hi ? hi : e.End;
                if (end > start)
                {
                    var label = state.Activities.FirstOrDefault(a => a.Id == e.ActivityId)?.Name ?? e.Label;
                    rows.Add(new ReportRow(e.Id, e.ActivityId, label, start, end, false));
                }
            }
        }

        if (state.Running is { } run)
        {
            var runEnd = run.Cutoff is { } cutoff && now > cutoff ? cutoff : now;
            if (run.Start < hi && runEnd > lo)
            {
                var start = run.Start < lo ? lo : run.Start;
                var end = runEnd > hi ? hi : runEnd;
                if (end > start)
                {
                    rows.Add(new ReportRow(null, run.ActivityId, run.Label, start, end, true));
                }
            }
        }

        rows.Sort((a, b) => a.Start.CompareTo(b.Start));
        return rows;
    }
    public static string Csv(IEnumerable<ReportRow> rows, TimeZoneInfo zone)
    {
        var text = new StringBuilder("Date,Start,End,Activity,Minutes\r\n");
        foreach (var r in rows.Where(r => !r.Live))
        {
            var start = TimeZoneInfo.ConvertTime(r.Start, zone);
            var end = TimeZoneInfo.ConvertTime(r.End, zone);
            var label = r.Label;
            var trimmed = label.AsSpan().TrimStart();
            if ((!trimmed.IsEmpty && (trimmed[0] is '=' or '+' or '-' or '@')) || label.StartsWith('\t') || label.StartsWith('\r'))
            {
                label = "'" + label;
            }
            text.Append(start.ToString("yyyy-MM-dd"))
                .Append(',')
                .Append(start.ToString("HH:mm:sszzz"))
                .Append(',')
                .Append(end.ToString("HH:mm:sszzz"))
                .Append(",\"")
                .Append(label.Replace("\"", "\"\""))
                .Append("\",")
                .Append(r.Duration.TotalMinutes.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
        }
        return text.ToString();
    }
    public static string Duration(TimeSpan duration) => duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m" : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}m" : "<1m";
}
