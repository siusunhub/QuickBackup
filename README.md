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

## Build

Requirements:

- Windows
- .NET SDK with Windows Forms support

Build:

```powershell
dotnet build
```

If the application is already running and locking the generated executable during development, this build command can still verify the code:

```powershell
dotnet build /p:UseAppHost=false
```

## Notes

- QuickBackup is intended for local folder backup/sync workflows.
- It is not a version control system.
- It does not preserve deleted directory structure inside `.quickbackup`.
- Large log files are purged by timestamp according to the configured retention days.
