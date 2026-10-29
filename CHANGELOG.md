# Changelog

## Unreleased

- Offer per-user (no elevation) and all-users (administrator-approved) Windows installation, with scope-appropriate app locations, shortcuts and uninstall registration.
- Support explicit `/CURRENTUSER` and `/ALLUSERS` unattended deployment while retaining private per-user data and settings.
- Avoid launching the app under the deploying account after machine-wide installation or modifying that account's sign-in entry during machine-wide uninstall.

## 1.0.1 — 24 October 2026

- Keep CSV dates and times consistent across Windows regional settings, including non-Gregorian calendars and alternate time separators.
- Reject activity colours with trailing newlines during editing and backup validation.
- Keep the deck and report minimum window sizes within the available screen area on small or highly scaled displays.

## 1.0.0

- Initial native Windows time tracker, reports, local backups, notifications and installer.
