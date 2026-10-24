# Native Windows Time Deck Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task.

**Goal:** Deliver a runnable native Windows Time Deck with 32 fixed slots, tray tracking, notifications, cutoff and editable reports.

**Architecture:** A dependency-free .NET core owns state, timer transitions, reports and durable JSON. WPF owns presentation; application services own tray, single-instance activation and Windows App SDK notifications independently of windows.

**Tech Stack:** C# / .NET 10, WPF, Windows Forms NotifyIcon, Windows App SDK, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-23-native-windows-design.md` (approved by user: “lets implement please”).

## Global Constraints

- Single user, local storage, Windows 10/11 x64; no account or server.
- Fixed 8 by 4 grid; slots 30–32 are Report, Edit, Settings.
- Default workday 06:00–18:00 and reminder 60 minutes, repeat 15 minutes.
- Preserve original HTML; export/import instead of browser-profile access.
- Work in this directory: it is not a Git repository, so no worktree or commits are available.

## Review Focus

- Clock changes and resume: no negative time or duplicate cutoff entries (Task 1).
- Invalid import and duplicate names: leave current data untouched (Task 2).
- Interrupted write/corrupt primary: preserve corrupt file and recover validated backup (Task 2).
- Window hidden: reminder/cutoff still run and notification activation opens the existing instance (Task 3).
- Cross-midnight and overlapping entries: correct report slices, explicit overlap override and safe CSV (Tasks 1/4).

### Task 1: Timer core and deck

Files: `src/TimeDeck.Core/{Models,Tracker,Reports}.cs`, `tests/TimeDeck.Tests/{TrackerTests,ReportTests}.cs` and project files.

Interfaces: `AppState` contains Activities, Entries, Settings and Running; `Tracker` consumes state and explicit UTC timestamps/timezone; produces Press, Stop, Tick, Acknowledge, ChangeSettings, Move, SaveActivity and DeleteActivity. `Reports.Day` produces clipped report rows and `Reports.Csv` exports completed rows.

- [x] Write tests for switching at one instant, 18:00 cutoff on delayed tick/restart, overtime confirmation, changed cutoff, 60/15 minute reminders, acknowledge, slot swap, invalid slots and rename/delete history.
- [x] Run `dotnet test` and observe missing implementation failure.
- [x] Implement models and tracker using explicit instants; test midnight/DST slicing, overlap validation and CSV protection before implementing reports.
- [x] Run tests; require all tests pass.

### Task 2: Durable native data and backups

Files: `src/TimeDeck.Core/{StateStore,Backup}.cs`, `tests/TimeDeck.Tests/StorageTests.cs`.

Interfaces: `StateStore.Load/Save` validate state and atomically replace JSON; `Backup.Parse` validates native backups and returns a preview. Restore only replaces state after preview/confirmation.

- [x] Write failing tests for roundtrip, recovery preserving corrupt primary, invalid settings/IDs/slots and CSV escaping.
- [x] Implement storage and import validation; run tests.
- [x] User scope update: start fresh; original HTML export changes reverted, migration removed.

### Task 3: Native shell, tray and deck

Files: `src/TimeDeck.Windows/{App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs}`, `Services/{NotificationService,StartupService}.cs`, `UI/{Theme,ActivityDialog,SettingsWindow}.cs`, app manifest and project file.

Consumes core APIs. App owns one state store/tracker, a one-second dispatcher timer and resume/time-change reconciliation. MainWindow is a view, hidden on close. Named pipe activates existing instance. Native toast service registers before activation and reports delivery errors.

- [x] Build fixed-grid WPF shell with accessible controls, active/today durations, drag swapping and keyboard destination picker, settings, native restore preview and backup.
- [x] Add tray Open/Stop/Exit, first-close explanation, single instance, optional startup and Windows notifications/test button.
- [x] Build with `dotnet build`; verify live window, hidden lifecycle and second-instance activation with Windows UI Automation where available.

### Task 4: Reports and release

Files: `src/TimeDeck.Windows/UI/{ReportWindow,EntryDialog}.cs`, `README.md`, `scripts/{publish,smoke}.ps1`, verification record.

Consumes `Reports.Day/Csv` and tracker entry validation. Native report shows daily summary/timeline, activity breakdown, week totals, editable entries and day/week CSV.

- [x] Implement report and validated manual editing/deletion with overlap confirmation; expose empty states and live markers.
- [x] Run full tests, release build and self-contained Windows publish.
- [x] Exercise published app through UI Automation, including notification test; record observed delivery separately from notification appearance/click checks.
- [x] Request fresh whole-change review, fix substantive findings, rerun affected verification and document remaining manual checks.

## Execution ledger

- Setup: approved design; no Git repository, so implementing in place without initialising version control. User requested implementation; proceeding inline under existing authorization.
- Preflight: Tasks 2–4 consume Task 1 AppState/Tracker/Reports; signatures fixed above, no conflicts.
- Task 1: complete. Observed missing-implementation test failures, then 14 passing tracker/report tests.
- Task 2: complete. Observed missing-store/import failures, then 21 passing tests. Ruling: user requested a clean start during implementation; removed browser export/import and its 3 tests; native backups/restore remain. Original HTML changes reverted. Remaining suite: 18 tests.
- Task 3: native build passed with zero warnings/errors. Live UI Automation verified all slots and persisted start. Ruling: Foundation-only Windows App SDK omitted its Insights resource DLL; add the official Runtime package so self-contained notifications register reliably. No system runtime installer is run.
- Task 3: complete. Official Runtime MSIX resource is extracted at build time to resolve the upstream self-contained registration bug. Published app registers and Windows accepts notifications. Fixed-grid UI, slot picker, settings, lifecycle and activation checked through Windows UI Automation.
- Task 4: complete. Reports and self-contained release delivered; 20 full-solution tests and 15 published-build smoke checks passed. Fresh reviewer found DST edit and report-label bugs plus extreme-date overflow; fixed these, added regression tests and bounded calendar navigation. Physical drag/drop, toast visual appearance/click, actual sleep/sign-in and tray menu actions remain explicit manual checks in docs/verification.md.
- Final ruling: no Git repository exists, so no commit/branch/PR or worktree cleanup applies. Source, release and verification evidence remain in the requested workspace.
