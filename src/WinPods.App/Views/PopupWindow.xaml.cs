using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI;
using Windows.UI;
using WinPods.Core.Models;
using WinPods.App.Services;
using System.Numerics;

namespace WinPods.App.Views
{
    /// <summary>
    /// iOS-style popup window for displaying AirPods battery status.
    /// Auto-dismisses after 5 seconds like iOS with smooth animations.
    /// </summary>
    public sealed partial class PopupWindow : Window
    {
        private DispatcherTimer? _autoDismissTimer;
        private const int AutoDismissDelaySeconds = 5;
        private Compositor? _compositor;
        private NoiseControlService? _noiseControlService;
        private Services.AudioConnectionMonitor? _audioMonitor;
        private ulong? _bluetoothAddress;

        // Connection state
        private ConnectionState _connectionState = ConnectionState.Disconnected;

        public PopupWindow()
        {
            var constructorStopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[PopupWindow] ========== CONSTRUCTOR START ==========");

            this.InitializeComponent();
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] InitializeComponent completed");

            InitializeAutoDismissTimer();
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] Timer initialized");

            // Get compositor for animations
            _compositor = this.Compositor;

            // iOS 26 Liquid Glass: Make window borderless
            ConfigureAsBorderlessWindow();
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] Window configured as borderless");

            // Set window title (hidden but good for debugging)
            this.Title = "WinPods";

            // Set window size
            AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 1200));
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] Window resized");

            // Position in bottom-right corner (iOS 26 style)
            PositionBottomRight();
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] Positioned bottom-right");

            // Subscribe to window activation events
            this.Activated += (s, e) =>
            {
                Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] *** WINDOW ACTIVATED EVENT FIRED ***");
            };

            // Bring window to front and keep it on top
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] About to call Activate()...");
            this.Activate();
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] Activate() returned");
            EnsureTopMost();
            Console.WriteLine($"[PopupWindow] [{constructorStopwatch.ElapsedMilliseconds}ms] EnsureTopMost() completed");

            // Initialize noise control mode - default to Off
            SetNoiseControlMode(Services.NoiseControlMode.Off);

            // Initialize connection status (start as disconnected for audio)
            UpdateConnectionStatusUI(ConnectionState.Disconnected);

            // Subscribe to closed event for cleanup
            this.Closed += (s, e) =>
            {
                _autoDismissTimer?.Stop();
                _autoDismissTimer = null;
            };

            Console.WriteLine($"[PopupWindow] ========== CONSTRUCTOR END [{constructorStopwatch.ElapsedMilliseconds}ms] ==========");
        }

        /// <summary>
        /// Configures the window as borderless for iOS 26 Liquid Glass effect.
        /// </summary>
        private void ConfigureAsBorderlessWindow()
        {
            try
            {
                // Get the AppWindow's Presenter and configure as borderless
                var presenter = Microsoft.UI.Windowing.OverlappedPresenter.CreateForDialog();
                presenter.SetBorderAndTitleBar(false, false);

                // Apply the presenter to the window
                AppWindow.SetPresenter(presenter);
                System.Diagnostics.Debug.WriteLine("[PopupWindow] Borderless window configured");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Failed to configure borderless: {ex.Message}");
            }
        }

        /// <summary>
        /// Positions the window in bottom-right corner above taskbar (iOS 26 style).
        /// </summary>
        private void PositionBottomRight()
        {
            try
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    var workArea = displayArea.WorkArea;
                    var width = 800;
                    var height = 1200;
                    var margin = 16; // Distance from edges

                    // Position in bottom-right corner
                    var x = workArea.Width - width - margin;
                    var y = workArea.Height - height - margin;

                    AppWindow.Move(new Windows.Graphics.PointInt32((int)x, (int)y));
                    System.Diagnostics.Debug.WriteLine($"[PopupWindow] Positioned bottom-right at ({x}, {y})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Failed to position window: {ex.Message}");
            }
        }

        /// <summary>
        /// Connection states for the three-tier system.
        /// </summary>
        public enum ConnectionState
        {
            Disconnected,   // Gray - Not connected
            Connecting,     // Yellow - Attempting to connect
            Connected       // Green - Successfully connected
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
                    var width = 800;
                    var height = 1200;
                    var x = (workArea.Width - width) / 2;
                    var y = (workArea.Height - height) / 2;
                    AppWindow.Move(new Windows.Graphics.PointInt32((int)x, (int)y));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Failed to center window: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures the window stays on top of all other windows.
        /// </summary>
        private void EnsureTopMost()
        {
            try
            {
                // In WinUI 3, Activate() brings the window to the foreground
                // Call it multiple times to ensure it comes to front
                this.Activate();

                System.Diagnostics.Debug.WriteLine("[PopupWindow] Window brought to front");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Failed to bring to front: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the auto-dismiss timer.
        /// </summary>
        private void InitializeAutoDismissTimer()
        {
            _autoDismissTimer = new DispatcherTimer();
            _autoDismissTimer.Interval = TimeSpan.FromSeconds(AutoDismissDelaySeconds);
            _autoDismissTimer.Tick += OnAutoDismissTimerTick;
        }

        /// <summary>
        /// Handles the auto-dismiss timer tick.
        /// </summary>
        private void OnAutoDismissTimerTick(object? sender, object e)
        {
            try
            {
                _autoDismissTimer?.Stop();
                this.Close();
                System.Diagnostics.Debug.WriteLine("[PopupWindow] Auto-dismissed after 5 seconds");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Error closing window: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the popup with battery information and slide-up animation.
        /// </summary>
        public async void ShowBattery(AirPodsState state, Services.AudioConnectionMonitor? audioMonitor)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[PopupWindow] ========== ShowBattery START ==========");
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] ShowBattery called - Model: {state.ModelName}, IsConnected: {state.IsConnected}");
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] Battery - Left: {state.Battery.Left.Percentage}%, Right: {state.Battery.Right.Percentage}%, Case: {state.Battery.Case.Percentage}%");

            // Show window IMMEDIATELY - before any async operations
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] *** ABOUT TO ACTIVATE WINDOW ***");
            this.Activate();
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] Activate() called");
            EnsureTopMost();
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] EnsureTopMost() completed");

            // Animate in with slide-up effect
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] About to start animation...");
            AnimateIn();
            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] AnimateIn() returned");

            // Stop any existing timer
            _autoDismissTimer?.Stop();

            // Store audio monitor and bluetooth address
            _audioMonitor = audioMonitor;

            if (state.BluetoothAddress.HasValue)
            {
                _bluetoothAddress = state.BluetoothAddress.Value;
                Console.WriteLine($"[PopupWindow] Stored Bluetooth address: {_bluetoothAddress.Value:X12}");

                // Check current audio connection status (sync, fast)
                if (_audioMonitor != null)
                {
                    bool isConnected = _audioMonitor.IsAirPodsDefaultAudioDevice();
                    UpdateConnectionStatusUI(isConnected ? ConnectionState.Connected : ConnectionState.Disconnected);
                }
            }
            else
            {
                Console.WriteLine("[PopupWindow] Warning: No Bluetooth address available");
            }

            // Update UI with state
            UpdateBatteryDisplay(state);

            // Initialize noise control service ASYNCHRONOUSLY in background (don't block popup display)
            if (_noiseControlService == null && _bluetoothAddress.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _noiseControlService = new NoiseControlService();
                        bool connected = await _noiseControlService.ConnectAsync(_bluetoothAddress.Value);
                        Console.WriteLine($"[PopupWindow] Noise control service connection: {connected}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PopupWindow] Noise control service error: {ex.Message}");
                    }
                });
            }

            // Start auto-dismiss timer
            _autoDismissTimer?.Start();

            Console.WriteLine($"[PopupWindow] [{stopwatch.ElapsedMilliseconds}ms] Auto-dismiss timer started");
            Console.WriteLine($"[PopupWindow] ========== ShowBattery END [{stopwatch.ElapsedMilliseconds}ms] ==========");
        }

        /// <summary>
        /// Animates the window in with a slide-up effect.
        /// </summary>
        private void AnimateIn()
        {
            if (_compositor == null) return;

            try
            {
                // Get the visual for the root grid
                var visual = ElementCompositionPreview.GetElementVisual(RootGrid);
                var containerVisual = ElementCompositionPreview.GetElementVisual(this.Content);

                // Create iOS-like easing function
                var easing = _compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.25f, 0.1f),
                    new Vector2(0.25f, 1.0f)
                );

                // Create slide-up animation (move from below)
                var offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0, 80, 0)); // Start 80px below
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0, 0, 0), easing); // End at final position with easing
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(400);

                // Create scale animation (start slightly smaller, grow to full size)
                var scaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.InsertKeyFrame(0f, new Vector3(0.95f, 0.95f, 1f)); // Start at 95%
                scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), easing); // End at 100% with easing
                scaleAnimation.Duration = TimeSpan.FromMilliseconds(400);

                // Create fade animation
                var fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.InsertKeyFrame(0f, 0f);  // Start transparent
                fadeAnimation.InsertKeyFrame(1f, 1f, easing);  // End opaque with easing
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(300);

                // Apply animations to the container
                containerVisual.CenterPoint = new Vector3(400, 600, 0); // Set center point for scale (half of window size: 800/2=400, 1200/2=600)
                containerVisual.StartAnimation("Offset", offsetAnimation);
                containerVisual.StartAnimation("Scale", scaleAnimation);
                containerVisual.StartAnimation("Opacity", fadeAnimation);

                System.Diagnostics.Debug.WriteLine("[PopupWindow] Slide-up animation started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Animation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the battery display with current state.
        /// </summary>
        private void UpdateBatteryDisplay(AirPodsState state)
        {
            DeviceNameText.Text = state.ModelName;

            // Update left pod
            if (state.Battery.Left.IsAvailable)
            {
                LeftBatteryText.Text = $"{state.Battery.Left.Percentage}%";
                UpdateBatteryArc(LeftBatteryArc, state.Battery.Left.Percentage, state.Battery.Left.IsCharging);
                UpdateBatteryGlow(LeftBatteryGlow, state.Battery.Left.Percentage, state.Battery.Left.IsCharging);
                LeftChargingIcon.Visibility = state.Battery.Left.IsCharging ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                LeftBatteryText.Text = "--%";
                UpdateBatteryArc(LeftBatteryArc, 0, false);
                UpdateBatteryGlow(LeftBatteryGlow, 0, false);
                LeftChargingIcon.Visibility = Visibility.Collapsed;
            }

            // Update right pod
            if (state.Battery.Right.IsAvailable)
            {
                RightBatteryText.Text = $"{state.Battery.Right.Percentage}%";
                UpdateBatteryArc(RightBatteryArc, state.Battery.Right.Percentage, state.Battery.Right.IsCharging);
                UpdateBatteryGlow(RightBatteryGlow, state.Battery.Right.Percentage, state.Battery.Right.IsCharging);
                RightChargingIcon.Visibility = state.Battery.Right.IsCharging ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                RightBatteryText.Text = "--%";
                UpdateBatteryArc(RightBatteryArc, 0, false);
                UpdateBatteryGlow(RightBatteryGlow, 0, false);
                RightChargingIcon.Visibility = Visibility.Collapsed;
            }

            // Update case
            if (state.Battery.Case.IsAvailable)
            {
                CaseBatteryText.Text = $"{state.Battery.Case.Percentage}%";
                UpdateBatteryArc(CaseBatteryArc, state.Battery.Case.Percentage, state.Battery.Case.IsCharging);
                UpdateBatteryGlow(CaseBatteryGlow, state.Battery.Case.Percentage, state.Battery.Case.IsCharging);
                CaseChargingIcon.Visibility = state.Battery.Case.IsCharging ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                CaseBatteryText.Text = "--%";
                UpdateBatteryArc(CaseBatteryArc, 0, false);
                UpdateBatteryGlow(CaseBatteryGlow, 0, false);
                CaseChargingIcon.Visibility = Visibility.Collapsed;
            }

            // Update low battery warning banner
            UpdateLowBatteryWarning(state);
        }

        /// <summary>
        /// Updates a battery arc with animation.
        /// </summary>
        private void UpdateBatteryArc(Ellipse arc, byte percentage, bool isCharging)
        {
            // Calculate stroke dash array and offset for circular progress
            // For a 70px diameter circle: circumference = π * 70 ≈ 220
            double circumference = 220.0;

            // Set the dash array to show the progress (dash length, gap length)
            arc.StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { circumference, circumference };

            // Calculate the offset to show the correct percentage
            double offset = circumference - (circumference * percentage / 100);
            arc.StrokeDashOffset = offset;

            // Update color based on battery level and charging state
            Color color;
            if (isCharging)
            {
                color = Color.FromArgb(255, 0, 122, 255); // Blue for charging
            }
            else if (percentage <= 20)
            {
                color = Color.FromArgb(255, 255, 59, 48); // Red
            }
            else if (percentage <= 50)
            {
                color = Color.FromArgb(255, 255, 204, 0); // Yellow
            }
            else
            {
                color = Color.FromArgb(255, 48, 209, 88); // Green
            }

            arc.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        }

        /// <summary>
        /// Updates the battery glow effect for iOS 26 liquid glass design.
        /// </summary>
        private void UpdateBatteryGlow(Ellipse glow, byte percentage, bool isCharging)
        {
            // Update glow color based on battery level
            Color color;
            if (isCharging)
            {
                color = Color.FromArgb(40, 0, 122, 255); // Blue for charging (low opacity)
            }
            else if (percentage <= 20)
            {
                color = Color.FromArgb(40, 255, 59, 48); // Red (low opacity)
            }
            else if (percentage <= 50)
            {
                color = Color.FromArgb(40, 255, 204, 0); // Yellow (low opacity)
            }
            else
            {
                color = Color.FromArgb(40, 48, 209, 88); // Green (low opacity)
            }

            glow.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        }

        /// <summary>
        /// Updates the low battery warning banner.
        /// Shows warning when any battery is at 20% or below.
        /// </summary>
        private void UpdateLowBatteryWarning(AirPodsState state)
        {
            var lowBatteries = new List<string>();

            // Check for low battery (<= 20%)
            if (state.Battery.Left.IsAvailable && state.Battery.Left.Percentage <= 20)
                lowBatteries.Add($"Left ({state.Battery.Left.Percentage}%)");

            if (state.Battery.Right.IsAvailable && state.Battery.Right.Percentage <= 20)
                lowBatteries.Add($"Right ({state.Battery.Right.Percentage}%)");

            if (state.Battery.Case.IsAvailable && state.Battery.Case.Percentage <= 20)
                lowBatteries.Add($"Case ({state.Battery.Case.Percentage}%)");

            if (lowBatteries.Count > 0)
            {
                WarningText.Text = $"Charge your {state.ModelName} soon. Low battery: {string.Join(", ", lowBatteries)}";
                WarningBanner.Visibility = Visibility.Visible;
            }
            else
            {
                WarningBanner.Visibility = Visibility.Collapsed;
            }
        }

        private Services.NoiseControlMode _currentNoiseMode = Services.NoiseControlMode.Off;

        /// <summary>
        /// Handles Noise Cancellation button click.
        /// </summary>
        private void OnNoiseCancellationClick(object sender, RoutedEventArgs e)
        {
            SetNoiseControlMode(Services.NoiseControlMode.NoiseCancellation);
            System.Diagnostics.Debug.WriteLine("[PopupWindow] Noise Cancellation selected");
        }

        /// <summary>
        /// Handles Transparency button click.
        /// </summary>
        private void OnTransparencyClick(object sender, RoutedEventArgs e)
        {
            SetNoiseControlMode(Services.NoiseControlMode.Transparency);
            System.Diagnostics.Debug.WriteLine("[PopupWindow] Transparency selected");
        }

        /// <summary>
        /// Handles Off button click.
        /// </summary>
        private void OnNoiseOffClick(object sender, RoutedEventArgs e)
        {
            SetNoiseControlMode(Services.NoiseControlMode.Off);
            System.Diagnostics.Debug.WriteLine("[PopupWindow] Noise Control Off selected");
        }

        /// <summary>
        /// Sets the noise control mode and updates button appearance.
        /// </summary>
        private void SetNoiseControlMode(Services.NoiseControlMode mode)
        {
            _currentNoiseMode = mode;

            // Reset all buttons to inactive state
            var inactiveBackground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 44, 44, 46)); // #2C2C2E
            var inactiveForeground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 142, 142, 147)); // #8E8E93
            var activeBackground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0, 122, 255)); // #007AFF
            var activeForeground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 255, 255, 255)); // White

            // Reset all buttons
            NoiseCancellationButton.Background = inactiveBackground;
            NoiseCancellationButton.Foreground = inactiveForeground;
            TransparencyButton.Background = inactiveBackground;
            TransparencyButton.Foreground = inactiveForeground;
            NoiseOffButton.Background = inactiveBackground;
            NoiseOffButton.Foreground = inactiveForeground;

            // Highlight active button
            switch (mode)
            {
                case Services.NoiseControlMode.NoiseCancellation:
                    NoiseCancellationButton.Background = activeBackground;
                    NoiseCancellationButton.Foreground = activeForeground;
                    break;
                case Services.NoiseControlMode.Transparency:
                    TransparencyButton.Background = activeBackground;
                    TransparencyButton.Foreground = activeForeground;
                    break;
                case Services.NoiseControlMode.Off:
                    NoiseOffButton.Background = activeBackground;
                    NoiseOffButton.Foreground = activeForeground;
                    break;
            }

            // Send command to AirPods via BLE GATT
            SendNoiseControlCommand(mode);
        }

        /// <summary>
        /// Sends noise control command to AirPods.
        /// </summary>
        private async void SendNoiseControlCommand(Services.NoiseControlMode mode)
        {
            if (_noiseControlService != null)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Sending noise control command: {mode}");
                bool success = await _noiseControlService.SetModeAsync(mode);
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Noise control command result: {success}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[PopupWindow] Noise control service not available");
            }
        }

        /// <summary>
        /// Updates the connection status UI (shows audio connection state).
        /// </summary>
        private void UpdateConnectionStatusUI(ConnectionState state)
        {
            _connectionState = state;

            switch (state)
            {
                case ConnectionState.Connected:
                    ConnectionStatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 209, 88)); // Green
                    ConnectionStatusText.Text = "Connected";
                    ConnectionStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 209, 88));
                    ConnectButton.Visibility = Visibility.Collapsed; // Hide button when connected
                    break;

                case ConnectionState.Connecting:
                    ConnectionStatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 0)); // Yellow
                    ConnectionStatusText.Text = "Connecting...";
                    ConnectionStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 0));
                    ConnectButton.Visibility = Visibility.Collapsed; // Hide button while connecting
                    break;

                case ConnectionState.Disconnected:
                default:
                    ConnectionStatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 58, 60)); // Gray
                    ConnectionStatusText.Text = "Not Connected";
                    ConnectionStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 142, 142, 147));
                    ConnectButton.Visibility = Visibility.Visible; // Show manual connect button
                    break;
            }
        }

        /// <summary>
        /// Sets the connection state from external code (e.g., from App.xaml.cs during auto-connect).
        /// </summary>
        public void SetConnectionState(ConnectionState state)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateConnectionStatusUI(state);
            });
        }

        /// <summary>
        /// Handles the Open Bluetooth Settings button click.
        /// </summary>
        private async void OnConnectButtonClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[PopupWindow] Opening Bluetooth Settings...");

            try
            {
                // Open Bluetooth Settings
                bool success = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("[PopupWindow] ✓ Bluetooth Settings opened");

                    // Start monitoring for connection in background
                    if (_audioMonitor != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            System.Diagnostics.Debug.WriteLine("[PopupWindow] Monitoring for audio connection (30s timeout)...");

                            bool connected = await _audioMonitor.WaitForAudioConnectionAsync(timeoutSeconds: 30);

                            if (connected)
                            {
                                System.Diagnostics.Debug.WriteLine("[PopupWindow] ✓ User connected AirPods via Settings!");

                                // Update UI on main thread
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    SetConnectionState(ConnectionState.Connected);

                                    // Show success toast
                                    // TODO: Add toast notification service

                                    // Auto-dismiss after 2 seconds
                                    _ = Task.Run(async () =>
                                    {
                                        await Task.Delay(2000);
                                        this.DispatcherQueue.TryEnqueue(() =>
                                        {
                                            try
                                            {
                                                Close();
                                            }
                                            catch { }
                                        });
                                    });
                                });
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("[PopupWindow] User didn't connect within timeout");
                            }
                        });
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[PopupWindow] ❌ Failed to open Bluetooth Settings");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupWindow] Exception opening Settings: {ex.Message}");
            }
        }
    }
}
