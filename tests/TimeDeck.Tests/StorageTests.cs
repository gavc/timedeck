using System.Text.Json;
using TimeDeck.Core;
using static TimeDeck.Tests.TrackerTests;

namespace TimeDeck.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TimeDeck-tests-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    [Fact]
    public void Roundtrip_preserves_running_timer_and_stable_ids()
    {
        var (s, t) = Make(); t.Press(s.Activities[0].Id, At("09:00"));
        var store = new StateStore(directory); store.Save(s);
        var restored = store.Load();
        Assert.Equal(s.Running!.SessionId, restored.Running!.SessionId);
        Assert.Equal(s.Activities[0].Id, restored.Activities[0].Id);
        Assert.Equal(At("18:00"), restored.Running.Cutoff);
    }
    [Fact]
    public void Corrupt_primary_recovers_backup_without_losing_corrupt_file()
    {
        var store = new StateStore(directory); var s = AppState.CreateDefault();
        store.Save(s); s.Activities[0].Name = "Changed"; store.Save(s);
        File.WriteAllText(Path.Combine(directory, "state.json"), "corrupt");
        var recovered = store.Load();
        Assert.Equal("HR", recovered.Activities[0].Name);
        Assert.NotNull(store.RecoveryMessage);
        Assert.NotEmpty(Directory.GetFiles(directory, "state.corrupt-*.json"));
        store.Save(recovered);
        Assert.Equal("HR", store.Load().Activities[0].Name);
    }
    [Fact]
    public void Invalid_primary_without_backup_does_not_reset_to_defaults()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "state.json"), "{}");
        Assert.Throws<InvalidDataException>(() => new StateStore(directory).Load());
    }
    [Fact]
    public void Native_backup_rejects_duplicate_slots_and_invalid_settings()
    {
        var s = AppState.CreateDefault(); s.Activities[1].Slot = 0;
        Assert.Throws<InvalidDataException>(() => Backup.Parse(JsonSerializer.Serialize(s, Backup.Json)));
        s.Activities[1].Slot = 1; s.Settings.ReminderMinutes = 0;
        Assert.Throws<InvalidDataException>(() => Backup.Parse(JsonSerializer.Serialize(s, Backup.Json)));
    }
    [Theory]
    [InlineData("#2F7F6A", true)]
    [InlineData("#ffffff", true)]
    [InlineData("#000000", true)]
    [InlineData("#AABBCC", true)]
    [InlineData("2F7F6A", false)]
    [InlineData("#2F7F6", false)]
    [InlineData("#2F7F6AA", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("#AABBCC\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Valid_color_format_is_enforced(string? color, bool expected)
    {
        Assert.Equal(expected, Backup.IsValidColor(color));
    }
    [Fact]
    public void ImportPreview_exposes_state_without_duplication()
    {
        var s = AppState.CreateDefault();
        var preview = Backup.Parse(JsonSerializer.Serialize(s, Backup.Json));
        Assert.NotNull(preview.State);
        Assert.Equal(s.Activities.Count, preview.State.Activities.Count);
    }
}
