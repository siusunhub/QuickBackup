# QuickBackup

QuickBackup is a small Windows Forms utility for monitoring folders and keeping a target folder synchronized in near real time. It is designed for simple folder-to-folder backup jobs where changed, created, renamed, and deleted files should be reflected in the destination automatically.

## Features

- Monitor one or more source folders with `FileSystemWatcher`.
- Copy file and folder changes to a selected destination folder.
- Mirror deletes from source to destination.
- Optional backup-before-change mode for each monitored row.
- Store overwritten or deleted destination files in a `.quickbackup` folder.
- Ignore `.quickbackup` folders during sync to prevent backup loops.
- De-duplicate rapid file events with a short delayed copy buffer.
- Save settings to `quickbackup.settings.json` beside the executable.
- Write logs to `quickbackup.log` beside the executable.
- Configurable log retention from 1 to 14 days.
- Configurable `.quickbackup` file retention from 1 to 14 days.
- Optional current-user autorun at Windows startup.
- Tray icon support with open and exit menu actions.
- Auto-start monitoring when valid settings already exist.
- Validate source and destination folders before monitoring starts.
- Show per-row status lights with hover tooltips.
- Lock row editing while monitoring is running.
- Keep the main window non-maximizable for a compact tray-style workflow.

## Example Use Case

One practical use case is backing up Claude Code skills and plan files. These folders can be updated or removed during AI-assisted work, so QuickBackup can keep a live mirror and optional backup-before-change copies to help recover files after accidental removal or overwrite.

## Backup Before Change

Each row has a `Backup` checkbox. When enabled, QuickBackup protects destination files before they are overwritten or deleted.

Backup files are stored in the destination folder under:

```text
.quickbackup
```

The backup filename format is:

```text
_yyyyMMdd_HHmmss_random6_originalfilename
```

When a directory is deleted from the source, all files inside the matching destination directory are moved into `.quickbackup`. Subfolders are not recreated inside `.quickbackup`; all backup files are stored flat in that folder.

## Row Status

Each setting row has a status light at the left:

- Green: monitoring is running for this row.
- Yellow: row is ready, stopped, or not configured.
- Red: row has an error, such as an empty path, missing folder, or matching source and destination paths.

Hover over the status light to see the current row message.

## Start Validation

QuickBackup will not start monitoring unless at least one valid path pair is configured. A valid row requires:

- `From path` is not empty.
- `To path` is not empty.
- `From path` and `To path` are not the same folder.
- Both folders already exist.

If autorun is enabled but validation fails at startup, QuickBackup shows the main window and does not start monitoring.

While monitoring is running, row editing is locked. The source path, destination path, browse buttons, backup checkbox, add button, and remove buttons are disabled until monitoring is stopped.

## Settings File

Settings are saved in:

```text
quickbackup.settings.json
```

Example:

```json
{
  "AutoRunAtStartup": false,
  "LogKeepDays": 7,
  "BackupKeepDays": 3,
  "Settings": [
    {
      "Id": "00000000-0000-0000-0000-000000000000",
      "FromPath": "C:\\SourceFolder",
      "ToPath": "D:\\BackupFolder",
      "BackupBeforeChange": true
    }
  ]
}
```

## Logging

Logs are written to:

```text
quickbackup.log
```

The log retention setting controls how many days of timestamped log lines are kept. Old lines are removed based on the timestamp at the start of each log line.

## Autorun

When `autorun at startup` is checked, QuickBackup adds a current-user startup entry:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\QuickBackup
```

Unchecking the option removes that registry value.

## Notes

- QuickBackup is intended for local folder backup/sync workflows.
- It is not a version control system.
- It does not preserve deleted directory structure inside `.quickbackup`.
- Large log files are purged by timestamp according to the configured retention days.
