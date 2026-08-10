using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Serialization;

namespace quickbackup
{
    public sealed class AppConfig
    {
        [JsonPropertyName("AutoRunAtStartup")]
        public bool AutoRunAtStartup { get; set; }

        [JsonPropertyName("LogKeepDays")]
        public int LogKeepDays { get; set; } = 7;

        [JsonPropertyName("BackupKeepDays")]
        public int BackupKeepDays { get; set; } = 3;

        [JsonPropertyName("Settings")]
        public List<CopySetting> Settings { get; set; } = [];
    }

    public sealed class CopySetting
    {
        [JsonPropertyName("Id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("FromPath")]
        public string FromPath { get; set; } = "";

        [JsonPropertyName("ToPath")]
        public string ToPath { get; set; } = "";

        [JsonPropertyName("BackupBeforeChange")]
        public bool BackupBeforeChange { get; set; }

        public CopySetting Clone()
        {
            return new CopySetting
            {
                Id = Id,
                FromPath = FromPath,
                ToPath = ToPath,
                BackupBeforeChange = BackupBeforeChange
            };
        }
    }

    public sealed class MonitorWorker : IDisposable
    {
        public const string BackupFolderName = ".quickbackup";

        private readonly CopySetting setting;
        private readonly Action<string> log;
        private readonly ConcurrentDictionary<string, PendingFileEvent> pendingEvents = new(StringComparer.OrdinalIgnoreCase);
        private readonly AutoResetEvent pendingSignal = new(false);
        private readonly Thread thread;
        private FileSystemWatcher? watcher;
        private volatile bool disposed;

        public MonitorWorker(CopySetting setting, Action<string> log)
        {
            this.setting = setting.Clone();
            this.log = log;
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"QuickBackup-{setting.Id:N}"
            };
        }

        public void Start()
        {
            thread.Start();
        }

        public bool Matches(CopySetting other)
        {
            return setting.Id == other.Id
                && string.Equals(setting.FromPath, other.FromPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(setting.ToPath, other.ToPath, StringComparison.OrdinalIgnoreCase)
                && setting.BackupBeforeChange == other.BackupBeforeChange;
        }

        private void Run()
        {
            try
            {
                Directory.CreateDirectory(setting.ToPath);
                SyncAll();
                StartWatcher();

                while (!disposed)
                {
                    pendingSignal.WaitOne(1000);
                    ProcessDueEvents();
                }
            }
            catch (Exception ex)
            {
                log($"Monitor failed: {setting.FromPath} -> {setting.ToPath}; {ex.Message}");
            }
        }

        private void StartWatcher()
        {
            watcher = new FileSystemWatcher(setting.FromPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Created += (_, e) => Enqueue(e.FullPath, e.ChangeType);
            watcher.Changed += (_, e) => Enqueue(e.FullPath, e.ChangeType);
            watcher.Deleted += (_, e) => Enqueue(e.FullPath, e.ChangeType);
            watcher.Renamed += (_, e) =>
            {
                Enqueue(e.OldFullPath, WatcherChangeTypes.Deleted);
                Enqueue(e.FullPath, WatcherChangeTypes.Created);
            };
            watcher.Error += (_, e) =>
            {
                log($"Watcher error for {setting.FromPath}: {e.GetException().Message}");
                Dispose();
            };
        }

        private void Enqueue(string path, WatcherChangeTypes changeType)
        {
            if (disposed || IsQuickBackupPath(path))
            {
                return;
            }

            string key = NormalizePathKey(path);
            DateTime dueAt = DateTime.UtcNow.AddSeconds(1);
            pendingEvents.AddOrUpdate(
                key,
                _ => new PendingFileEvent(path, changeType, dueAt),
                (_, existing) =>
                {
                    existing.Path = path;
                    existing.ChangeType = changeType;
                    existing.DueAtUtc = dueAt;
                    return existing;
                });
            pendingSignal.Set();
        }

        private void ProcessDueEvents()
        {
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, PendingFileEvent> pending in pendingEvents.ToArray())
            {
                if (pending.Value.DueAtUtc > now)
                {
                    continue;
                }

                if (pendingEvents.TryRemove(pending.Key, out PendingFileEvent? fileEvent))
                {
                    ProcessEvent(new FileEvent(fileEvent.Path, fileEvent.ChangeType));
                }
            }
        }

        private void ProcessEvent(FileEvent fileEvent)
        {
            try
            {
                string relativePath = Path.GetRelativePath(setting.FromPath, fileEvent.Path);
                if (IsIgnoredRelativePath(relativePath))
                {
                    return;
                }

                string destinationPath = Path.Combine(setting.ToPath, relativePath);

                if (fileEvent.ChangeType == WatcherChangeTypes.Deleted)
                {
                    DeleteDestination(destinationPath);
                    return;
                }

                if (Directory.Exists(fileEvent.Path))
                {
                    CopyDirectoryTree(fileEvent.Path, destinationPath);
                    return;
                }

                if (File.Exists(fileEvent.Path))
                {
                    CopyFileWithRetry(fileEvent.Path, destinationPath);
                }
            }
            catch (Exception ex)
            {
                log($"Could not process change {fileEvent.Path}: {ex.Message}");
            }
        }

        private void SyncAll()
        {
            if (!Directory.Exists(setting.FromPath))
            {
                log($"Source folder does not exist: {setting.FromPath}");
                return;
            }

            Directory.CreateDirectory(setting.ToPath);
            foreach (string directory in EnumerateDirectoriesExcludingBackup(setting.FromPath))
            {
                string relativePath = Path.GetRelativePath(setting.FromPath, directory);
                Directory.CreateDirectory(Path.Combine(setting.ToPath, relativePath));
            }

            foreach (string file in EnumerateFilesExcludingBackup(setting.FromPath))
            {
                string relativePath = Path.GetRelativePath(setting.FromPath, file);
                string destinationPath = Path.Combine(setting.ToPath, relativePath);
                if (NeedsCopy(file, destinationPath))
                {
                    CopyFileWithRetry(file, destinationPath);
                }
            }

            foreach (string destinationFile in Directory.EnumerateFiles(setting.ToPath, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                if (IsQuickBackupPath(destinationFile))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(setting.ToPath, destinationFile);
                string sourcePath = Path.Combine(setting.FromPath, relativePath);
                if (!File.Exists(sourcePath))
                {
                    DeleteDestinationFile(destinationFile);
                    log($"Deleted stale file: {destinationFile}");
                }
            }

            foreach (string destinationDirectory in Directory.EnumerateDirectories(setting.ToPath, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                if (IsQuickBackupPath(destinationDirectory))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(setting.ToPath, destinationDirectory);
                string sourcePath = Path.Combine(setting.FromPath, relativePath);
                if (!Directory.Exists(sourcePath))
                {
                    DeleteDestinationDirectory(destinationDirectory);
                    log($"Deleted stale directory: {destinationDirectory}");
                }
            }

            log($"Initial sync completed: {setting.FromPath} -> {setting.ToPath}");
        }

        private static bool NeedsCopy(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return true;
            }

            FileInfo source = new(sourcePath);
            FileInfo destination = new(destinationPath);
            return source.Length != destination.Length
                || Math.Abs((source.LastWriteTimeUtc - destination.LastWriteTimeUtc).TotalSeconds) > 2;
        }

        private void CopyFileWithRetry(string sourcePath, string destinationPath)
        {
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    BackupBeforeOverwrite(destinationPath);
                    File.Copy(sourcePath, destinationPath, true);
                    File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
                    log($"Copied: {sourcePath} -> {destinationPath}");
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(300);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    Thread.Sleep(300);
                }
            }
        }

        private void CopyDirectoryTree(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string directory in EnumerateDirectoriesExcludingBackup(sourceDirectory))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
            }

            foreach (string file in EnumerateFilesExcludingBackup(sourceDirectory))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, file);
                string destinationPath = Path.Combine(destinationDirectory, relativePath);
                if (NeedsCopy(file, destinationPath))
                {
                    CopyFileWithRetry(file, destinationPath);
                }
            }
        }

        private void DeleteDestination(string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                DeleteDestinationFile(destinationPath);
                log($"Deleted file: {destinationPath}");
            }
            else if (Directory.Exists(destinationPath))
            {
                DeleteDestinationDirectory(destinationPath);
                log($"Deleted directory: {destinationPath}");
            }
        }

        private void BackupBeforeOverwrite(string destinationPath)
        {
            if (!setting.BackupBeforeChange || !File.Exists(destinationPath) || IsQuickBackupPath(destinationPath))
            {
                return;
            }

            string backupPath = CreateBackupPath(destinationPath);
            File.Copy(destinationPath, backupPath, false);
            File.SetLastWriteTimeUtc(backupPath, File.GetLastWriteTimeUtc(destinationPath));
            log($"Backed up before overwrite: {destinationPath} -> {backupPath}");
        }

        private void DeleteDestinationFile(string destinationFile)
        {
            if (!setting.BackupBeforeChange || IsQuickBackupPath(destinationFile))
            {
                File.Delete(destinationFile);
                return;
            }

            string backupPath = CreateBackupPath(destinationFile);
            File.Move(destinationFile, backupPath);
            log($"Moved deleted file to backup: {destinationFile} -> {backupPath}");
        }

        private void DeleteDestinationDirectory(string destinationDirectory)
        {
            if (!setting.BackupBeforeChange || IsQuickBackupPath(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, true);
                return;
            }

            foreach (string file in Directory.EnumerateFiles(destinationDirectory, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                if (IsQuickBackupPath(file))
                {
                    continue;
                }

                string backupPath = CreateBackupPath(file);
                File.Move(file, backupPath);
                log($"Moved deleted directory file to backup: {file} -> {backupPath}");
            }

            Directory.Delete(destinationDirectory, true);
        }

        private string CreateBackupPath(string originalPath)
        {
            string backupFolder = Path.Combine(setting.ToPath, BackupFolderName);
            Directory.CreateDirectory(backupFolder);
            string originalFileName = Path.GetFileName(originalPath);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                string fileName = $"_{DateTime.Now:yyyyMMdd_HHmmss}_{Random.Shared.Next(0, 1000000):D6}_{originalFileName}";
                string backupPath = Path.Combine(backupFolder, fileName);
                if (!File.Exists(backupPath))
                {
                    return backupPath;
                }
            }

            return Path.Combine(backupFolder, $"_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{originalFileName}");
        }

        private static IEnumerable<string> EnumerateDirectoriesExcludingBackup(string root)
        {
            Stack<string> pending = new();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    if (string.Equals(Path.GetFileName(directory), BackupFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    yield return directory;
                    pending.Push(directory);
                }
            }
        }

        private static IEnumerable<string> EnumerateFilesExcludingBackup(string root)
        {
            Stack<string> pending = new();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string file in Directory.EnumerateFiles(current))
                {
                    yield return file;
                }

                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    if (!string.Equals(Path.GetFileName(directory), BackupFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        pending.Push(directory);
                    }
                }
            }
        }

        private static bool IsIgnoredRelativePath(string relativePath)
        {
            return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => string.Equals(part, BackupFolderName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsQuickBackupPath(string path)
        {
            return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => string.Equals(part, BackupFolderName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePathKey(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            watcher?.Dispose();
            pendingSignal.Set();
        }
    }

    public sealed record FileEvent(string Path, WatcherChangeTypes ChangeType);

    public sealed class PendingFileEvent
    {
        public PendingFileEvent(string path, WatcherChangeTypes changeType, DateTime dueAtUtc)
        {
            Path = path;
            ChangeType = changeType;
            DueAtUtc = dueAtUtc;
        }

        public string Path { get; set; }
        public WatcherChangeTypes ChangeType { get; set; }
        public DateTime DueAtUtc { get; set; }
    }
}
