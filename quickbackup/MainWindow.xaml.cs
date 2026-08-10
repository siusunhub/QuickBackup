using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Panel = System.Windows.Controls.Panel;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FontFamily = System.Windows.Media.FontFamily;

namespace quickbackup
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "0.30";
        private const int DefaultLogKeepDays = 7;
        private const int DefaultBackupKeepDays = 3;
        private const int MinKeepDays = 1;
        private const int MaxKeepDays = 14;
        private const string StartupRegistryValueName = "QuickBackup";
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static string AppTitle => $"QuickBackup {AppVersion}";

        private readonly string configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "quickbackup.settings.json");
        private readonly string logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "quickbackup.log");
        private readonly List<CopySetting> settings = [];
        private readonly Dictionary<Guid, MonitorWorker> workers = [];
        private readonly Dictionary<Guid, FrameworkElement> settingRows = [];
        private readonly Dictionary<Guid, Ellipse> statusLights = [];
        private readonly object logLock = new();

        private readonly DispatcherTimer refreshTimer = new();
        private readonly DispatcherTimer memoryTimer = new();
        private readonly DispatcherTimer backupCleanupTimer = new();

        private WinForms.NotifyIcon? trayIcon;
        private WinForms.ContextMenuStrip? trayMenu;

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

        public MainWindow()
        {
            loadingSettings = true;
            InitializeComponent();
            Title = AppTitle;

            InitTrayIcon();
            InitComboBoxes();

            refreshTimer.Interval = TimeSpan.FromSeconds(30);
            refreshTimer.Tick += (_, _) => ReconcileWorkers();

            memoryTimer.Interval = TimeSpan.FromHours(1);
            memoryTimer.Tick += (_, _) => StartMemoryCollection();
            memoryTimer.Start();

            backupCleanupTimer.Interval = TimeSpan.FromMinutes(1);
            backupCleanupTimer.Tick += (_, _) => RunBackupCleanupIfNeeded();
            backupCleanupTimer.Start();

            Closing += MainWindow_Closing;

            LoadSettings();
            CheckStorageWritable();
            PurgeOldLogLines(true);
            CleanupBackupFolders();
            RenderRows();

            autoStartOnLoad = settings.Any(IsValidSetting);
        }

        private void InitTrayIcon()
        {
            trayMenu = new WinForms.ContextMenuStrip();
            WinForms.ToolStripMenuItem menuOpen = new("Open");
            WinForms.ToolStripMenuItem menuExit = new("Exit");

            menuOpen.Click += (_, _) => ShowMainWindow();
            menuExit.Click += (_, _) => ExitApplication();

            trayMenu.Items.Add(menuOpen);
            trayMenu.Items.Add(menuExit);

            System.Drawing.Icon? appIcon = null;
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch
            {
            }

            if (appIcon == null)
            {
                try
                {
                    string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "database-management.ico");
                    if (File.Exists(iconPath))
                    {
                        appIcon = new System.Drawing.Icon(iconPath);
                    }
                }
                catch
                {
                }
            }

            appIcon ??= System.Drawing.SystemIcons.Application;

            trayIcon = new WinForms.NotifyIcon
            {
                Icon = appIcon,
                Text = AppTitle,
                Visible = true,
                ContextMenuStrip = trayMenu
            };

            trayIcon.MouseClick += (sender, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };
        }

        private void InitComboBoxes()
        {
            for (int i = MinKeepDays; i <= MaxKeepDays; i++)
            {
                cmbLogKeepDays.Items.Add(i);
                cmbBackupKeepDays.Items.Add(i);
            }
            cmbLogKeepDays.SelectedItem = DefaultLogKeepDays;
            cmbBackupKeepDays.SelectedItem = DefaultBackupKeepDays;
        }

        public void StartApplication()
        {
            if (autoStartOnLoad && !initialAutoStartHandled)
            {
                initialAutoStartHandled = true;
                if (TryValidateStart(out string message))
                {
                    StartMonitoring();
                    return;
                }
                else
                {
                    SetLastAction(message);
                }
            }

            Show();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            allowExit = true;
            StopMonitoring();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            System.Windows.Application.Current.Shutdown();
        }

        private void AddNewRow()
        {
            CopySetting setting = new();
            settings.Add(setting);
            SaveSettings();
            AddSettingRow(setting);
            UpdateStatus();
            ReconcileWorkers();
        }

        private void RemoveRow(CopySetting setting)
        {
            if (MessageBox.Show("Remove this copy setting?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            settings.Remove(setting);
            SaveSettings();
            statusLights.Remove(setting.Id);
            if (settingRows.TryGetValue(setting.Id, out FrameworkElement? row))
            {
                settingRows.Remove(setting.Id);
                rowsPanel.Children.Remove(row);
            }

            UpdateStatus();
            ReconcileWorkers();
        }

        private void RenderRows()
        {
            rowsPanel.Children.Clear();
            settingRows.Clear();
            statusLights.Clear();

            foreach (CopySetting setting in settings)
            {
                AddSettingRow(setting);
            }

            UpdateStatus();
        }

        private void AddSettingRow(CopySetting setting)
        {
            Grid row = new()
            {
                Height = 38,
                Margin = new Thickness(0, 0, 0, 6)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            // 0: Status Light
            FrameworkElement statusLightControl = CreateStatusLight(setting);
            Grid.SetColumn(statusLightControl, 0);
            row.Children.Add(statusLightControl);

            // 1: Source Path Picker
            FrameworkElement sourcePicker = CreatePathPicker(setting, true);
            Grid.SetColumn(sourcePicker, 1);
            row.Children.Add(sourcePicker);

            // 2: Destination Path Picker
            FrameworkElement destPicker = CreatePathPicker(setting, false);
            Grid.SetColumn(destPicker, 2);
            row.Children.Add(destPicker);

            // 3: Backup Checkbox
            FrameworkElement backupControl = CreateBackupCheckbox(setting);
            Grid.SetColumn(backupControl, 3);
            row.Children.Add(backupControl);

            // 4: Remove Button
            Button removeButton = new()
            {
                Content = "\uE107",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Width = 34,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Remove Backup Path"
            };
            removeButton.Click += (_, _) => RemoveRow(setting);
            Grid.SetColumn(removeButton, 4);
            row.Children.Add(removeButton);

            settingRows[setting.Id] = row;
            rowsPanel.Children.Add(row);

            SetRowEditEnabled(row, !isRunning);
            UpdateSettingStatus(setting);
        }

        private void SetRowsEditEnabled(bool enabled)
        {
            btnAdd.IsEnabled = enabled;
            foreach (FrameworkElement row in settingRows.Values)
            {
                SetRowEditEnabled(row, enabled);
            }
        }

        private static void SetRowEditEnabled(FrameworkElement element, bool enabled)
        {
            if (element is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    if (child is FrameworkElement childFe)
                    {
                        SetRowEditEnabled(childFe, enabled);
                    }
                }
            }
            else if (element is Grid grid)
            {
                foreach (UIElement child in grid.Children)
                {
                    if (child is FrameworkElement childFe)
                    {
                        SetRowEditEnabled(childFe, enabled);
                    }
                }
            }
            else if (element is TextBox textBox)
            {
                textBox.IsReadOnly = !enabled;
            }
            else if (element is Button button)
            {
                button.IsEnabled = button.Name == "openPathButton" || enabled;
            }
            else if (element is CheckBox checkBox)
            {
                checkBox.IsEnabled = enabled;
            }
        }

        private FrameworkElement CreateStatusLight(CopySetting setting)
        {
            Grid container = new()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Ellipse light = new()
            {
                Width = 18,
                Height = 18,
                Fill = new SolidColorBrush(Colors.Goldenrod),
                ToolTip = "Ready"
            };

            container.Children.Add(light);
            statusLights[setting.Id] = light;
            return container;
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
            if (!statusLights.TryGetValue(setting.Id, out Ellipse? light))
            {
                return;
            }

            if (!TryValidateConfiguredSetting(setting, out string message))
            {
                light.Fill = new SolidColorBrush(Colors.Firebrick);
                light.ToolTip = message;
                return;
            }

            if (isRunning && workers.ContainsKey(setting.Id))
            {
                light.Fill = new SolidColorBrush(Colors.ForestGreen);
                light.ToolTip = "Running";
            }
            else
            {
                message = string.IsNullOrWhiteSpace(setting.FromPath) && string.IsNullOrWhiteSpace(setting.ToPath)
                    ? "Not configured"
                    : "Ready";
                light.Fill = new SolidColorBrush(Colors.Goldenrod);
                light.ToolTip = message;
            }
        }

        private FrameworkElement CreateBackupCheckbox(CopySetting setting)
        {
            CheckBox checkBox = new()
            {
                IsChecked = setting.BackupBeforeChange,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            checkBox.Click += (_, _) =>
            {
                if (isRunning)
                {
                    checkBox.IsChecked = setting.BackupBeforeChange;
                    return;
                }

                setting.BackupBeforeChange = checkBox.IsChecked ?? false;
                SaveSettings();
                ReconcileWorkers();
            };

            return checkBox;
        }

        private FrameworkElement CreatePathPicker(CopySetting setting, bool isSource)
        {
            Grid grid = new()
            {
                Margin = new Thickness(4, 3, 8, 3)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox textBox = new()
            {
                Text = isSource ? setting.FromPath : setting.ToPath,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            Button browseButton = new()
            {
                Content = "📁",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 14,
                Width = 34,
                Height = 28,
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = "Select Folder"
            };

            Button openButton = new()
            {
                Content = "\uE8A7",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Name = "openPathButton",
                Width = 34,
                Height = 28,
                ToolTip = "Open in Explorer"
            };

            textBox.LostFocus += (_, _) =>
            {
                if (!isRunning)
                {
                    SetPathValue(setting, isSource, textBox.Text);
                }
            };

            textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    SetPathValue(setting, isSource, textBox.Text);
                    e.Handled = true;
                }
            };

            browseButton.Click += (_, _) =>
            {
                if (isRunning)
                {
                    return;
                }

                using WinForms.FolderBrowserDialog dialog = new()
                {
                    SelectedPath = Directory.Exists(textBox.Text) ? textBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
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
                    MessageBox.Show(message, "QuickBackup", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            Grid.SetColumn(textBox, 0);
            Grid.SetColumn(browseButton, 1);
            Grid.SetColumn(openButton, 2);

            grid.Children.Add(textBox);
            grid.Children.Add(browseButton);
            grid.Children.Add(openButton);

            return grid;
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

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddNewRow();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
        }

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            ShowLogWindow();
        }

        private void CmbRetention_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveRetentionSettings();
        }

        private void ChkAutorun_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!loadingSettings && !updatingAutorunCheckbox)
            {
                ApplyAutorunCheckboxChange();
            }
        }

        private void StartMonitoring()
        {
            if (!TryValidateStart(out string message, createMissingDirectories: true))
            {
                UpdateAllSettingStatuses();
                SetLastAction(message);
                MessageBox.Show(message, "QuickBackup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CleanupBackupFolders();
            isRunning = true;
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
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
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
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

            if (MessageBox.Show($"The folder does not exist:\n\n{path}\n\nCreate it?", "Create folder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
                    || firstPath.StartsWith(secondPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || secondPath.StartsWith(firstPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first.TrimEnd('\\'), second.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeDirectoryPath(string path)
        {
            return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        private void LoadSettings()
        {
            settings.Clear();
            loadingSettings = true;

            string targetConfigPath = configPath;
            if (!File.Exists(targetConfigPath))
            {
                string currentDirConfig = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "quickbackup.settings.json");
                if (File.Exists(currentDirConfig))
                {
                    targetConfigPath = currentDirConfig;
                }
                else
                {
                    loadingSettings = false;
                    return;
                }
            }

            try
            {
                AppConfig? config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(targetConfigPath), jsonOptions);
                if (config?.Settings == null)
                {
                    loadingSettings = false;
                    return;
                }

                chkAutorun.IsChecked = config.AutoRunAtStartup;
                cmbLogKeepDays.SelectedItem = ClampKeepDays(config.LogKeepDays, DefaultLogKeepDays);
                cmbBackupKeepDays.SelectedItem = ClampKeepDays(config.BackupKeepDays, DefaultBackupKeepDays);

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
                    AutoRunAtStartup = chkAutorun.IsChecked ?? false,
                    LogKeepDays = (int)(cmbLogKeepDays.SelectedItem ?? DefaultLogKeepDays),
                    BackupKeepDays = (int)(cmbBackupKeepDays.SelectedItem ?? DefaultBackupKeepDays)
                };

                File.WriteAllText(configPath, JsonSerializer.Serialize(config, jsonOptions));
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
            string testPath = System.IO.Path.Combine(AppContext.BaseDirectory, ".quickbackup_write_test.tmp");
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
            MessageBox.Show(message, "QuickBackup storage warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Dispatcher.BeginInvoke(() => MessageBox.Show(message, "QuickBackup log warning", MessageBoxButton.OK, MessageBoxImage.Warning));
        }

        private void SetLastAction(string message)
        {
            string shortMessage = message.Length > 90 ? message.Substring(0, 87) + "..." : message;
            lastActionMessage = shortMessage;

            if (Dispatcher.CheckAccess())
            {
                UpdateStatus();
            }
            else
            {
                Dispatcher.BeginInvoke((Action)UpdateStatus);
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

            LogWindow logWin = new(content)
            {
                Owner = this
            };
            logWin.ShowDialog();
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

            int keepDays = (int)(cmbLogKeepDays.SelectedItem ?? DefaultLogKeepDays);
            DateTime cutoff = DateTime.Now.AddDays(-keepDays);
            string[] lines = File.ReadAllLines(logPath);
            List<string> keptLines = [];

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
            bool requestedState = chkAutorun.IsChecked ?? false;
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
            MessageBox.Show(message, "QuickBackup autorun warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SetAutorunCheckboxSilently(bool checkedValue)
        {
            updatingAutorunCheckbox = true;
            try
            {
                chkAutorun.IsChecked = checkedValue;
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
                    string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
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
            int keepDays = (int)(cmbBackupKeepDays.SelectedItem ?? DefaultBackupKeepDays);
            DateTime cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (string backupFolder in settings
                .Where(IsValidSetting)
                .Select(setting => System.IO.Path.Combine(setting.ToPath, MonitorWorker.BackupFolderName))
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
}
