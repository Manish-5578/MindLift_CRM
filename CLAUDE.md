# MindLift CRM Rule Book

These rules are mandatory. Read them at the start of every session and check them before every commit and push.
The owner adds new rules by telling Claude; append them below with the next number and the date.

## 1. Never use native UI components (web and mobile)
- **Mobile (Flutter):** build screens only from `mindlift_ui` widgets (MlButton, MlTextField, MlSheet, MlDialog, MlToast, MlPressable, MlScreen, MlIcon and the rest). Do not use Material or Cupertino widgets (`ElevatedButton`, `TextField`, `AlertDialog`, `showModalBottomSheet`, `SnackBar`, `Switch`, `Checkbox`, `DropdownButton`, `Scaffold` and similar) or the platform's own pickers and dialogs directly in feature code. If a needed widget does not exist, add it to `mindlift_ui` first, then use it.
- **Web (Blazor):** use the shared components in `MindLift.UI` / the web app's own components. Do not use raw browser-native controls for user-facing UI: no bare `<select>`, `<input type="date|time|checkbox|file">` styling left at browser default, and no `alert()`, `confirm()` or `prompt()`. Build or reuse a custom component instead.
- Icons come from the Material Symbols set via `MlIcons` / `MlIcon` (mobile), not the default platform icons.
- If a native component is truly unavoidable (for example a system file or photo picker the OS requires), say so to the owner before using it and wrap it in a custom component.

## 2. Every push of the app also ships the latest APK to the phone
- Whenever app code is pushed, build the latest release APK from the code being pushed and put it on the phone in the same task.
- Install over the existing app with `adb install -r` so the user stays signed in (never uninstall, never clear data).
- Copy the APK to internal storage in both `/sdcard/CRM` and `/sdcard/MLC`.
- Name the APK `MindLift-CRM-MM-YY.apk`, ending with the month and year of the build (for example `MindLift-CRM-11-26.apk` for November 2026). A slash cannot be used in a file name, so a dash is used.
- Delete every older APK from both folders when copying the new one, so only the latest build stays on the phone.
- If no device is connected, still build the APK, say clearly that it was not installed, and install it when the device is next connected. Never claim it was installed when it was not.

## Standing working patterns
- Commit and push after each task.
- Log work in Notion: tasks in Mindlift Track, defects and security findings in Mindlift CRM Issues (see AGENTS.md).

## 3. Every feature is wired end to end and tested before it counts as done (added 2026-11-10)
- A feature is finished only when everything it touches is connected: UI, API endpoint, permissions, data model/migration, navigation and menus, Arabic strings, help text, and any mobile or related-page impact.
- Each feature gets automated tests (API, component and, where it fits, end-to-end) that pass before the commit.
- Check the related existing files for places the feature must plug in, and say what was wired in the summary.
- Applies to every feature, one at a time: build, wire, test, commit and push, log in Notion, then start the next.
