using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Forms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;

namespace DBServersManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DatabaseServiceItem> _services = [];
    private readonly List<DatabaseServiceItem> _allServices = [];
    private readonly ICollectionView _servicesView;
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _allowClose;

    private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "DBServersManager";
    private const string AllDbTypesOption = "All DB Types";

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            _servicesView = CollectionViewSource.GetDefaultView(_services);
            _servicesView.Filter = ServiceFilter;
            ServicesGrid.ItemsSource = _servicesView;

            _trayIcon = CreateTrayIcon();
            StateChanged += MainWindow_OnStateChanged;
            Closing += MainWindow_OnClosing;

            InitializeFilterControls();
            LoadStartupPreference();
            _initialized = true;
            _ = RefreshServicesAsync();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to initialize application: {ex}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }

    private async Task RefreshServicesAsync()
    {
        try
        {
            SetStatus("Scanning database services...");
            var items = await Task.Run(DiscoverDatabaseServices);

            _allServices.Clear();
            _allServices.AddRange(items);

            _services.Clear();
            foreach (var item in items)
            {
                _services.Add(item);
            }

            UpdateTypeFilterOptions();
            ApplyViewSettings();
            SetStatus($"Found {_services.Count} database-related services.");
        }
        catch (Exception ex)
        {
            SetStatus("Failed to scan services.");
            WpfMessageBox.Show($"Could not load services: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Filter[] GetDatabaseFilters()
    {
        return new[]
        {
            new Filter("PostgreSQL", "postgres", "postgresql", "pgsql"),
            new Filter("Oracle", "oracle", "oraclesvc", "oracleservice"),
            new Filter("MySQL", "mysql", "mariadb"),
            new Filter("MSSQL", "mssql", "sql server", "sqlbrowser", "sqlwriter", "sqlagent")
        };
    }

    private static List<DatabaseServiceItem> DiscoverDatabaseServices()
    {
        var filters = GetDatabaseFilters();
        var items = new List<DatabaseServiceItem>();
        var controllers = ServiceController.GetServices();

        foreach (var controller in controllers)
        {
            var databaseType = MatchDatabaseType(controller, filters);
            if (databaseType is null)
            {
                continue;
            }

            var startType = GetStartType(controller.ServiceName);

            items.Add(new DatabaseServiceItem
            {
                DatabaseType = databaseType,
                ServiceName = controller.ServiceName,
                DisplayName = controller.DisplayName,
                Status = controller.Status.ToString(),
                StartType = startType
            });
        }

        return items
            .OrderBy(s => s.DatabaseType)
            .ThenBy(s => s.DisplayName)
            .ToList();
    }

    private static string? MatchDatabaseType(ServiceController controller, IReadOnlyList<Filter> filters)
    {
        var combined = $"{controller.ServiceName} {controller.DisplayName}".ToLowerInvariant();

        foreach (var filter in filters)
        {
            if (filter.Keywords.Any(combined.Contains))
            {
                return filter.Type;
            }
        }

        return null;
    }

    private static string GetStartType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var startValue = key?.GetValue("Start");
            return startValue switch
            {
                0 => "Boot",
                1 => "System",
                2 => "Automatic",
                3 => "Manual",
                4 => "Disabled",
                _ => "Unknown"
            };
        }
        catch
        {
            return "Unknown";
        }
    }

    private async Task ControlServiceAsync(string serviceName, ServiceAction action)
    {
        try
        {
            SetStatus($"{action} {serviceName}...");
            await Task.Run(() => ExecuteServiceAction(serviceName, action));
            SetStatus($"{action} completed for {serviceName}.");
            await RefreshServicesAsync();
        }
        catch (InvalidOperationException ex)
        {
            SetStatus($"{action} failed for {serviceName}.");
            WpfMessageBox.Show(ex.Message, "Service Action Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Win32Exception)
        {
            SetStatus($"{action} failed for {serviceName}.");
            WpfMessageBox.Show(
                "Access denied. Please run the app as Administrator to manage service states.",
                "Permission Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ControlServicesByTypeAsync(ServiceAction action)
    {
        var selectedType = TypeFilterComboBox.SelectedItem as string ?? AllDbTypesOption;
        var targets = _allServices
            .Where(s => selectedType == AllDbTypesOption || s.DatabaseType.Equals(selectedType, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.ServiceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
        {
            WpfMessageBox.Show("No services found for the selected type.", "Nothing to do", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var scopeText = selectedType == AllDbTypesOption ? "all database types" : selectedType;
        var confirmation = WpfMessageBox.Show(
            $"{action} {targets.Count} service(s) in {scopeText}?",
            "Confirm bulk action",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetStatus($"Running bulk action: {action} ({targets.Count} services)...");

        var failures = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var serviceName in targets)
            {
                try
                {
                    ExecuteServiceAction(serviceName, action);
                }
                catch (Exception ex)
                {
                    failed.Add($"{serviceName}: {ex.Message}");
                }
            }

            return failed;
        });

        await RefreshServicesAsync();

        if (failures.Count == 0)
        {
            SetStatus($"Bulk {action} completed successfully for {targets.Count} service(s).");
            return;
        }

        SetStatus($"Bulk {action} completed with {failures.Count} failure(s).");
        WpfMessageBox.Show(
            $"Some services could not be processed:\n\n{string.Join("\n", failures.Take(10))}\n{(failures.Count > 10 ? "\n..." : string.Empty)}",
            "Bulk Action Results",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void ExecuteServiceAction(string serviceName, ServiceAction action)
    {
        using var controller = new ServiceController(serviceName);

        switch (action)
        {
            case ServiceAction.Start:
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                }

                break;

            case ServiceAction.Stop:
                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }

                break;

            case ServiceAction.Restart:
                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }

                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                break;
        }
    }

    private void LoadStartupPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, writable: false);
            AutoStartCheckBox.IsChecked = key?.GetValue(StartupValueName) is string;
        }
        catch
        {
            AutoStartCheckBox.IsChecked = false;
        }
    }

    private void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKeyPath, true);
        if (key is null)
        {
            throw new InvalidOperationException("Unable to access startup settings in registry.");
        }

        if (enabled)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? throw new InvalidOperationException("Cannot determine app executable path.");
            key.SetValue(StartupValueName, $"\"{exePath}\"");
            SetStatus("Enabled launch on Windows startup.");
        }
        else
        {
            key.DeleteValue(StartupValueName, false);
            SetStatus("Disabled launch on Windows startup.");
        }
    }

    private bool _initialized;

    private void SetStatus(string message)
    {
        if (_initialized)
        {
            StatusText.Text = message;
        }
    }

    private void InitializeFilterControls()
    {
        SearchTextBox.Text = string.Empty;
        TypeFilterComboBox.ItemsSource = new[] { AllDbTypesOption };
        TypeFilterComboBox.SelectedIndex = 0;
        GroupByTypeCheckBox.IsChecked = true;
    }

    private void UpdateTypeFilterOptions()
    {
        var existingSelection = TypeFilterComboBox.SelectedItem as string;

        // Only show database types that are actually discovered on the system.
        var types = _allServices
            .Select(s => s.DatabaseType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        types.Insert(0, AllDbTypesOption);
        TypeFilterComboBox.ItemsSource = types;

        if (!string.IsNullOrWhiteSpace(existingSelection) && types.Contains(existingSelection))
        {
            TypeFilterComboBox.SelectedItem = existingSelection;
        }
        else
        {
            TypeFilterComboBox.SelectedIndex = 0;
        }
    }

    private bool ServiceFilter(object obj)
    {
        if (obj is not DatabaseServiceItem item)
        {
            return false;
        }

        var selectedType = TypeFilterComboBox.SelectedItem as string ?? AllDbTypesOption;
        if (!selectedType.Equals(AllDbTypesOption, StringComparison.OrdinalIgnoreCase) &&
            !item.DatabaseType.Equals(selectedType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var keyword = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return item.ServiceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || item.DatabaseType.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || item.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyViewSettings()
    {
        if (_servicesView is not ListCollectionView collectionView)
        {
            return;
        }

        using (collectionView.DeferRefresh())
        {
            collectionView.SortDescriptions.Clear();
            collectionView.GroupDescriptions.Clear();

            var sortMode = (SortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Type (A-Z)";
            switch (sortMode)
            {
                case "Service Name (A-Z)":
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(DatabaseServiceItem.ServiceName), ListSortDirection.Ascending));
                    break;

                case "Display Name (A-Z)":
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(DatabaseServiceItem.DisplayName), ListSortDirection.Ascending));
                    break;

                case "Status":
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(DatabaseServiceItem.Status), ListSortDirection.Ascending));
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(DatabaseServiceItem.DatabaseType), ListSortDirection.Ascending));
                    break;

                default:
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(DatabaseServiceItem.DatabaseType), ListSortDirection.Ascending));
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(DatabaseServiceItem.DisplayName), ListSortDirection.Ascending));
                    break;
            }

            if (GroupByTypeCheckBox.IsChecked == true)
            {
                collectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DatabaseServiceItem.DatabaseType)));
            }
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Refresh", null, async (_, _) => await RefreshServicesAsync());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var icon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "DB Servers Manager",
            Visible = false,
            ContextMenuStrip = menu
        };

        icon.DoubleClick += (_, _) => RestoreFromTray();
        return icon;
    }

    private System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            // Prefer the project's icon in Assets folder
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DBServersManager.ico");
            if (System.IO.File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath);
            }

            // Fallback to executable icon that should carry same branding
            var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
            if (exeIcon != null)
            {
                return exeIcon;
            }
        }
        catch
        {
            // ignore and fallback to default
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        _trayIcon.Visible = true;
        Hide();
        ShowInTaskbar = false;
        SetStatus("Running in system tray.");
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
        SetStatus("Window restored from tray.");
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshServicesAsync();
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        _servicesView.Refresh();
    }

    private void TypeFilterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        _servicesView.Refresh();
        SetStatus("Applied type filter.");
    }

    private void SortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        ApplyViewSettings();
        SetStatus("Applied sorting.");
    }

    private void GroupByTypeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ApplyViewSettings();
        SetStatus(GroupByTypeCheckBox.IsChecked == true ? "Grouping enabled." : "Grouping disabled.");
    }

    private async void StartAllByTypeButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ControlServicesByTypeAsync(ServiceAction.Start);
    }

    private async void StopAllByTypeButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ControlServicesByTypeAsync(ServiceAction.Stop);
    }

    private async void StartServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { CommandParameter: string serviceName })
        {
            return;
        }

        await ControlServiceAsync(serviceName, ServiceAction.Start);
    }

    private async void StopServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { CommandParameter: string serviceName })
        {
            return;
        }

        await ControlServiceAsync(serviceName, ServiceAction.Stop);
    }

    private async void RestartServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { CommandParameter: string serviceName })
        {
            return;
        }

        await ControlServiceAsync(serviceName, ServiceAction.Restart);
    }

    private void AutoStartCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            SetAutoStart(true);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Unable to enable startup: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            AutoStartCheckBox.IsChecked = false;
        }
    }

    private void AutoStartCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        try
        {
            SetAutoStart(false);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Unable to disable startup: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            AutoStartCheckBox.IsChecked = true;
        }
    }

    private sealed class Filter(string type, params string[] keywords)
    {
        public string Type { get; } = type;
        public IReadOnlyList<string> Keywords { get; } = keywords;
    }

    private sealed class DatabaseServiceItem
    {
        public string DatabaseType { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StartType { get; set; } = string.Empty;
    }

    private enum ServiceAction
    {
        Start,
        Stop,
        Restart
    }
}