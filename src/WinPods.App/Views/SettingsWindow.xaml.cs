using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using WinPods.App.Services;
using Windows.UI;

namespace WinPods.App.Views
{
    /// <summary>
    /// Settings window for WinPods.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "WinPods";

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(long wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const long SHCNE_ASSOCCHANGED = 0x08000000;

        private readonly SettingsService _settings;
        private DriverService? _driverService;

        public SettingsWindow()
        {
            this.InitializeComponent();

            // Set window title
            this.Title = "WinPods Settings";

            // Set window size - make it taller to accommodate new settings
            AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 850));

            // Center the window on screen
            CenterWindow();

            // Get settings service
            _settings = SettingsService.Instance;

            // Initialize toggle states from settings
            AutoStartToggle.IsOn = _settings.AutoStartEnabled;
            EarDetectionToggle.IsOn = _settings.EarDetectionEnabled;
            AutoPauseToggle.IsOn = _settings.AutoPauseOnRemoval;
            AutoResumeToggle.IsOn = _settings.AutoResumeOnInsertion;

            // Update toggle enablement
            UpdateEarDetectionToggleStates();

            // Initialize driver status
            InitializeDriverStatus();
        }

        /// <summary>
        /// Initializes the driver status display.
        /// </summary>
        private void InitializeDriverStatus()
        {
            try
            {
                _driverService = new DriverService();
                UpdateDriverStatusUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Failed to initialize driver status: {ex.Message}");
                DriverStatusText.Text = "Error";
                DriverStatusBadge.Background = new SolidColorBrush(Color.FromArgb(255, 255, 59, 48));
                InstallDriverButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Updates the driver status UI.
        /// </summary>
        private void UpdateDriverStatusUI()
        {
            if (_driverService == null) return;

            bool isInstalled = _driverService.IsInstalled;

            if (isInstalled)
            {
                DriverStatusText.Text = "Installed";
                DriverStatusBadge.Background = new SolidColorBrush(Color.FromArgb(255, 48, 209, 88)); // Green
                DriverStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                InstallDriverButton.Content = "Reinstall";
                DriverDescriptionText.Text = "Noise control features are available";
            }
            else
            {
                DriverStatusText.Text = "Not Installed";
                DriverStatusBadge.Background = new SolidColorBrush(Color.FromArgb(255, 58, 58, 60)); // Gray
                DriverStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 142, 142, 147));
                InstallDriverButton.Content = "Install";
                DriverDescriptionText.Text = "Required for noise control (ANC/Transparency) features";
            }

            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Driver status updated: {(isInstalled ? "Installed" : "Not Installed")}");
        }

        /// <summary>
        /// Centers the window on the screen.
        /// </summary>
        private void CenterWindow()
        {
            try
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    var workArea = displayArea.WorkArea;
                    var width = 700;
                    var height = 850;
                    var x = (workArea.Width - width) / 2;
                    var y = (workArea.Height - height) / 2;
                    AppWindow.Move(new Windows.Graphics.PointInt32((int)x, (int)y));
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Failed to center window: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the auto-start toggle change.
        /// </summary>
        private void OnAutoStartToggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = AutoStartToggle.IsOn;
            _settings.AutoStartEnabled = isEnabled;
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Auto-start {(isEnabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Handles the ear detection toggle change.
        /// </summary>
        private void OnEarDetectionToggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = EarDetectionToggle.IsOn;
            _settings.EarDetectionEnabled = isEnabled;
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Ear detection {(isEnabled ? "enabled" : "disabled")}");

            // Update auto-pause and auto-resume toggle visibility/enablement
            UpdateEarDetectionToggleStates();
        }

        /// <summary>
        /// Handles the auto-pause toggle change.
        /// </summary>
        private void OnAutoPauseToggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = AutoPauseToggle.IsOn;
            _settings.AutoPauseOnRemoval = isEnabled;
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Auto-pause on removal {(isEnabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Handles the auto-resume toggle change.
        /// </summary>
        private void OnAutoResumeToggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = AutoResumeToggle.IsOn;
            _settings.AutoResumeOnInsertion = isEnabled;
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Auto-resume on insertion {(isEnabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Updates the enabled state of ear detection sub-toggles based on master toggle.
        /// </summary>
        private void UpdateEarDetectionToggleStates()
        {
            bool earDetectionEnabled = EarDetectionToggle.IsOn;
            AutoPauseToggle.IsEnabled = earDetectionEnabled;
            AutoResumeToggle.IsEnabled = earDetectionEnabled;
        }

        /// <summary>
        /// Handles the install driver button click.
        /// </summary>
        private async void OnInstallDriverClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = "Installing...";
            }

            try
            {
                // Get the path to the driver INF file
                string appPath = AppContext.BaseDirectory;
                string driverInfPath = Path.Combine(appPath, "driver", "WinPodsAAP", "WinPodsAAP.inf");

                // Check if the INF file exists in the app directory
                // If not, check relative to the solution (for development)
                if (!File.Exists(driverInfPath))
                {
                    // Try to find it relative to the project
                    string? projectRoot = FindProjectRoot();
                    if (projectRoot != null)
                    {
                        driverInfPath = Path.Combine(projectRoot, "driver", "WinPodsAAP", "WinPodsAAP.inf");
                    }
                }

                if (!File.Exists(driverInfPath))
                {
                    await ShowErrorDialogAsync("Driver Not Found",
                        "The WinPodsAAP driver files were not found.\n\n" +
                        "Please ensure the driver is built and deployed with the application.\n\n" +
                        "See driver/WinPodsAAP/README.md for build instructions.");
                    return;
                }

                // Run the driver installer with elevation
                bool success = await InstallDriverAsync(driverInfPath);

                if (success)
                {
                    await ShowSuccessDialogAsync("Driver Installed",
                        "The WinPodsAAP driver has been installed successfully.\n\n" +
                        "You may need to restart the application for noise control features to become available.");
                    UpdateDriverStatusUI();
                }
                else
                {
                    await ShowErrorDialogAsync("Installation Failed",
                        "The driver installation was not completed.\n\n" +
                        "Make sure you approved the User Account Control (UAC) prompt.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Driver install error: {ex.Message}");
                await ShowErrorDialogAsync("Installation Error", $"An error occurred: {ex.Message}");
            }
            finally
            {
                if (button != null)
                {
                    button.IsEnabled = true;
                    UpdateDriverStatusUI();
                }
            }
        }

        /// <summary>
        /// Installs the driver using pnputil with elevation.
        /// </summary>
        private async Task<bool> InstallDriverAsync(string infPath)
        {
            try
            {
                // Use PowerShell to run pnputil with elevation
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Start-Process pnputil -ArgumentList '/add-driver', '{infPath}' -Verb RunAs -Wait\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] InstallDriverAsync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds the project root directory.
        /// </summary>
        private static string? FindProjectRoot()
        {
            string? currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null)
            {
                if (File.Exists(Path.Combine(currentDir, "WinPods.slnx")) ||
                    File.Exists(Path.Combine(currentDir, "WinPods.sln")))
                {
                    return currentDir;
                }
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            return null;
        }

        /// <summary>
        /// Shows an error dialog.
        /// </summary>
        private async Task ShowErrorDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        /// <summary>
        /// Shows a success dialog.
        /// </summary>
        private async Task ShowSuccessDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        /// <summary>
        /// Handles the close button click.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
