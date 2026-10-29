# Windows implementation verification

## Dual-scope installer acceptance checks

Local checks — 29 October 2026:

- `scripts/build-installer.ps1` successfully published the app and compiled the dual-scope installer with Inno Setup.
- Windows UI Automation verified that a fresh interactive setup offers **Install for all users** and **Install for me only**.
- Windows UI Automation verified that `/CURRENTUSER` skips the scope prompt and defaults to the current user's `%LOCALAPPDATA%\Programs\TimeDeck` directory.
- Both wizard checks stopped before installation; no installed copy or running app was changed.

The installer now uses Inno Setup's installation-mode choice and automatic path/shortcut constants. Application data, settings, notifications and sign-in startup remain per user; the application continues to request `asInvoker` execution. Run the following installation checks on a disposable Windows VM, not a workstation with an existing Time Deck install:

- As a standard user, start setup normally and select **Just me**. Verify there is no UAC prompt, the default path is `%LOCALAPPDATA%\Programs\TimeDeck`, shortcuts/uninstall registration are user-specific, and optional post-install launch runs as that user.
- As a standard user, select **All users**, supply a different administrator's credentials at UAC, and verify installation goes to `%ProgramFiles%\TimeDeck`, shortcuts/uninstall registration are machine-wide, and there is no post-install launch option.
- Sign in as two different standard users after the all-users install. Launch from each Start menu without elevation; verify independent `%LOCALAPPDATA%\TimeDeck\state.json` files, settings, notification registration and optional sign-in startup. Confirm neither user needs write permission to the installation directory.
- Exercise `/CURRENTUSER` as the intended standard user and `/ALLUSERS` from an elevated deployment account or SYSTEM, with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`. Check setup exit codes/logs, correct registration scope, and that no app process starts.
- Update an existing per-user install and an existing all-users install. Verify setup retains each scope/path, offers no unintended scope switch, and preserves tracking data.
- Change scope using the documented uninstall/reinstall procedure, including disabling sign-in startup first. Verify the same user's existing data is retained. A different administrator's data must not be copied automatically.
- Uninstall each scope. Verify app files and matching shortcuts/registration are removed while user data/backups remain. Per-user uninstall removes only a matching startup command; all-users uninstall must not change the deploying administrator's HKCU startup command.

These are acceptance checks, not a claim that the cross-account/UAC/SYSTEM matrix has been executed locally.

## Version 1.0.1 quality pass — 24 October 2026

- Reviewed native timer, storage, backup, report, UI and release code, including an independent read-only review.
- Reproduced and fixed regional-settings-dependent CSV dates/times and acceptance of trailing newlines in hex colours. The three added regression cases failed before the fixes; all **41 tests pass** afterward.
- Corrected deck/report minimum window bounds for smaller work areas. Builds and the existing desktop smoke checks pass; a physical small-screen/high-DPI matrix remains a manual check.
- `dotnet build src/TimeDeck.Windows -c Release --no-restore`: **0 warnings, 0 errors**.
- Published-app UI automation: **15 checks passed**, including report editing, persistence, notification acceptance, tray behaviour, second launch, timer recovery and exact cutoff.
- `scripts/build-installer.ps1` builds the self-contained Windows x64 app and `artifacts/installer/TimeDeck-Setup-1.0.1-win-x64.exe`. Builds run locally; this repository does not currently have a GitHub Actions build workflow.
- Installer signing and clean-machine/upgrade testing remain as described below. The original browser app is outside this native-app quality pass.

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
