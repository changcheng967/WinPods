using System.Runtime.InteropServices;
using WinPods.Core.Models;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using System.Collections.Generic;
using Windows.Graphics;
using Microsoft.UI.Dispatching;
using System.Threading;
using System.Reflection;
using Windows.Media.Control;

namespace WinPods.App.Services
{
    /// <summary>
    /// System tray icon service using native Windows Shell APIs.
    /// Shows battery percentage in tooltip and provides context menu.
    /// </summary>
    public class TrayIconService : IDisposable
    {
        #region Native Methods and Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA pnid);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 100;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_DESTROY = 0x0002;
        private const int WM_COMMAND = 0x0111;
        private const uint WM_QUIT = 0x0012;
        private const int GWLP_WNDPROC = -4;
        private const int WS_OVERLAPPED = 0x00000000;

        #endregion

        private bool _isDisposed;
        private IntPtr _windowHandle;
        private IntPtr _iconHandle;
        private int _trayIconId = 1;
        private AirPodsState? _lastState;
        private string _currentTooltip = "WinPods - AirPods not connected";
        private WndProcDelegate? _wndProcDelegate;
        private Thread? _messageThread;
        private bool _messageLoopRunning;
        private MediaController? _mediaController;
        private NoiseControlService? _noiseControlService;
        private ulong? _bluetoothAddress;

        /// <summary>
        /// Event raised when the tray icon is clicked.
        /// </summary>
        public event EventHandler? TrayIconClicked;

        /// <summary>
        /// Event raised when settings is requested.
        /// </summary>
        public event EventHandler? SettingsRequested;

        /// <summary>
        /// Event raised when exit is requested.
        /// </summary>
        public event EventHandler? ExitRequested;

        /// <summary>
        /// Gets or sets whether a device is connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Initializes the system tray icon.
        /// </summary>
        public void Initialize(XamlRoot? xamlRoot)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TrayIconService));
            }

            // Create a message-only window on a background thread
            _messageLoopRunning = true;
            _messageThread = new Thread(MessageLoopThread)
            {
                IsBackground = true,
                Name = "TrayIconMessageLoop"
            };
            _messageThread.Start();

            // Wait for the window to be created
            Thread.Sleep(500);

            // Load system icon
            _iconHandle = LoadSystemIcon();

            // Add the tray icon
            AddTrayIcon();

            Console.WriteLine("[TrayIconService] System tray icon initialized (native Shell API with message pump)");
        }

        /// <summary>
        /// Message loop thread - creates window and processes messages.
        /// </summary>
        private void MessageLoopThread()
        {
            try
            {
                // Create a hidden message window
                _wndProcDelegate = new WndProcDelegate(WindowProc);
                IntPtr hInstance = GetModuleHandle(null);

                _windowHandle = CreateWindowEx(
                    0,
                    "Message",
                    "WinPodsTrayIcon",
                    0,
                    0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero
                );

                if (_windowHandle == IntPtr.Zero)
                {
                    Console.WriteLine("[TrayIconService] Failed to create message window");
                    return;
                }

                // Subclass the window to use our WndProc
                IntPtr originalProc = SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
                Console.WriteLine($"[TrayIconService] Message window created, subclassed with custom WndProc");

                // Message loop
                MSG msg;
                while (_messageLoopRunning)
                {
                    bool result = GetMessage(out msg, IntPtr.Zero, 0, 0);

                    if (!result)
                    {
                        // WM_QUIT received
                        break;
                    }

                    // Log messages for debugging
                    if (msg.message == WM_TRAYICON)
                    {
                        uint mouseMsg = (uint)msg.lParam.ToInt32();
                        Console.WriteLine($"[TrayIconService] WM_TRAYICON received, mouse msg: {mouseMsg} (LBUTTON={WM_LBUTTONDOWN}, RBUTTON={WM_RBUTTONDOWN})");
                    }

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                // Restore original window procedure
                SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, originalProc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrayIconService] Message loop error: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a system icon.
        /// </summary>
        private IntPtr LoadSystemIcon()
        {
            // Load the application icon (IDI_APPLICATION)
            IntPtr hInstance = GetModuleHandle(null);
            IntPtr hIcon = LoadIcon(hInstance, IntPtr.Zero);

            return hIcon;
        }

        /// <summary>
        /// Window procedure for handling tray icon messages.
        /// </summary>
        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                uint mouseMsg = (uint)lParam.ToInt32();

                if (mouseMsg == WM_LBUTTONDOWN)
                {
                    // Left click - show popup
                    TrayIconClicked?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;
                }
                else if (mouseMsg == WM_RBUTTONDOWN)
                {
                    // Right click - show context menu
                    ShowContextMenu();
                    return IntPtr.Zero;
                }
            }
            else if (msg == WM_DESTROY)
            {
                // Clean up
                RemoveTrayIcon();
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// Shows the context menu.
        /// </summary>
        private void ShowContextMenu()
        {
            IntPtr hMenu = CreatePopupMenu();

            if (hMenu == IntPtr.Zero)
            {
                Console.WriteLine("[TrayIconService] Failed to create popup menu");
                return;
            }

            // Create Noise Control submenu (disabled on Windows - GATT not accessible)
            IntPtr hNoiseMenu = CreatePopupMenu();
            AppendMenu(hNoiseMenu, MF_STRING | MF_GRAYED, MENU_NOISE_ANC, "Noise Cancellation (Windows limitation)");
            AppendMenu(hNoiseMenu, MF_STRING | MF_GRAYED, MENU_NOISE_TRANSPARENCY, "Transparency (Windows limitation)");
            AppendMenu(hNoiseMenu, MF_STRING | MF_GRAYED, MENU_NOISE_OFF, "Off (Windows limitation)");

            // Add menu items in order
            AppendMenu(hMenu, MF_STRING, MENU_PLAY_PAUSE, "Play/Pause Media");
            AppendMenu(hMenu, MF_SEPARATOR, 0, null);
            InsertMenu(hMenu, 2, MF_BYPOSITION | MF_POPUP, (uint)hNoiseMenu.ToInt32(), "Noise Control");
            AppendMenu(hMenu, MF_SEPARATOR, 0, null);
            AppendMenu(hMenu, MF_STRING, MENU_SHOW_POPUP, "Show Popup");
            AppendMenu(hMenu, MF_STRING, MENU_SETTINGS, "Settings");
            AppendMenu(hMenu, MF_SEPARATOR, 0, null);
            AppendMenu(hMenu, MF_STRING, MENU_EXIT, "Exit");

            // Get cursor position
            POINT cursorPos = new POINT();
            GetCursorPos(ref cursorPos);

            // Show the menu and get the selection
            int result = TrackPopupMenu(
                hMenu,
                TPM_LEFTALIGN | TPM_BOTTOMALIGN | 0x0100, // TPM_RETURNCMD
                cursorPos.x,
                cursorPos.y,
                0,
                _windowHandle,
                IntPtr.Zero
            );

            // Handle the menu selection
            uint menuId = (uint)result;

            if (menuId == MENU_SHOW_POPUP)
            {
                TrayIconClicked?.Invoke(this, EventArgs.Empty);
                Console.WriteLine("[TrayIconService] Menu: Show Popup clicked");
            }
            else if (menuId == MENU_SETTINGS)
            {
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                Console.WriteLine("[TrayIconService] Menu: Settings clicked");
            }
            else if (menuId == MENU_EXIT)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
                Console.WriteLine("[TrayIconService] Menu: Exit clicked");
            }
            else if (menuId == MENU_PLAY_PAUSE)
            {
                OnPlayPauseClick();
                Console.WriteLine("[TrayIconService] Menu: Play/Pause clicked");
            }
            else if (menuId == MENU_NOISE_ANC)
            {
                OnNoiseControlClick(NoiseControlMode.NoiseCancellation);
                Console.WriteLine("[TrayIconService] Menu: Noise Cancellation clicked");
            }
            else if (menuId == MENU_NOISE_TRANSPARENCY)
            {
                OnNoiseControlClick(NoiseControlMode.Transparency);
                Console.WriteLine("[TrayIconService] Menu: Transparency clicked");
            }
            else if (menuId == MENU_NOISE_OFF)
            {
                OnNoiseControlClick(NoiseControlMode.Off);
                Console.WriteLine("[TrayIconService] Menu: Noise Off clicked");
            }
        }

        /// <summary>
        /// Handles Play/Pause menu click.
        /// </summary>
        private async void OnPlayPauseClick()
        {
            if (_mediaController != null)
            {
                try
                {
                    var status = await _mediaController.GetPlaybackStatusAsync();
                    if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        await _mediaController.PauseAsync();
                        Console.WriteLine("[TrayIconService] Paused media playback");
                    }
                    else
                    {
                        await _mediaController.ResumeAsync();
                        Console.WriteLine("[TrayIconService] Resumed media playback");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TrayIconService] Error toggling playback: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[TrayIconService] MediaController not available");
            }
        }

        /// <summary>
        /// Handles Noise Control menu click.
        /// </summary>
        private async void OnNoiseControlClick(NoiseControlMode mode)
        {
            // Show notification explaining Windows limitation
            await ShowNotificationAsync("Noise Control Unavailable",
                "Noise control requires kernel-level Bluetooth access. This is a Windows limitation, not a bug.\n\n" +
                "Consider using MagicPods (requires kernel driver) or use your iPhone to change noise control modes.");
        }

        /// <summary>
        /// Sets the MediaController for play/pause functionality.
        /// </summary>
        public void SetMediaController(MediaController? mediaController)
        {
            _mediaController = mediaController;
            Console.WriteLine("[TrayIconService] MediaController set");
        }

        /// <summary>
        /// Sets the NoiseControlService for noise control functionality.
        /// </summary>
        public void SetNoiseControlService(NoiseControlService? noiseControlService)
        {
            _noiseControlService = noiseControlService;
            Console.WriteLine("[TrayIconService] NoiseControlService set");
        }

        /// <summary>
        /// Sets the Bluetooth address for GATT connection.
        /// </summary>
        public void SetBluetoothAddress(ulong? bluetoothAddress)
        {
            _bluetoothAddress = bluetoothAddress;
            if (bluetoothAddress.HasValue)
            {
                Console.WriteLine($"[TrayIconService] Bluetooth address set: {bluetoothAddress.Value:X12}");
            }
        }

        /// <summary>
        /// Adds the tray icon to the system tray.
        /// </summary>
        private void AddTrayIcon()
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = _trayIconId,
                uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _iconHandle,
                szTip = _currentTooltip
            };

            Shell_NotifyIcon(NIM_ADD, ref nid);
        }

        /// <summary>
        /// Removes the tray icon.
        /// </summary>
        private void RemoveTrayIcon()
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = _trayIconId
            };

            Shell_NotifyIcon(NIM_DELETE, ref nid);
        }

        /// <summary>
        /// Updates the tray icon tooltip.
        /// </summary>
        private void UpdateTooltip()
        {
            if (_windowHandle == IntPtr.Zero)
                return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = _trayIconId,
                uFlags = NIF_TIP,
                szTip = _currentTooltip
            };

            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        /// <summary>
        /// Updates the tray icon with battery information.
        /// </summary>
        public void UpdateBattery(AirPodsState? state)
        {
            if (_isDisposed)
            {
                return;
            }

            _lastState = state;

            if (state == null || !state.IsConnected)
            {
                IsConnected = false;
                _currentTooltip = "WinPods - AirPods not connected";
            }
            else
            {
                IsConnected = true;

                // Build tooltip text with battery levels
                string left = state.Battery.Left.IsAvailable ? $"L: {state.Battery.Left.Percentage}%" : "L: --%";
                string right = state.Battery.Right.IsAvailable ? $"R: {state.Battery.Right.Percentage}%" : "R: --%";
                string case_ = state.Battery.Case.IsAvailable ? $"C: {state.Battery.Case.Percentage}%" : "C: --%";

                _currentTooltip = $"{state.ModelName}\n{left} {right} {case_}";
            }

            UpdateTooltip();

            Console.WriteLine($"[TrayIconService] Updated tooltip: {_currentTooltip.Replace("\n", " | ")}");
        }

        /// <summary>
        /// Shows a notification balloon.
        /// </summary>
        public async Task ShowNotificationAsync(string title, string message)
        {
            if (_isDisposed || _windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _windowHandle,
                    uID = _trayIconId,
                    uFlags = NIF_INFO,
                    szInfo = message,
                    szInfoTitle = title,
                    dwInfoFlags = 0x00000001 // NIIF_INFO
                };

                Shell_NotifyIcon(NIM_MODIFY, ref nid);
                Console.WriteLine($"[TrayIconService] Balloon notification: {title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrayIconService] Failed to show notification - {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Shows low battery notification.
        /// </summary>
        public async Task ShowLowBatteryNotificationAsync(AirPodsState state)
        {
            Console.WriteLine($"[TrayIconService] ShowLowBatteryNotificationAsync called - Model: {state?.ModelName}, IsConnected: {state?.IsConnected}");

            if (_isDisposed || state == null || !state.IsConnected)
            {
                return;
            }

            var lowBatteries = new List<string>();
            byte threshold = 20;

            if (state.Battery.Left.IsAvailable && state.Battery.Left.Percentage <= threshold)
                lowBatteries.Add($"Left ({state.Battery.Left.Percentage}%)");

            if (state.Battery.Right.IsAvailable && state.Battery.Right.Percentage <= threshold)
                lowBatteries.Add($"Right ({state.Battery.Right.Percentage}%)");

            if (state.Battery.Case.IsAvailable && state.Battery.Case.Percentage <= threshold)
                lowBatteries.Add($"Case ({state.Battery.Case.Percentage}%)");

            if (lowBatteries.Count > 0)
            {
                string title = "Low Battery Warning";
                string message = $"Low battery on {state.ModelName}: {string.Join(", ", lowBatteries)}";
                await ShowNotificationAsync(title, message);
            }
        }

        /// <summary>
        /// Disposes the tray icon service.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                // Stop message loop
                _messageLoopRunning = false;

                // Post a quit message to unblock GetMessage
                if (_windowHandle != IntPtr.Zero)
                {
                    PostMessage(_windowHandle, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                }

                // Wait for message thread to exit
                if (_messageThread != null && _messageThread.IsAlive)
                {
                    _messageThread.Join(1000);
                }

                // Remove the tray icon
                if (_windowHandle != IntPtr.Zero)
                {
                    RemoveTrayIcon();
                }

                if (_iconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_iconHandle);
                }

                if (_windowHandle != IntPtr.Zero)
                {
                    DestroyWindow(_windowHandle);
                }

                _isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(
            IntPtr hMenu,
            uint uFlags,
            int x, int y,
            int nReserved,
            IntPtr hWnd,
            IntPtr prcRect);

        private const int MF_SEPARATOR = 0x00000800;
        private const int MF_STRING = 0x00000000;
        private const int MF_GRAYED = 0x00000001;
        private const int TPM_BOTTOMALIGN = 0x0020;
        private const int TPM_LEFTALIGN = 0x0000;

        private const int MENU_SHOW_POPUP = 100;
        private const int MENU_SETTINGS = 101;
        private const int MENU_EXIT = 102;
        private const int MENU_PLAY_PAUSE = 103;
        private const int MENU_NOISE_CONTROL = 104;
        private const int MENU_NOISE_ANC = 105;
        private const int MENU_NOISE_TRANSPARENCY = 106;
        private const int MENU_NOISE_OFF = 107;

        [DllImport("user32.dll")]
        private static extern bool InsertMenu(
            IntPtr hMenu,
            uint uPosition,
            uint uFlags,
            uint uIDNewItem,
            string lpNewItem);

        private const int MF_BYPOSITION = 0x00000400;
        private const int MF_POPUP = 0x00000010;
    }
}
