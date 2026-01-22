using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Control;
using WinPods.Core.StateManagement;
using WinPods.Core.Models;
using WinPods.Core.Services;
using WinPods.App.Views;
using WinPods.App.Services;
using System.IO;
using System.Text;

namespace WinPods.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private DeviceManager? _deviceManager;
        private TrayIconService? _trayIconService;
        private PopupWindow? _popupWindow;
        private AirPodsState? _lastState;
        private bool _isPopupVisible;

        // Media control and services
        private MediaController? _mediaController;
        private GlobalHotkeyService? _globalHotkeyService;
        private NoiseControlService? _noiseControlService;
        private Services.ConnectionTriggerService? _connectionTriggerService;
        private Services.AudioConnectionMonitor? _audioConnectionMonitor;
        private readonly SettingsService _settings = SettingsService.Instance;

        // Lid counter tracking for auto-connect
        private byte? _lastLidCount;

        // Synchronization for state changes (prevents race conditions in auto-connect)
        private int _stateChangeInProgress = 0;

        // File logging
        private StreamWriter? _logWriter;
        private readonly string _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinPods",
            "winpods_debug.log"
        );

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            // Initialize file logging
            InitializeLogging();

            // Subscribe to unhandled exceptions
            this.UnhandledException += OnUnhandledException;

            // Log startup
            Log("[App] WinPods starting...");
            Log("[App] Please open your AirPods case near your PC...");
            Log("");
        }

        /// <summary>
        /// Initializes file logging to LocalAppData\WinPods\winpods_debug.log
        /// </summary>
        private void InitializeLogging()
        {
            try
            {
                string logDir = Path.GetDirectoryName(_logFilePath)!;
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // Clear previous log file
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }

                _logWriter = new StreamWriter(_logFilePath) { AutoFlush = true };
                _logWriter.WriteLine($"=== WinPods Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _logWriter.WriteLine($"Log file: {_logFilePath}");
                _logWriter.WriteLine("==========================================================");
                _logWriter.WriteLine();

                // Redirect ALL Console.WriteLine to our log file
                var writer = new TextWriterWithTimestamp(_logWriter);
                Console.SetOut(writer);
                Console.SetError(writer);

                Console.WriteLine($"[App] Logging to file: {_logFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Failed to initialize file logging: {ex.Message}");
            }
        }

        /// <summary>
        /// Custom TextWriter that adds timestamps to each line.
        /// </summary>
        private class TextWriterWithTimestamp : TextWriter
        {
            private readonly TextWriter _baseWriter;

            public TextWriterWithTimestamp(TextWriter baseWriter)
            {
                _baseWriter = baseWriter;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                _baseWriter.WriteLine($"[{timestamp}] {value}");
            }

            public override void Write(string? value)
            {
                _baseWriter.Write(value);
            }
        }

        /// <summary>
        /// Writes a message to both console and log file.
        /// </summary>
        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logMessage = $"[{timestamp}] {message}";

            Console.WriteLine(logMessage);

            try
            {
                _logWriter?.WriteLine(logMessage);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        /// <summary>
        /// Writes a formatted message to both console and log file.
        /// </summary>
        private void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            _window = new Window();
            _window.Content = new Frame();

            // Initialize core components
            InitializeDeviceManager();
            InitializeTrayIcon();
            await InitializeServicesAsync();

            // Hide main window - app runs from system tray
            // _window.Activate();  // Commented out - no main window needed
        }

        /// <summary>
        /// Initializes the AirPods device manager.
        /// </summary>
        private void InitializeDeviceManager()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[App] Initializing DeviceManager...");
                _deviceManager = new DeviceManager();

                // Subscribe to state changes
                _deviceManager.StateChanged += OnAirPodsStateChanged;
                _deviceManager.LidOpened += OnAirPodsLidOpened;

                // Start scanning for AirPods
                _deviceManager.Initialize();
                System.Diagnostics.Debug.WriteLine("[App] DeviceManager initialized and scanning started!");
            }
            catch (Exception ex)
            {
                // Log error but continue - app can still show tray icon
                System.Diagnostics.Debug.WriteLine($"[App] ERROR: Failed to initialize DeviceManager: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Initializes the system tray icon.
        /// </summary>
        private void InitializeTrayIcon()
        {
            try
            {
                _trayIconService = new TrayIconService();

                // Subscribe to tray events
                _trayIconService.TrayIconClicked += OnTrayIconClicked;
                _trayIconService.SettingsRequested += OnSettingsRequested;
                _trayIconService.ExitRequested += OnExitRequested;

                // Initialize tray icon - pass null for now since we don't have a visible window
                // The tray icon will work without XamlRoot for basic functionality
                _trayIconService.Initialize(null);

                System.Diagnostics.Debug.WriteLine("[App] TrayIconService initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Failed to initialize TrayIconService: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Wire up services to TrayIconService after they are initialized.
        /// </summary>
        private void WireUpTrayIconServices()
        {
            try
            {
                if (_trayIconService != null)
                {
                    _trayIconService.SetMediaController(_mediaController);
                    _trayIconService.SetNoiseControlService(_noiseControlService);
                    Log("[App] TrayIconService wired up with MediaController and NoiseControlService");
                }
            }
            catch (Exception ex)
            {
                Log($"[App] Failed to wire up TrayIconService: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the media controller, global hotkeys, and other services.
        /// </summary>
        private async System.Threading.Tasks.Task InitializeServicesAsync()
        {
            try
            {
                Console.WriteLine("[App] Initializing services...");

                // Always create media controller (needed for hotkeys and tray menu)
                _mediaController = new MediaController();
                await _mediaController.InitializeAsync();

                // Create noise control service (needed for tray menu)
                _noiseControlService = new NoiseControlService();

                // Create auto-connect services
                _connectionTriggerService = new Services.ConnectionTriggerService();
                _audioConnectionMonitor = new Services.AudioConnectionMonitor();
                Console.WriteLine("[App] Auto-connect services created");

                // Wire up services to tray icon
                WireUpTrayIconServices();

                // Initialize global hotkeys
                Console.WriteLine("[App] Initializing Global Hotkeys...");
                _globalHotkeyService = new GlobalHotkeyService();

                // Register Ctrl+Alt+P for Play/Pause toggle
                // P key = 0x50
                bool registered = _globalHotkeyService.RegisterHotkey(0x50, GlobalHotkeyService.Modifiers.Control | GlobalHotkeyService.Modifiers.Alt, async () =>
                {
                    Console.WriteLine("[GlobalHotkey] Ctrl+Alt+P pressed - Toggling play/pause");
                    await TogglePlayPauseAsync();
                });

                if (registered)
                {
                    Console.WriteLine("[App] Registered hotkey: Ctrl+Alt+P (Play/Pause toggle)");
                }
                else
                {
                    Console.WriteLine("[App] Failed to register Ctrl+Alt+P hotkey");
                }

                // Register Ctrl+Alt+N for Noise Control popup
                // N key = 0x4E
                registered = _globalHotkeyService.RegisterHotkey(0x4E, GlobalHotkeyService.Modifiers.Control | GlobalHotkeyService.Modifiers.Alt, () =>
                {
                    Console.WriteLine("[GlobalHotkey] Ctrl+Alt+N pressed - Showing noise control popup");
                    ShowNoiseControlPopup();
                });

                if (registered)
                {
                    Console.WriteLine("[App] Registered hotkey: Ctrl+Alt+N (Noise control)");
                }

                Console.WriteLine("[App] Services initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Failed to initialize services: {ex.Message}");
                Console.WriteLine($"[App] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Handles AirPods state changes with three-tier auto-connect system.
        /// </summary>
        private async void OnAirPodsStateChanged(object? sender, AirPodsState state)
        {
            // Prevent reentrancy - if a state change is already being processed, skip this one
            if (System.Threading.Interlocked.CompareExchange(ref _stateChangeInProgress, 1, 0) != 0)
            {
                Log($"[App] ⚠️ State change already in progress, skipping this event");
                return;
            }

            try
            {
                Log($"[App] OnAirPodsStateChanged called - IsConnected: {state.IsConnected}, LidOpenCount: {state.LidOpenCount}");

                // Update tray icon with new state
                _trayIconService?.UpdateBattery(state);

                // Update Bluetooth address in tray icon for noise control
                if (state.BluetoothAddress.HasValue)
                {
                    _trayIconService?.SetBluetoothAddress(state.BluetoothAddress.Value);
                }

                _lastState = state;

                // THREE-TIER AUTO-CONNECT SYSTEM
                // Check if this is a new lid-open event (case just opened)
                // MUST HAPPEN FIRST - before any blocking await calls!
                if (state.LidOpenCount != _lastLidCount && state.BluetoothAddress.HasValue)
                {
                    _lastLidCount = state.LidOpenCount;
                    Log($"[App] ========== CASE OPEN DETECTED (Lid Count: {state.LidOpenCount}) ==========");

                    // FIRST: Check if AirPods are already connected (instant check, no async)
                    bool alreadyConnected = _audioConnectionMonitor?.IsAirPodsDefaultAudioDevice() ?? false;
                    Log($"[App] Initial audio check: AirPods connected = {alreadyConnected}");

                    if (alreadyConnected)
                    {
                        // Already connected! Show popup with "Connected" status immediately, skip Tiers 1 & 2
                        Log("[App] AirPods already connected - showing Connected popup immediately");
                        if (!_isPopupVisible)
                        {
                            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                            {
                                ShowAirPodsPopup(state);
                                _popupWindow?.SetConnectionState(PopupWindow.ConnectionState.Connected);

                                // Auto-dismiss after 2 seconds
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(2000);
                                    _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                                    {
                                        try
                                        {
                                            _popupWindow?.Close();
                                        }
                                        catch { }
                                    });
                                });
                            });
                        }
                    }
                    else
                    {
                        // Not connected - run three-tier system
                        Log("[App] AirPods not connected - running three-tier system");

                        // Show popup immediately with "Connecting..." status
                        if (!_isPopupVisible)
                        {
                            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                            {
                                ShowAirPodsPopup(state);
                                // Set popup to Connecting state
                                _popupWindow?.SetConnectionState(PopupWindow.ConnectionState.Connecting);
                            });
                        }

                        // TIER 1: Try to trigger Windows Bluetooth connection
                        if (_connectionTriggerService != null)
                        {
                            try
                            {
                                Log("[App] Tier 1: Triggering Windows Bluetooth connection...");
                                bool triggered = await _connectionTriggerService.TryTriggerConnectionAsync(state.BluetoothAddress.Value);
                                Log($"[App] Tier 1 complete: {triggered}");
                            }
                            catch (Exception ex)
                            {
                                Log($"[App] Tier 1 exception: {ex.Message}");
                                Log($"[App] Tier 1 stack trace: {ex.StackTrace}");
                            }
                        }

                        // TIER 2: Wait for audio connection with timeout
                        if (_audioConnectionMonitor != null)
                        {
                            Log("[App] Tier 2: Waiting for audio connection (8s timeout)...");
                            bool connected = await _audioConnectionMonitor.WaitForAudioConnectionAsync(timeoutSeconds: 8);

                            if (connected)
                            {
                                // SUCCESS! Auto-connected
                                Log("[App] ✓✓✓ SUCCESS - AirPods auto-connected!");
                                _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                                {
                                    _popupWindow?.SetConnectionState(PopupWindow.ConnectionState.Connected);

                                    // Show toast notification
                                    if (_lastState != null)
                                    {
                                        string batteryInfo = $"L: {_lastState.Battery.Left.Percentage}% R: {_lastState.Battery.Right.Percentage}% C: {_lastState.Battery.Case.Percentage}%";
                                        _ = _trayIconService?.ShowNotificationAsync("AirPods Connected", batteryInfo);
                                    }

                                    // Auto-dismiss after 2 seconds
                                    _ = Task.Run(async () =>
                                    {
                                        await Task.Delay(2000);
                                        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                                        {
                                            try
                                            {
                                                _popupWindow?.Close();
                                            }
                                            catch { }
                                        });
                                    });
                                });
                            }
                            else
                            {
                                // TIER 3: Timeout - show manual connect button
                                Log("[App] Tier 2 timeout - Showing manual connect option");
                                _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                                {
                                    _popupWindow?.SetConnectionState(PopupWindow.ConnectionState.Disconnected);
                                });
                            }
                        }
                    }
                }

                // NOW DO NON-CRITICAL BACKGROUND TASKS (after popup is shown)

                // Connect noise control service if not already connected (runs in background, doesn't block popup)
                if (state.BluetoothAddress.HasValue && _noiseControlService != null && !_noiseControlService.IsConnected)
                {
                    Log($"[App] Attempting to connect NoiseControlService to Bluetooth address: {state.BluetoothAddress.Value:X12}");
                    bool connected = await _noiseControlService.ConnectAsync(state.BluetoothAddress.Value);
                    Log($"[App] Noise control service connection result: {connected}");
                    if (!connected)
                    {
                        Log($"[App] WARNING: Noise control not available - GATT characteristics not accessible on Windows");
                    }
                }

                // Check for low battery and show notification
                if (state.IsConnected && state.Battery.IsLowBattery)
                {
                    Console.WriteLine("[App] Low battery detected! Calling ShowLowBatteryNotificationAsync...");
                    await _trayIconService?.ShowLowBatteryNotificationAsync(state);
                }
            }
            finally
            {
                // Reset the in-progress flag to allow next state change
                System.Threading.Interlocked.Exchange(ref _stateChangeInProgress, 0);
            }
        }

        /// <summary>
        /// Handles AirPods lid opened events.
        /// </summary>
        private void OnAirPodsLidOpened(object? sender, EventArgs e)
        {
            // Show popup when case lid is opened, but NOT if already visible
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
            {
                if (_lastState != null && _lastState.IsConnected && !_isPopupVisible)
                {
                    ShowAirPodsPopup(_lastState);
                }
            });
        }

        /// <summary>
        /// Toggles play/pause for media playback.
        /// </summary>
        private async Task TogglePlayPauseAsync()
        {
            try
            {
                if (_mediaController != null)
                {
                    var status = await _mediaController.GetPlaybackStatusAsync();
                    if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        await _mediaController.PauseAsync();
                        Console.WriteLine("[App] Toggled to PAUSED");
                    }
                    else
                    {
                        await _mediaController.ResumeAsync();
                        Console.WriteLine("[App] Toggled to PLAYING");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Error toggling play/pause: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the popup with noise control buttons.
        /// </summary>
        private void ShowNoiseControlPopup()
        {
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
            {
                if (_lastState != null && _lastState.IsConnected)
                {
                    ShowAirPodsPopup(_lastState);
                }
                else
                {
                    ShowNotification("WinPods", "AirPods not connected. Open the case near your PC.");
                }
            });
        }

        /// <summary>
        /// Shows the AirPods battery popup.
        /// </summary>
        private void ShowAirPodsPopup(AirPodsState state)
        {
            try
            {
                // Close existing popup if open
                if (_popupWindow != null)
                {
                    try
                    {
                        _popupWindow.Close();
                    }
                    catch
                    {
                        // Ignore if already closed
                    }
                    _popupWindow = null;
                    _isPopupVisible = false;
                }

                // Create and show new popup
                _popupWindow = new PopupWindow();

                // Subscribe to closed event to track visibility
                _popupWindow.Closed += (s, e) =>
                {
                    _isPopupVisible = false;
                    Console.WriteLine("[App] Popup closed, _isPopupVisible set to false");
                };

                // Show battery popup
                _popupWindow.ShowBattery(state, _audioConnectionMonitor);
                _isPopupVisible = true;
                Console.WriteLine("[App] Popup shown, _isPopupVisible set to true");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show popup: {ex.Message}");
                _isPopupVisible = false;
            }
        }

        /// <summary>
        /// Handles tray icon click events.
        /// </summary>
        private void OnTrayIconClicked(object? sender, EventArgs e)
        {
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
            {
                if (_lastState != null && _lastState.IsConnected)
                {
                    ShowAirPodsPopup(_lastState);
                }
                else
                {
                    // Show "not connected" message or settings
                    ShowNotification("WinPods", "AirPods not detected. Please open the case near your PC.");
                }
            });
        }

        /// <summary>
        /// Handles settings requests.
        /// </summary>
        private void OnSettingsRequested(object? sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("[App] Settings requested, attempting to open window...");

                // Create settings window on the UI thread
                if (_window?.DispatcherQueue != null)
                {
                    _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                    {
                        try
                        {
                            var settingsWindow = new SettingsWindow();
                            settingsWindow.Activate();
                            Console.WriteLine("[App] Settings window opened successfully");
                            System.Diagnostics.Debug.WriteLine("[App] Settings window opened");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[App] Failed to open settings window: {ex.Message}");
                            Console.WriteLine($"[App] Stack trace: {ex.StackTrace}");
                            System.Diagnostics.Debug.WriteLine($"[App] Failed to open settings: {ex.Message}");
                        }
                    });
                }
                else
                {
                    Console.WriteLine("[App] Error: DispatcherQueue is null");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Exception in OnSettingsRequested: {ex.Message}");
                Console.WriteLine($"[App] Stack trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[App] Failed to open settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles exit requests.
        /// </summary>
        private void OnExitRequested(object? sender, EventArgs e)
        {
            // Clean up and exit
            Shutdown();
        }

        /// <summary>
        /// Shows a notification using the tray icon.
        /// </summary>
        private async void ShowNotification(string title, string message)
        {
            try
            {
                if (_trayIconService != null)
                {
                    await _trayIconService.ShowNotificationAsync(title, message);
                }
                else
                {
                    // Fallback to debug output
                    System.Diagnostics.Debug.WriteLine($"{title}: {message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles unhandled exceptions.
        /// </summary>
        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Log exception
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Message}");
            e.Handled = true;
        }

        /// <summary>
        /// Shuts down the application gracefully.
        /// </summary>
        public void Shutdown()
        {
            try
            {
                Log("[App] Shutting down...");

                // Stop device manager
                _deviceManager?.Shutdown();

                // Dispose global hotkey service
                _globalHotkeyService?.Dispose();

                // Dispose noise control service
                _noiseControlService?.Dispose();

                // Dispose tray icon
                _trayIconService?.Dispose();

                // Close popup
                _popupWindow?.Close();

                // Close log file
                try
                {
                    _logWriter?.Close();
                    _logWriter?.Dispose();
                    Log("[App] Log file closed successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] Error closing log file: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex.Message}");
            }

            // Exit application
            Exit();
        }
    }
}
