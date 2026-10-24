using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimeDeck.Core;

public sealed class ImportPreview(AppState state)
{
    public AppState State => state;
    public IReadOnlyList<Activity> Activities => state.Activities;
    public int EntryCount => state.Entries.Count;
    public bool HasRunningTimer => state.Running != null;
    public AppState SelectActivities(IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet();
        if (selected.Count > 29) throw new ArgumentException("Choose at most 29 activities for the deck. All historical entries will be kept.");
        if (selected.Except(state.Activities.Select(a => a.Id)).Any()) throw new ArgumentException("Unknown activity selection.");
        var copy = Backup.Clone(state);
        copy.Activities = copy.Activities.Where(a => selected.Contains(a.Id)).ToList();
        foreach (var e in copy.Entries.Where(e => e.ActivityId.HasValue && !selected.Contains(e.ActivityId.Value))) e.ActivityId = null;
        if (copy.Running?.ActivityId is { } id && !selected.Contains(id)) copy.Running.ActivityId = null;
        Backup.Validate(copy);
        return copy;
    }
}
public static partial class Backup
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static AppState Clone(AppState state) => JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, Json), Json)!;
    public static ImportPreview Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("version", out _) || !root.TryGetProperty("activities", out _) || !root.TryGetProperty("entries", out _) || !root.TryGetProperty("settings", out _)) throw new InvalidDataException("This is not a Time Deck backup.");
            var state = JsonSerializer.Deserialize<AppState>(text, Json) ?? throw new InvalidDataException("Empty backup.");
            Validate(state); return new ImportPreview(state);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException or ArgumentException)
        { throw new InvalidDataException("The backup contains invalid data: " + ex.Message, ex); }
    }
    public static void Validate(AppState state)
    {
        if (state.Version != 1 || state.Activities == null || state.Entries == null || state.Settings == null) throw new InvalidDataException("Unsupported or incomplete Time Deck data.");
        try { state.Settings.Validate(); } catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
        if (state.Activities.Count > 29) throw new InvalidDataException("The deck has more than 29 activities.");
        if (state.Activities.Any(a => a == null || a.Id == Guid.Empty || a.Slot is < 0 or >= 29 || !ValidLabel(a.Name) || !IsValidColor(a.Color) || a.Icon == null || a.Category == null)) throw new InvalidDataException("Invalid activity in backup.");
        if (state.Activities.Select(a => a.Id).Distinct().Count() != state.Activities.Count || state.Activities.Select(a => a.Slot).Distinct().Count() != state.Activities.Count || state.Activities.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Activities.Count) throw new InvalidDataException("Duplicate activity ID, name or slot.");
        var ids = state.Activities.Select(a => a.Id).ToHashSet();
        if (state.Entries.Any(e => e == null || e.Id == Guid.Empty || !ValidLabel(e.Label) || e.End <= e.Start || e.Start.Year < 1900 || e.End.Year > 9998 || e.ActivityId.HasValue && !ids.Contains(e.ActivityId.Value)) || state.Entries.Select(e => e.Id).Distinct().Count() != state.Entries.Count) throw new InvalidDataException("Invalid or duplicate time entry.");
        if (state.Running is { } run && (run.SessionId == Guid.Empty || !ValidLabel(run.Label) || run.Start.Year < 1900 || run.Start.Year > 9998 || run.NextReminder.Year < 1900 || run.NextReminder.Year > 9998 || run.Cutoff < run.Start || run.ActivityId.HasValue && !ids.Contains(run.ActivityId.Value))) throw new InvalidDataException("Invalid running timer.");
    }
    private static bool ValidLabel(string? label) => !string.IsNullOrWhiteSpace(label) && label.Trim() == label && label.Length <= 80;
    public static bool IsValidColor(string? color) => color != null && HexColorRegex().IsMatch(color);

    [GeneratedRegex("^#[a-fA-F0-9]{6}$")]
    private static partial Regex HexColorRegex();
}
