using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace quickbackup
{
    public partial class Form1 : Form
    {
        private const string AppVersion = "0.24";
        private const int DefaultLogKeepDays = 7;
        private const int DefaultBackupKeepDays = 3;
        private const int MinKeepDays = 1;
        private const int MaxKeepDays = 14;
        private const string StartupRegistryValueName = "QuickBackup";
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static string AppTitle => $"QuickBackup {AppVersion}";
        private readonly string configPath = Path.Combine(AppContext.BaseDirectory, "quickbackup.settings.json");
        private readonly string logPath = Path.Combine(AppContext.BaseDirectory, "quickbackup.log");
        private readonly List<CopySetting> settings = [];
        private readonly Dictionary<Guid, MonitorWorker> workers = [];
        private readonly Dictionary<Guid, Control> settingRows = [];
        private readonly Dictionary<Guid, Panel> statusLights = [];
        private readonly ToolTip statusToolTip = new();
        private readonly object logLock = new();
        private readonly System.Windows.Forms.Timer refreshTimer = new();
        private readonly System.Windows.Forms.Timer memoryTimer = new();
        private readonly System.Windows.Forms.Timer backupCleanupTimer = new();
        private DateTime lastBackupCleanupDate = DateTime.MinValue;
        private DateTime lastLogPurgeDate = DateTime.MinValue;
        private bool isRunning;
        private bool allowExit;
        private bool autoStartOnLoad;
        private bool initialAutoStartHandled;
        private bool loadingSettings;
        private bool updatingAutorunCheckbox;
        private bool storageWarningShown;
        private bool logWriteWarningShown;
        private int memoryCollectionRunning;
        private string lastActionMessage = "Ready";

        public Form1()
        {
            InitializeComponent();
            Text = AppTitle;
            trayIcon.Text = AppTitle;
            statusToolTip.AutoPopDelay = 8000;
            statusToolTip.InitialDelay = 300;
            statusToolTip.ReshowDelay = 100;

            btnAdd.Click += (_, _) => AddNewRow();
            btnStart.Click += (_, _) => StartMonitoring();
            btnStop.Click += (_, _) => StopMonitoring();
            btnLog.Click += (_, _) => ShowLogWindow();
            numLogKeepDays.ValueChanged += (_, _) => SaveRetentionSettings();
            numBackupKeepDays.ValueChanged += (_, _) => SaveRetentionSettings();
            chkAutorun.CheckedChanged += (_, _) =>
            {
                if (!loadingSettings && !updatingAutorunCheckbox)
                {
                    ApplyAutorunCheckboxChange();
                }
            };
            rowsPanel.SizeChanged += (_, _) => ResizeRows();
            EnableDoubleBuffering(rowsPanel);
            trayIcon.MouseClick += TrayIcon_MouseClick;
            menuOpen.Click += (_, _) => ShowMainWindow();
            menuExit.Click += (_, _) => ExitApplication();
            FormClosing += Form1_FormClosing;

            refreshTimer.Interval = 30000;
            refreshTimer.Tick += (_, _) => ReconcileWorkers();
            memoryTimer.Interval = 60 * 60 * 1000;
            memoryTimer.Tick += (_, _) => StartMemoryCollection();
            memoryTimer.Start();
            backupCleanupTimer.Interval = 60 * 1000;
            backupCleanupTimer.Tick += (_, _) => RunBackupCleanupIfNeeded();
            backupCleanupTimer.Start();

            LoadSettings();
            CheckStorageWritable();
            PurgeOldLogLines(true);
            CleanupBackupFolders();
            RenderRows();

            autoStartOnLoad = settings.Any(IsValidSetting);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (value && autoStartOnLoad && !initialAutoStartHandled)
            {
                initialAutoStartHandled = true;
                if (TryValidateStart(out string message))
                {
                    StartMonitoring();
                    base.SetVisibleCore(false);
                }
                else
                {
                    SetLastAction(message);
                    base.SetVisibleCore(true);
                }
                return;
            }

            base.SetVisibleCore(value);
        }

        private void AddNewRow()
        {
            CopySetting setting = new();
            settings.Add(setting);
            SaveSettings();
            AddSettingRow(setting);
            ResizeRows();
            UpdateStatus();
            ReconcileWorkers();
        }

        private void RemoveRow(CopySetting setting)
        {
            if (MessageBox.Show("Remove this copy setting?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            settings.Remove(setting);
            SaveSettings();
            statusLights.Remove(setting.Id);
            if (settingRows.TryGetValue(setting.Id, out Control? row))
            {
                settingRows.Remove(setting.Id);
                rowsPanel.SuspendLayout();
                rowsPanel.Controls.Remove(row);
                row.Dispose();
                rowsPanel.ResumeLayout();
            }

            UpdateStatus();
            ReconcileWorkers();
        }

        private void RenderRows()
        {
            rowsPanel.SuspendLayout();
            rowsPanel.Controls.Clear();
            settingRows.Clear();
            statusLights.Clear();

            AddHeaderRow();
            foreach (CopySetting setting in settings)
            {
                AddSettingRow(setting);
            }

            rowsPanel.ResumeLayout();
            ResizeRows();
            UpdateStatus();
        }

        private static void EnableDoubleBuffering(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }

        private void ResizeRows()
        {
            int width = Math.Max(720, rowsPanel.ClientSize.Width - rowsPanel.Padding.Left - rowsPanel.Padding.Right - SystemInformation.VerticalScrollBarWidth);
            foreach (Control control in rowsPanel.Controls)
            {
                control.Width = width;
            }
        }

        private void AddHeaderRow()
        {
            TableLayoutPanel header = CreateRowLayout(30);
            header.BackColor = SystemColors.ControlLight;
            header.Controls.Add(CreateHeaderLabel(""), 0, 0);
            header.Controls.Add(CreateHeaderLabel("From path"), 1, 0);
            header.Controls.Add(CreateHeaderLabel("To path"), 2, 0);
            header.Controls.Add(CreateHeaderLabel("Backup"), 3, 0);
            header.Controls.Add(CreateHeaderLabel(""), 4, 0);
            rowsPanel.Controls.Add(header);
        }

        private Label CreateHeaderLabel(string text)
        {
            return new Label()
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(8, 0, 0, 0)
            };
        }

        private void AddSettingRow(CopySetting setting)
        {
            rowsPanel.SuspendLayout();
            TableLayoutPanel row = CreateRowLayout(38);

            row.Controls.Add(CreateStatusLight(setting), 0, 0);
            row.Controls.Add(CreatePathPicker(setting, true), 1, 0);
            row.Controls.Add(CreatePathPicker(setting, false), 2, 0);
            row.Controls.Add(CreateBackupCheckbox(setting), 3, 0);

            Button removeButton = new()
            {
                Text = "-",
                Anchor = AnchorStyles.None,
                Size = new Size(34, 28),
                Margin = new Padding(0)
            };
            removeButton.Click += (_, _) => RemoveRow(setting);
            statusToolTip.SetToolTip(removeButton, "Remove Backup Path");
            row.Controls.Add(removeButton, 4, 0);
            settingRows[setting.Id] = row;
            rowsPanel.Controls.Add(row);
            SetRowEditEnabled(row, !isRunning);
            UpdateSettingStatus(setting);
            rowsPanel.ResumeLayout();
        }

        private void SetRowsEditEnabled(bool enabled)
        {
            btnAdd.Enabled = enabled;
            foreach (Control row in settingRows.Values)
            {
                SetRowEditEnabled(row, enabled);
            }
        }

        private static void SetRowEditEnabled(Control control, bool enabled)
        {
            foreach (Control child in control.Controls)
            {
                if (child is TextBox textBox)
                {
                    textBox.ReadOnly = !enabled;
                    textBox.TabStop = enabled;
                }
                else if (child is Button button)
                {
                    button.Enabled = button.Name == "openPathButton" || enabled;
                }
                else if (child is CheckBox)
                {
                    child.Enabled = enabled;
                }

                if (child.HasChildren)
                {
                    SetRowEditEnabled(child, enabled);
                }
            }
        }

        private TableLayoutPanel CreateRowLayout(int height)
        {
            TableLayoutPanel row = new()
            {
                ColumnCount = 5,
                RowCount = 1,
                Height = height,
                Width = Math.Max(720, rowsPanel.ClientSize.Width - rowsPanel.Padding.Left - rowsPanel.Padding.Right - SystemInformation.VerticalScrollBarWidth),
                Margin = new Padding(0, 0, 0, 4),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            return row;
        }

        private Control CreateStatusLight(CopySetting setting)
        {
            Panel light = new()
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Tag = Color.Goldenrod
            };
            light.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color color = light.Tag is Color statusColor ? statusColor : Color.Goldenrod;
                int size = 18;
                int x = Math.Max(0, (light.ClientSize.Width - size) / 2);
                int y = 4;
                using SolidBrush brush = new(color);
                e.Graphics.FillEllipse(brush, x, y, size, size);
            };
            statusLights[setting.Id] = light;
            return light;
        }

        private void UpdateAllSettingStatuses()
        {
            foreach (CopySetting setting in settings)
            {
                UpdateSettingStatus(setting);
            }
        }

        private void UpdateSettingStatus(CopySetting setting)
        {
            if (!statusLights.TryGetValue(setting.Id, out Panel? light))
            {
                return;
            }

            string message = "";
            if (!TryValidateConfiguredSetting(setting, out message))
            {
                light.Tag = Color.Firebrick;
                light.Invalidate();
                statusToolTip.SetToolTip(light, message);
                return;
            }

            if (isRunning && workers.ContainsKey(setting.Id))
            {
                light.Tag = Color.ForestGreen;
                light.Invalidate();
                statusToolTip.SetToolTip(light, "Running");
            }
            else
            {
                message = string.IsNullOrWhiteSpace(setting.FromPath) && string.IsNullOrWhiteSpace(setting.ToPath)
                    ? "Not configured"
                    : "Ready";
                light.Tag = Color.Goldenrod;
                light.Invalidate();
                statusToolTip.SetToolTip(light, message);
            }
        }

        private Control CreateBackupCheckbox(CopySetting setting)
        {
            CheckBox checkBox = new()
            {
                Checked = setting.BackupBeforeChange,
                CheckAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Text = ""
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                if (isRunning)
                {
                    checkBox.Checked = setting.BackupBeforeChange;
                    return;
                }

                setting.BackupBeforeChange = checkBox.Checked;
                SaveSettings();
                ReconcileWorkers();
            };
            return checkBox;
        }

        private Control CreatePathPicker(CopySetting setting, bool isSource)
        {
            Panel panel = new()
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(4, 3, 8, 3)
            };

            TextBox textBox = new()
            {
                Dock = DockStyle.Fill,
                Text = isSource ? setting.FromPath : setting.ToPath,
                Margin = new Padding(0)
            };
            Button browseButton = new()
            {
                Text = "...",
                Dock = DockStyle.Right,
                Width = 34,
                Margin = new Padding(0)
            };
            Button openButton = new()
            {
                Text = "O",
                Name = "openPathButton",
                Dock = DockStyle.Right,
                Width = 34,
                Margin = new Padding(0)
            };
            statusToolTip.SetToolTip(browseButton, "Select Folder");
            statusToolTip.SetToolTip(openButton, "Open in Explorer");

            textBox.Leave += (_, _) =>
            {
                if (!isRunning)
                {
                    SetPathValue(setting, isSource, textBox.Text);
                }
            };
            textBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SetPathValue(setting, isSource, textBox.Text);
                    e.SuppressKeyPress = true;
                }
            };
            browseButton.Click += (_, _) =>
            {
                if (isRunning)
                {
                    return;
                }

                using FolderBrowserDialog dialog = new()
                {
                    SelectedPath = Directory.Exists(textBox.Text) ? textBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    textBox.Text = dialog.SelectedPath;
                    SetPathValue(setting, isSource, dialog.SelectedPath);
                }
            };
            openButton.Click += (_, _) =>
            {
                string path = textBox.Text.Trim();
                if (EnsureDirectoryExists(path, out string message))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else if (!string.IsNullOrEmpty(message))
                {
                    MessageBox.Show(message, "QuickBackup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            panel.Controls.Add(textBox);
            panel.Controls.Add(browseButton);
            panel.Controls.Add(openButton);
            return panel;
        }

        private void SetPathValue(CopySetting setting, bool isSource, string path)
        {
            if (isRunning)
            {
                return;
            }

            string normalized = path.Trim();
            if (isSource)
            {
                setting.FromPath = normalized;
            }
            else
            {
                setting.ToPath = normalized;
            }

            SaveSettings();
            ReconcileWorkers();
            UpdateSettingStatus(setting);
            UpdateStatus();
        }

        private void StartMonitoring()
        {
            if (!TryValidateStart(out string message, createMissingDirectories: true))
            {
                UpdateAllSettingStatuses();
                SetLastAction(message);
                MessageBox.Show(message, "QuickBackup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CleanupBackupFolders();
            isRunning = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            SetRowsEditEnabled(false);
            refreshTimer.Start();
            ReconcileWorkers();
            Log("Monitoring started.");
            UpdateStatus();
        }

        private void StopMonitoring()
        {
            refreshTimer.Stop();
            foreach (MonitorWorker worker in workers.Values.ToList())
            {
                worker.Dispose();
            }

            workers.Clear();
            isRunning = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            SetRowsEditEnabled(true);
            Log("Monitoring stopped.");
            UpdateAllSettingStatuses();
            UpdateStatus();
        }

        private void ReconcileWorkers()
        {
            if (!isRunning)
            {
                UpdateAllSettingStatuses();
                return;
            }

            HashSet<Guid> desiredIds = settings.Where(IsValidSetting).Select(setting => setting.Id).ToHashSet();
            foreach (Guid id in workers.Keys.Where(id => !desiredIds.Contains(id)).ToList())
            {
                workers[id].Dispose();
                workers.Remove(id);
                Log("Stopped monitor for removed or invalid setting.");
            }

            foreach (CopySetting setting in settings.Where(IsValidSetting))
            {
                if (!TryValidateConfiguredSetting(setting, out string validationMessage))
                {
                    if (workers.TryGetValue(setting.Id, out MonitorWorker? missingWorker))
                    {
                        workers.Remove(setting.Id);
                        missingWorker.Dispose();
                        Log($"Stopped monitor because path is invalid: {validationMessage}");
                    }
                    UpdateSettingStatus(setting);
                    continue;
                }

                if (workers.TryGetValue(setting.Id, out MonitorWorker? existingWorker))
                {
                    if (!existingWorker.Matches(setting))
                    {
                        existingWorker.Dispose();
                        workers.Remove(setting.Id);
                    }
                    else
                    {
                        continue;
                    }
                }

                MonitorWorker worker = new(setting, Log);
                workers[setting.Id] = worker;
                worker.Start();
                Log($"Started monitor: {setting.FromPath} -> {setting.ToPath}");
            }

            UpdateAllSettingStatuses();
            UpdateStatus();
        }

        private bool IsValidSetting(CopySetting setting)
        {
            return !string.IsNullOrWhiteSpace(setting.FromPath)
                && !string.IsNullOrWhiteSpace(setting.ToPath)
                && !PathsOverlap(setting.FromPath, setting.ToPath);
        }

        private bool TryValidateStart(out string message, bool createMissingDirectories = false)
        {
            message = "";
            List<CopySetting> configuredSettings = settings
                .Where(setting => !string.IsNullOrWhiteSpace(setting.FromPath) || !string.IsNullOrWhiteSpace(setting.ToPath))
                .ToList();
            if (configuredSettings.Count == 0)
            {
                message = "No valid path is config";
                return false;
            }

            foreach (CopySetting setting in configuredSettings)
            {
                if (!TryValidateConfiguredSetting(setting, out message, requireDirectories: !createMissingDirectories))
                {
                    return false;
                }
            }

            if (createMissingDirectories)
            {
                foreach (string path in configuredSettings
                    .SelectMany(setting => new[] { setting.FromPath, setting.ToPath })
                    .Select(path => path.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!EnsureDirectoryExists(path, out message))
                    {
                        return false;
                    }
                }
            }

            if (!configuredSettings.Any(IsValidSetting))
            {
                message = "No valid path is config";
                return false;
            }

            return true;
        }

        private bool TryValidateConfiguredSetting(CopySetting setting, out string message, bool requireDirectories = true)
        {
            message = "";
            bool hasFromPath = !string.IsNullOrWhiteSpace(setting.FromPath);
            bool hasToPath = !string.IsNullOrWhiteSpace(setting.ToPath);
            if (!hasFromPath && !hasToPath)
            {
                return true;
            }

            if (!hasFromPath)
            {
                message = "From path is empty.";
                return false;
            }

            if (!hasToPath)
            {
                message = "To path is empty.";
                return false;
            }

            if (PathsOverlap(setting.FromPath, setting.ToPath))
            {
                message = "From path and To path cannot be the same or inside each other.";
                return false;
            }

            if (requireDirectories && !Directory.Exists(setting.FromPath))
            {
                message = $"From path does not exist: {setting.FromPath}";
                return false;
            }

            if (requireDirectories && !Directory.Exists(setting.ToPath))
            {
                message = $"To path does not exist: {setting.ToPath}";
                return false;
            }

            return true;
        }

        private bool EnsureDirectoryExists(string path, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(path))
            {
                message = "Path is empty.";
                return false;
            }

            if (Directory.Exists(path))
            {
                return true;
            }

            if (MessageBox.Show($"The folder does not exist:\n\n{path}\n\nCreate it?", "Create folder", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                message = $"Folder was not created: {path}";
                return false;
            }

            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception ex)
            {
                message = $"Could not create folder: {path}\n\n{ex.Message}";
                return false;
            }
        }

        private static bool PathsOverlap(string first, string second)
        {
            try
            {
                string firstPath = NormalizeDirectoryPath(first);
                string secondPath = NormalizeDirectoryPath(second);
                return string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase)
                    || firstPath.StartsWith(secondPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || secondPath.StartsWith(firstPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first.TrimEnd('\\'), second.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeDirectoryPath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void LoadSettings()
        {
            settings.Clear();
            loadingSettings = true;
            if (!File.Exists(configPath))
            {
                loadingSettings = false;
                return;
            }

            try
            {
                AppConfig? config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath));
                if (config?.Settings == null)
                {
                    loadingSettings = false;
                    return;
                }

                chkAutorun.Checked = config.AutoRunAtStartup;
                numLogKeepDays.Value = ClampKeepDays(config.LogKeepDays, DefaultLogKeepDays);
                numBackupKeepDays.Value = ClampKeepDays(config.BackupKeepDays, DefaultBackupKeepDays);
                if (!TrySetAutorun(config.AutoRunAtStartup, out string autorunError))
                {
                    Log($"Could not update autorun setting: {autorunError}");
                }
                foreach (CopySetting setting in config.Settings)
                {
                    setting.Id = setting.Id == Guid.Empty ? Guid.NewGuid() : setting.Id;
                    settings.Add(setting);
                }
            }
            catch (Exception ex)
            {
                Log($"Could not read config: {ex.Message}");
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                AppConfig config = new()
                {
                    Settings = settings,
                    AutoRunAtStartup = chkAutorun.Checked,
                    LogKeepDays = (int)numLogKeepDays.Value,
                    BackupKeepDays = (int)numBackupKeepDays.Value
                };
                JsonSerializerOptions options = new() { WriteIndented = true };
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
            }
            catch (Exception ex)
            {
                string message = $"Could not save settings file. Check write permission for: {AppContext.BaseDirectory}. {ex.Message}";
                SetLastAction(message);
                ShowStorageWarning(message);
                TryWriteLogLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
            }
        }

        private void CheckStorageWritable()
        {
            string testPath = Path.Combine(AppContext.BaseDirectory, ".quickbackup_write_test.tmp");
            try
            {
                File.WriteAllText(testPath, "test");
                File.Delete(testPath);
            }
            catch (Exception ex)
            {
                ShowStorageWarning($"QuickBackup cannot write to its executable folder: {AppContext.BaseDirectory}{Environment.NewLine}{Environment.NewLine}Settings and logs may not be saved. If installed under Program Files, run as administrator or move QuickBackup to a writable folder.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        }

        private void ShowStorageWarning(string message)
        {
            if (storageWarningShown)
            {
                return;
            }

            storageWarningShown = true;
            MessageBox.Show(message, "QuickBackup storage warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SaveRetentionSettings()
        {
            if (loadingSettings)
            {
                return;
            }

            SaveSettings();
            PurgeOldLogLines(true);
            CleanupBackupFolders();
        }

        private static int ClampKeepDays(int value, int defaultValue)
        {
            if (value < MinKeepDays)
            {
                return defaultValue;
            }

            return Math.Min(Math.Max(value, MinKeepDays), MaxKeepDays);
        }

        private void UpdateStatus()
        {
            int validCount = settings.Count(IsValidSetting);
            string stateText = isRunning
                ? $"Status: running ({workers.Count}/{validCount})"
                : $"Status: stopped ({validCount} valid)";
            lblStatus.Text = $"{stateText} - {lastActionMessage}";
        }

        private void Log(string message)
        {
            SetLastAction(message);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            if (TryWriteLogLine(line))
            {
                try
                {
                    PurgeOldLogLines(false);
                }
                catch (Exception ex)
                {
                    ShowLogWarning($"Could not purge log file: {ex.Message}");
                }
            }
        }

        private bool TryWriteLogLine(string line)
        {
            try
            {
                lock (logLock)
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
                return true;
            }
            catch (Exception ex)
            {
                ShowLogWarning($"Could not write log file. Check write permission for: {AppContext.BaseDirectory}. {ex.Message}");
                return false;
            }
        }

        private void ShowLogWarning(string message)
        {
            if (logWriteWarningShown)
            {
                return;
            }

            logWriteWarningShown = true;
            SetLastAction(message);
            BeginInvoke((Action)(() => MessageBox.Show(message, "QuickBackup log warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
        }

        private void SetLastAction(string message)
        {
            string shortMessage = message.Length > 90 ? message.Substring(0, 87) + "..." : message;
            lastActionMessage = shortMessage;
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateStatus);
            }
            else
            {
                UpdateStatus();
            }
        }

        private void ShowLogWindow()
        {
            string content = "";
            try
            {
                lock (logLock)
                {
                    if (File.Exists(logPath))
                    {
                        content = File.ReadAllText(logPath);
                    }
                }
            }
            catch (Exception ex)
            {
                content = $"Could not read log file: {ex.Message}";
            }

            Form logForm = new()
            {
                Text = "QuickBackup log",
                Size = new Size(800, 500),
                StartPosition = FormStartPosition.CenterParent
            };
            TextBox textBox = new()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = content
            };
            logForm.Controls.Add(textBox);
            logForm.Show(this);
        }

        private void PurgeOldLogLines(bool force)
        {
            DateTime today = DateTime.Today;
            if (!force && lastLogPurgeDate == today)
            {
                return;
            }

            lastLogPurgeDate = today;
            if (!File.Exists(logPath))
            {
                return;
            }

            DateTime cutoff = DateTime.Now.AddDays(-(int)numLogKeepDays.Value);
            string[] lines = File.ReadAllLines(logPath);
            List<string> keptLines = new List<string>();
            foreach (string line in lines)
            {
                if (line.Length < 19 || !DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out DateTime logDate))
                {
                    keptLines.Add(line);
                    continue;
                }

                if (logDate >= cutoff)
                {
                    keptLines.Add(line);
                }
            }

            if (keptLines.Count != lines.Length)
            {
                File.WriteAllLines(logPath, keptLines);
            }
        }

        private void ApplyAutorunCheckboxChange()
        {
            bool requestedState = chkAutorun.Checked;
            if (TrySetAutorun(requestedState, out string errorMessage))
            {
                SaveSettings();
                return;
            }

            SetAutorunCheckboxSilently(!requestedState);
            SaveSettings();

            string action = requestedState ? "enable" : "disable";
            string message = $"Could not {action} autorun at startup. QuickBackup could not update HKCU\\{StartupRegistryPath}\\{StartupRegistryValueName}. Check registry permission or Windows policy. {errorMessage}";
            Log(message);
            MessageBox.Show(message, "QuickBackup autorun warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SetAutorunCheckboxSilently(bool checkedValue)
        {
            updatingAutorunCheckbox = true;
            try
            {
                chkAutorun.Checked = checkedValue;
            }
            finally
            {
                updatingAutorunCheckbox = false;
            }
        }

        private bool TrySetAutorun(bool enabled, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, true);
                if (key == null)
                {
                    errorMessage = "Could not open startup registry key.";
                    return false;
                }

                if (enabled)
                {
                    string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                    string startupValue = $"\"{exePath}\"";
                    key.SetValue(StartupRegistryValueName, startupValue);
                    if (!string.Equals(key.GetValue(StartupRegistryValueName) as string, startupValue, StringComparison.Ordinal))
                    {
                        errorMessage = "Registry value was not saved correctly.";
                        return false;
                    }

                    Log($"Enabled autorun at startup: {exePath}");
                }
                else
                {
                    key.DeleteValue(StartupRegistryValueName, false);
                    if (key.GetValue(StartupRegistryValueName) != null)
                    {
                        errorMessage = "Registry value still exists after remove.";
                        return false;
                    }

                    Log("Disabled autorun at startup.");
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void ExitApplication()
        {
            allowExit = true;
            StopMonitoring();
            trayIcon.Visible = false;
            Application.Exit();
        }

        private void CollectMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void StartMemoryCollection()
        {
            if (Interlocked.Exchange(ref memoryCollectionRunning, 1) == 1)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    CollectMemory();
                }
                finally
                {
                    Interlocked.Exchange(ref memoryCollectionRunning, 0);
                }
            });
        }

        private void RunBackupCleanupIfNeeded()
        {
            DateTime today = DateTime.Today;
            if (lastBackupCleanupDate == today)
            {
                return;
            }

            CleanupBackupFolders();
        }

        private void CleanupBackupFolders()
        {
            lastBackupCleanupDate = DateTime.Today;
            DateTime cutoff = DateTime.Now.AddDays(-(int)numBackupKeepDays.Value);
            foreach (string backupFolder in settings
                .Where(IsValidSetting)
                .Select(setting => Path.Combine(setting.ToPath, MonitorWorker.BackupFolderName))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(backupFolder))
                    {
                        continue;
                    }

                    foreach (string file in Directory.EnumerateFiles(backupFolder))
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                            Log($"Removed old backup file: {file}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Could not clean backup folder {backupFolder}: {ex.Message}");
                }
            }
        }

    }

    public sealed class AppConfig
    {
        public bool AutoRunAtStartup { get; set; }
        public int LogKeepDays { get; set; } = 7;
        public int BackupKeepDays { get; set; } = 3;
        public List<CopySetting> Settings { get; set; } = [];
    }

    public sealed class CopySetting
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FromPath { get; set; } = "";
        public string ToPath { get; set; } = "";
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
