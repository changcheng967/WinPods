using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace WinPods.App.Services
{
    /// <summary>
    /// Manages application settings using Windows Registry.
    /// </summary>
    public class SettingsService : INotifyPropertyChanged
    {
        private const string RegistryKey = @"Software\WinPods";
        private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "WinPods";

        // Setting names
        private const string EarDetectionEnabledKey = "EarDetectionEnabled";
        private const string AutoPauseOnRemovalKey = "AutoPauseOnRemoval";
        private const string AutoResumeOnInsertionKey = "AutoResumeOnInsertion";

        private static SettingsService? _instance;
        private static readonly object _lock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;

        public static SettingsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SettingsService();
                    }
                }
                return _instance;
            }
        }

        private SettingsService()
        {
            // Initialize with default values from registry
            LoadSettings();
        }

        private bool _earDetectionEnabled = true;
        private bool _autoPauseOnRemoval = true;
        private bool _autoResumeOnInsertion = true;

        /// <summary>
        /// Gets or sets whether ear detection is enabled.
        /// </summary>
        public bool EarDetectionEnabled
        {
            get => _earDetectionEnabled;
            set
            {
                if (_earDetectionEnabled != value)
                {
                    _earDetectionEnabled = value;
                    SaveSetting(EarDetectionEnabledKey, value);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to auto-pause when earbuds are removed.
        /// </summary>
        public bool AutoPauseOnRemoval
        {
            get => _autoPauseOnRemoval;
            set
            {
                if (_autoPauseOnRemoval != value)
                {
                    _autoPauseOnRemoval = value;
                    SaveSetting(AutoPauseOnRemovalKey, value);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to auto-resume when earbuds are inserted.
        /// </summary>
        public bool AutoResumeOnInsertion
        {
            get => _autoResumeOnInsertion;
            set
            {
                if (_autoResumeOnInsertion != value)
                {
                    _autoResumeOnInsertion = value;
                    SaveSetting(AutoResumeOnInsertionKey, value);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether auto-start with Windows is enabled.
        /// </summary>
        public bool AutoStartEnabled
        {
            get => IsAutoStartEnabled();
            set
            {
                SetAutoStart(value);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Loads settings from registry.
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false))
                {
                    if (key != null)
                    {
                        _earDetectionEnabled = GetBoolValue(key, EarDetectionEnabledKey, true);
                        _autoPauseOnRemoval = GetBoolValue(key, AutoPauseOnRemovalKey, true);
                        _autoResumeOnInsertion = GetBoolValue(key, AutoResumeOnInsertionKey, true);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to load settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves a setting to registry.
        /// </summary>
        private void SaveSetting(string name, bool value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    key?.SetValue(name, value, RegistryValueKind.DWord);
                }
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Saved setting: {name} = {value}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to save setting {name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a boolean value from registry with default fallback.
        /// </summary>
        private bool GetBoolValue(RegistryKey key, string name, bool defaultValue)
        {
            var value = key.GetValue(name);
            if (value is int intValue)
            {
                return intValue != 0;
            }
            return defaultValue;
        }

        /// <summary>
        /// Checks if auto-start is enabled.
        /// </summary>
        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(AppName);
                        return value != null;
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to check auto-start: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Sets the auto-start registry setting.
        /// </summary>
        private void SetAutoStart(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                key.SetValue(AppName, exePath);
                            }
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Auto-start {(enable ? "enabled" : "disabled")}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to set auto-start: {ex.Message}");
            }
        }

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
