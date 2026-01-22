using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using WinPods.App.Services;

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
        /// Handles the close button click.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
