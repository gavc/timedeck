using System.Text;
using System.Text.Json;

namespace TimeDeck.Core;

public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? RecoveryMessage { get; private set; }
    private string Primary => Path.Combine(DirectoryPath, "state.json");
    private string Previous => Path.Combine(DirectoryPath, "state.backup.json");
    public AppState Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(Primary) && !File.Exists(Previous)) return AppState.CreateDefault();
        try { return Read(Primary); }
        catch (Exception primaryError) when (primaryError is IOException or JsonException or InvalidDataException)
        {
            AppState recovered;
            try { recovered = Read(Previous); }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException)
            { throw new InvalidDataException("Time Deck data could not be read. Your files have been preserved in " + DirectoryPath + ". Restore a JSON backup before continuing.", primaryError); }
            var preserved = Path.Combine(DirectoryPath, "state.corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
            if (File.Exists(Primary)) File.Move(Primary, preserved);
            RecoveryMessage = "Recovered the last valid backup. The damaged file was preserved in " + DirectoryPath;
            return recovered;
        }
    }
    private static AppState Read(string path)
    {
        var text = File.ReadAllText(path);
        var preview = Backup.Parse(text);
        return preview.SelectActivities(preview.Activities.Select(a => a.Id));
    }
    public void Save(AppState state)
    {
        Backup.Validate(state); Directory.CreateDirectory(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, "state." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, Backup.Json));
                file.Write(bytes); file.Flush(true);
            }
            if (File.Exists(Primary)) File.Replace(temporary, Primary, Previous, true);
            else File.Move(temporary, Primary);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string ExportSafetyCopy(AppState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(state, Backup.Json), new UTF8Encoding(false)); return path;
    }
}
