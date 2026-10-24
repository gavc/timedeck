using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeDeck.Core;
using TimeDeck.Windows.Services;
using TimeDeck.Windows.UI;
using Forms = System.Windows.Forms;

namespace TimeDeck.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private bool ownsMutex;
    private readonly CancellationTokenSource stopping = new();
    private DispatcherTimer? clock;
    private Forms.NotifyIcon? tray;
    private MainWindow? deck;
    private StateStore store = null!;
    private Tracker tracker = null!;
    private bool reconciling;
    private DateTimeOffset nextRetry;
    public AppState State => tracker.State;
    public bool Quitting { get; private set; }
    public NotificationService Notifications { get; private set; } = null!;
    public event Action? StateChanged;
    public string DataDirectory => store.DirectoryPath;
    private readonly string pipeName = "TimeDeck-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..24];
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\" + pipeName, out ownsMutex);
        if (!ownsMutex)
        {
            try { using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly); pipe.Connect(3000); pipe.WriteByte(1); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { MessageBox.Show("Time Deck is already running but could not be opened. Try its system tray icon."); }
            Shutdown(); return;
        }
        var dataArg = Array.FindIndex(e.Args, a => a == "--data-dir");
        var directory = dataArg >= 0 && dataArg + 1 < e.Args.Length ? e.Args[dataArg + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeDeck");
        store = new StateStore(directory);
        try { tracker = new Tracker(store.Load(), TimeZoneInfo.Local); tracker.ReconcileCutoff(DateTimeOffset.UtcNow); store.Save(State); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Time Deck could not open your data", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); return; }
        Notifications = new NotificationService(() => Dispatcher.BeginInvoke(() => ShowDeck()));
        Notifications.Initialize();
        deck = new MainWindow(this); MainWindow = deck;
        CreateTray();
        _ = ListenForActivation();
        clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { Reconcile(); deck.Refresh(); UpdateTray(); }, Dispatcher);
        clock.Start();
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.TimeChanged += TimeChanged;
        SessionEnding += (_, _) => { if (tracker != null) Execute(t => t.Stop(DateTimeOffset.UtcNow)); };
        if (!e.Args.Contains("--tray")) deck.Show();
        if (store.RecoveryMessage != null) { ShowDeck(); MessageBox.Show(deck, store.RecoveryMessage, "Recovered backup"); }
        Reconcile();
    }
    private async Task ListenForActivation()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                var buffer = new byte[1]; await pipe.ReadExactlyAsync(buffer, stopping.Token);
                await Dispatcher.InvokeAsync(ShowDeck);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(250); }
        }
    }
    public void ShowDeck()
    {
        if (deck == null) return;
        Reconcile(); deck.Show(); if (deck.WindowState == WindowState.Minimized) deck.WindowState = WindowState.Normal; deck.Activate(); deck.Focus(); deck.Refresh();
    }
    private void CreateTray()
    {
        tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "Time Deck", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Time Deck", null, (_, _) => Dispatcher.Invoke(ShowDeck));
        menu.Items.Add("Stop tracking", null, (_, _) => Dispatcher.Invoke(() => Execute(t => t.Stop(DateTimeOffset.UtcNow))));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDeck);
    }
    private void UpdateTray()
    {
        if (tray == null) return;
        var text = State.Running is { } run ? "Time Deck · " + run.Label : "Time Deck · Not tracking";
        tray.Text = text.Length > 63 ? text[..60] + "…" : text;
        if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Items[1].Enabled = State.Running != null;
    }
    public bool Execute(Action<Tracker> change, bool showErrors = true)
    {
        var before = Backup.Clone(State);
        try
        {
            change(tracker); store.Save(State);
        }
        catch (Exception ex)
        {
            tracker = new Tracker(before, TimeZoneInfo.Local); deck?.Render();
            if (!showErrors) throw;
            MessageBox.Show(deck, ex.Message + "\n\nThe change was not saved. Your previous data is intact.", "Time Deck", MessageBoxButton.OK, MessageBoxImage.Warning); return false;
        }
        if (State.Running?.ReminderPending != true) Notifications?.ClearReminder();
        deck?.Render(); StateChanged?.Invoke(); UpdateTray(); return true;
    }
    public void Reconcile()
    {
        if (reconciling || tracker == null || State.Running is not { } run) return;
        var now = DateTimeOffset.UtcNow;
        if (now < nextRetry) return;
        if (run.Cutoff is { } cutoff && now >= cutoff || State.Settings.Reminders && now >= run.NextReminder)
        {
            reconciling = true;
            try
            {
                TickResult result = TickResult.None;
                if (Execute(t => result = t.Tick(now)))
                {
                    if (result == TickResult.Reminder && State.Running is { } active) Notifications.ShowReminder(active, now);
                    else if (result == TickResult.AutoStopped) Notifications.ShowInfo("Workday finished", "Tracking stopped at your configured finish time.");
                }
                else nextRetry = DateTimeOffset.UtcNow.AddSeconds(30);
            }
            finally { reconciling = false; }
        }
    }
    public bool Restore(AppState replacement)
    {
        try
        {
            var saved = Backup.Clone(replacement);
            // Restored backups never silently restart an old session.
            if (saved.Running != null) { var t = new Tracker(saved, TimeZoneInfo.Local); t.Stop(DateTimeOffset.UtcNow); }
            saved.Settings.StartWithWindows = State.Settings.StartWithWindows;
            store.ExportSafetyCopy(State); store.Save(saved);
            tracker = new Tracker(saved, TimeZoneInfo.Local); deck?.Render(); StateChanged?.Invoke(); UpdateTray(); return true;
        }
        catch (Exception ex) { Theme.Error(deck!, ex); return false; }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(Reconcile); }
    private void TimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        TimeZoneInfo.ClearCachedData(); Execute(t => t.UpdateTimeZone(TimeZoneInfo.Local, DateTimeOffset.UtcNow)); Reconcile();
    });
    public void ExitApp()
    {
        if (!Execute(t => t.Stop(DateTimeOffset.UtcNow))) return;
        Quitting = true; Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Quitting = true; clock?.Stop(); stopping.Cancel();
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.TimeChanged -= TimeChanged;
        Notifications?.Dispose(); tray?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex(); instance?.Dispose(); stopping.Dispose(); base.OnExit(e);
    }
}
