# Time Deck: native Windows app

Date: 23 October 2026
Status: approved; implementation authorised by user

## Scope update during implementation

The user requested a fresh start: “dont worry about a browser export - we can start clean”. This supersedes all browser export, migration and overflow-selection requirements below. The HTML remains unchanged. Native backup/restore remains in scope.

## Intent and agreed requirements

Turn the existing single-file time tracker into a native Windows desktop app for one person. Preserve quick activity switching while making forgotten timers easier to catch. The user selected a native Windows rebuild over Electron and confirmed that closing the window must leave the app running in the system tray for reminders.

Provide a fixed 32-button deck inspired by Stream Deck XL, movable activity buttons, three fixed controls at the lower right (Report, Edit, Settings), configurable work hours defaulting to 06:00–18:00, automatic stopping at workday end, configurable Windows reminders defaulting to one hour, and improved reports.

## Approaches considered

- Electron: reuse the HTML interface, adding desktop lifecycle and notification services. Less interface rewriting, but a larger application footprint. Proposed initially; user chose native Windows instead.
- Native .NET: rebuild the interface using Windows desktop controls and own the timer lifecycle locally. More interface work, with direct Windows integration. Selected by the user.

Builder choice: WPF on .NET 10, targeting Windows 10/11 x64, using C# and XAML. The installed SDK supports this. Use a separate testable core for time calculations and persistence contracts. Use Windows App SDK app notifications for Windows toasts and Windows Forms NotifyIcon for the tray. No browser shell, account, server, cloud sync, or multi-user support.

## Deck and activity behaviour

Builder proposals:

- Exactly eight columns and four rows, scaling within a resizable desktop window without reflowing the grid. Set a usable minimum window size and support Windows display scaling.
- Slots 1–29 hold activities or empty placeholders. Slots 30–32 are Report, Edit, Settings and cannot move or be replaced.
- Preserve existing default activities, colour and emoji customisation, and categories as metadata. Category headings no longer split the deck.
- Clicking an activity starts it; clicking it again stops it; clicking a different activity stops the old one and starts the new one using the same timestamp. Only one timer runs.
- Show the active activity and elapsed time above the deck with a Stop control, and highlight its tile. Show today's accumulated activity time on each occupied tile.
- In Edit mode, dragging onto an occupied slot swaps buttons; dragging onto an empty slot moves the button. Also provide a destination-slot selector for keyboard users. Clicking an empty slot opens the activity editor. Cancel discards editor changes.
- Enforce the 29-activity capacity explicitly. Require unique nonblank activity names. Give activities stable IDs so renaming and moving do not break running timers or reporting links. Renaming updates the displayed name of linked history, matching current behaviour; deleting a button retains historical labels and entries.

## Desktop lifecycle

The application owns timer state independently of windows. Closing the main window hides it to the tray. The tray offers Open Time Deck, Stop tracking, and Exit; its tooltip identifies the running activity. Exit stops and saves the timer before terminating. One instance runs per Windows user; launching a second instance opens the existing window.

Builder proposal: an optional Start with Windows setting, disabled initially, launches to the tray. Show a short first-close explanation that the app remains running. All normal windows open within the application; no browser is needed.

Windows cannot run this process during shutdown or sleep. Persist the active timer and its cutoff immediately, reconcile on resume/startup, and never imply that reminders can appear while the computer is asleep or the app is explicitly exited.

## Work hours and cutoff

Builder proposals:

- Settings expose workday start and finish, default 06:00 and 18:00 local time, plus an automatic-stop toggle enabled initially. The daily rule applies every day; weekday-specific schedules and overnight shifts are outside this version.
- Require start earlier than finish. Starting before workday start or after finish offers an explicit overtime confirmation; do not silently discard intentional work. An overtime confirmation exempts that timer from that day's cutoff and is visibly indicated. It does not automatically start work the next morning.
- A normal timer ends exactly at that date's configured finish, even when a delayed tick, resume, or restart discovers the cutoff later. Save the cutoff with the running session; reconcile against it before showing elapsed time, reminders, or reports.
- Count elapsed time during sleep up to the cutoff. Automatic idle detection is outside scope. Use timestamp differences rather than counting UI ticks.
- When settings change during a timer, reconcile using the old cutoff first, then apply a future cutoff from the new settings. If the newly configured finish has already passed, stop at the settings-change time rather than retrospectively removing recorded work. Overtime exemption lasts until that timer stops.
- Use local calendar boundaries for scheduling and UTC instants for storage. Split entries across local midnight for reporting/export. Handle daylight-saving boundaries using timezone-aware conversions; the app follows the Windows local timezone. Re-evaluate future scheduling after system time/timezone changes without adding negative durations or duplicate entries.

## Reminders and notifications

Builder proposals:

- Reminder enable/disable and interval in minutes, default 60, valid range 1–1440. After the interval on an activity, show a Windows toast and an in-app check-in. Clicking the toast opens the app at that check-in.
- The check-in offers Still working, Change activity, and Stop. Still working restarts the reminder interval without resetting the activity timer. Change activity opens the deck without stopping until another activity is selected. If unanswered, repeat after 15 minutes, matching existing behaviour.
- Reset reminders when switching activities. Interval changes begin a fresh reminder period. On resume, send at most one overdue reminder after reconciling cutoff; do not replay missed notifications. Deduplicate notifications for a session.
- Offer a Test notification button and explain notification failures in settings. Keep the in-app check-in available when Windows notifications are blocked. Windows Focus Assist/Do Not Disturb may suppress presentation.
- Use Windows App SDK registration for an unpackaged WPF application and marshal activation to the UI thread. Verify toast delivery and click activation from the published build at its final location; build success alone is not notification verification.

## Reports

Retain day selection, timeline, activity totals, weekly comparison, manual entries, deletion and CSV export. Builder proposals: use a wider native report window with clear column headings, summary cards (tracked time, first/last activity, switches, longest block), a readable timeline, descending activity totals with percentages, weekly daily totals, and an entry table with start/end/duration/activity.

Add entry editing so forgotten switches can be corrected. Validate dates, positive durations and end-after-start; flag overlaps before accepting manual changes, with an explicit user override for intentional overlaps. Report totals represent summed tracked durations, including intentional overlaps. Live entries are clearly marked and excluded from exports until stopped. Export selected day or week to a UTF-8 CSV file, with correctly escaped fields and spreadsheet formula protection. Use exact durations internally and round only presentation. Empty dates have a clear empty state.

## Local data, migration and delivery

Builder proposals:

- Store versioned JSON in the current user's LocalAppData/TimeDeck folder, with atomic replacement and a last-known-good backup. Save every timer transition, settings edit, layout edit, and entry change. Surface write failures; do not pretend unsaved work is durable. Recover from a validated backup without silently overwriting corrupt data.
- Keep the original HTML working. Add explicit JSON backup export to it so its browser-local data can be imported into the native app; a desktop app cannot directly read another browser's localStorage. Import presets, entries and preferences with validation and a preview, backing up native data before replacement. Import historical entries and presets; report any active legacy timer and require it to be stopped in the old app before export/import, avoiding two concurrent running timers. No automatic browser-profile scanning.
- Native JSON backup/restore includes layout and settings. Restore validates data and confirms replacement. Existing presets beyond capacity must be reported before import, with a user choice of which 29 to place; preserve all historical entries.
- Deliver a runnable self-contained Windows x64 publish folder and documented build/publish commands. No Store distribution or signing is required for this version; code signing and an installer can be added later. A start-menu shortcut/registration helper may be included if required for reliable local notifications. Do not install or enable Windows startup silently.

## Verification and acceptance

Automated tests cover activity switching, cutoff while hidden/asleep/restarting, overtime, work-hour edits, reminder intervals and repeat/acknowledgement, midnight splitting, slot moves/swaps and fixed controls, import validation, and data recovery. Build the native app and self-contained release.

Manual Windows checks cover all 32 positions, drag/drop and keyboard repositioning, report editing/export, close-to-tray, second-instance activation, Exit, notification delivery and activation, and recovery after restart. Record which checks actually ran and any OS/UI checks that remain unverified. Preserve the original HTML and verify its added export works.

The result is accepted when the user can run the native app, organise up to 29 activities in a stable 32-button deck, track in the tray, receive configurable check-ins, have forgotten normal timers stop at workday end, and review/export/correct local reports without losing data across restarts.

## Decisions still open

No additional product decisions are required to plan implementation. All behaviour identified above as a builder proposal is presented for approval with this design. Visual polish and internal class structure remain builder decisions.
