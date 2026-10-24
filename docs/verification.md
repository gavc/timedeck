# Windows implementation verification

23 October 2026. Windows x64, .NET SDK 10.0.204.

## Automated core checks

`dotnet test tests/TimeDeck.Tests --no-restore`: **20 passed, 0 failed**.

Coverage includes exact activity-switch timestamps, delayed/restarted cutoff, overtime confirmation, 60-minute reminders and 15-minute repeats, acknowledgement, settings changes, slot moves/swaps and fixed-slot protection, rename/delete history, clock rollback, manual overlap confirmation, midnight and DST slicing, DST-safe entry editing, canonical activity labels, safe CSV export, durable state roundtrip, backup recovery and malformed data rejection.

## Native build and publish

`dotnet build src/TimeDeck.Windows`: succeeded with **0 warnings, 0 errors**.

`scripts/publish.ps1`: produced the self-contained Windows x64 application in `artifacts/TimeDeck`. This includes the .NET runtime and Windows App SDK components; no machine-wide runtime installation was performed.

Live testing exposed the Windows App SDK Insights resource packaging issue described in [Microsoft's issue tracker](https://github.com/microsoft/WindowsAppSDK/issues/6774). The project now obtains the missing DLL from the pinned official Runtime NuGet package's x64 MSIX at build time and copies it to build/publish output. Notification registration and Windows acceptance were then verified successfully.

## Live Windows checks

`powershell -NoProfile -STA -ExecutionPolicy Bypass -File scripts/smoke.ps1` drives the published app through Windows UI Automation using a separate data directory. It intentionally terminates only its own test process between restart scenarios; it never uses or resets the user's normal data directory. Screenshots use PrintWindow on the app's own window.

Verified:

- Exactly 32 positions; Report, Edit and Settings remain in slots 30, 31 and 32.
- Starting/stopping saves completed entries.
- Report opens, renders and accepts manual entries.
- Renaming an activity updates linked history.
- The destination picker moves an activity into an empty fixed slot while preserving history.
- Windows accepts a native notification from the published application.
- Closing the deck leaves the application and active timer running.
- A second launch exits after reopening the existing instance.
- Restart recovers a timer and presents one overdue check-in.
- Still working starts a fresh reminder period without stopping tracking.
- With the window hidden, the timer stops at its exact persisted cutoff.

The smoke script writes temporary captures to `artifacts/deck-preview.png` and `artifacts/report-preview.png` (ignored by Git). The README uses separate sample-data screenshots: [activity deck](images/activity-deck.png) and [daily report](images/daily-report.png).

## Review

A separate read-only reviewer checked the implementation. Fixed the substantive findings: preserve original instants when editing unchanged wall times during a DST fold, require an occurrence choice for changed ambiguous times, canonicalise linked activity labels, and bound report dates/navigation to avoid calendar overflow. Regression tests cover the timing and label fixes.

## Remaining manual checks

- Actual Windows banner appearance and clicking a banner, including Do Not Disturb/system notification settings; the automated check proves registration and Windows acceptance, not visual delivery.
- Physical drag/drop, actual sleep/resume and shutdown, and enabling launch at Windows sign-in. Timer recovery/cutoff is covered by automated explicit-time and process-restart checks.
- Tray menu Exit/Stop, file-dialog CSV export/backup restore, and varied monitor/DPI combinations. Core CSV, restore validation and slot swaps are covered by tests; the app window was visually inspected at the host's display scaling.

The original HTML remains functionally unchanged. The Windows app starts fresh, as requested; browser migration was removed from scope.

Final published-build smoke run: **15 checks passed**. Full solution verification, `dotnet test TimeDeck.slnx --no-restore`: **20 tests passed**. The original HTML file remains 33,188 bytes.

## Installer verification — 24 October 2026

Built `installer/TimeDeck.iss` using the installed Inno Setup 6.6.1 compiler after publishing the self-contained app. Output: `artifacts/installer/TimeDeck-Setup-1.0.0-win-x64.exe` (59.1 MB).

Installed silently into a unique test directory under `artifacts/`, verified the registered app name/version and hashes of the installed executable, app assemblies, .NET runtime and notification resource, then uninstalled. The app files and uninstall registration were removed, and the normal tracking data was unchanged. The installer's embedded icon matches the app's extracted icon pixel for pixel. The first metadata assertion expected an unversioned display name; it was corrected to accept Inno Setup's default `Time Deck version 1.0.0`, and the checks were rerun successfully.

The installer is unsigned. The interactive wizard, upgrades over an older installed release, and installation on a separate clean Windows machine have not yet been tested.
