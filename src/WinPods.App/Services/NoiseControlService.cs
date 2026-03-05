using WinPods.Core.AAP;

namespace WinPods.App.Services
{
    /// <summary>
    /// Noise control modes for AirPods.
    /// </summary>
    public enum NoiseControlMode
    {
        Off = 0,
        NoiseCancellation = 1,
        Transparency = 2,
        Adaptive = 3
    }

    /// <summary>
    /// Service for controlling AirPods noise control via the AAP kernel driver.
    /// This replaces the old GATT-based approach which does not work on Windows.
    /// </summary>
    public class NoiseControlService : IDisposable
    {
        private readonly AAPConnection _aapConnection;
        private readonly AAPNoiseControl _aapNoiseControl;
        private readonly DriverService _driverService;
        private readonly bool _ownsDriverService;
        private readonly object _lock = new();
        private bool _isDisposed;
        private NoiseControlMode _currentMode = NoiseControlMode.Off;
        private ulong? _connectedAddress;

        /// <summary>
        /// Event raised when noise control mode changes.
        /// </summary>
        public event EventHandler<NoiseControlMode>? ModeChanged;

        /// <summary>
        /// Event raised when connection state changes.
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        /// <summary>
        /// Gets the current noise control mode.
        /// </summary>
        public NoiseControlMode CurrentMode => _currentMode;

        /// <summary>
        /// Gets whether the service is connected to AirPods.
        /// </summary>
        public bool IsConnected => _aapNoiseControl.IsAvailable;

        /// <summary>
        /// Gets whether the driver is installed.
        /// </summary>
        public bool IsDriverInstalled => _driverService.IsInstalled;

        /// <summary>
        /// Gets the driver service for status checking.
        /// </summary>
        public DriverService Driver => _driverService;

        /// <summary>
        /// Creates a new NoiseControlService.
        /// </summary>
        public NoiseControlService()
        {
            _driverService = new DriverService();
            _ownsDriverService = true;
            _aapConnection = new AAPConnection(_driverService.GetBridge(), ownsDriver: false);
            _aapNoiseControl = new AAPNoiseControl(_aapConnection, ownsConnection: false);

            // Forward mode change events
            _aapNoiseControl.ModeChanged += (s, mode) =>
            {
                _currentMode = ConvertFromAAPMode(mode);
                ModeChanged?.Invoke(this, _currentMode);
            };
        }

        /// <summary>
        /// Creates a new NoiseControlService with an existing driver service.
        /// </summary>
        public NoiseControlService(DriverService driverService)
        {
            _driverService = driverService;
            _ownsDriverService = false;
            _aapConnection = new AAPConnection(_driverService.GetBridge(), ownsDriver: false);
            _aapNoiseControl = new AAPNoiseControl(_aapConnection, ownsConnection: false);

            _aapNoiseControl.ModeChanged += (s, mode) =>
            {
                _currentMode = ConvertFromAAPMode(mode);
                ModeChanged?.Invoke(this, _currentMode);
            };
        }

        /// <summary>
        /// Connects to the AirPods via the AAP driver.
        /// </summary>
        public async Task<bool> ConnectAsync(ulong bluetoothAddress)
        {
            ThrowIfDisposed();

            Console.WriteLine($"[NoiseControl] ConnectAsync called for {bluetoothAddress:X12}");

            // Check if driver is installed
            if (!_driverService.IsInstalled)
            {
                Console.WriteLine("[NoiseControl] Driver not installed - noise control unavailable");
                Console.WriteLine("[NoiseControl] This is a Windows limitation: AirPods use L2CAP (not GATT) for control");
                return false;
            }

            // Open the driver if not already open
            if (!_driverService.Open())
            {
                Console.WriteLine("[NoiseControl] Failed to open driver");
                return false;
            }

            // Connect via AAP
            bool connected = await _aapNoiseControl.ConnectAsync(bluetoothAddress);

            if (connected)
            {
                _connectedAddress = bluetoothAddress;
                Console.WriteLine($"[NoiseControl] Connected to {bluetoothAddress:X12}");
                ConnectionStateChanged?.Invoke(this, true);
            }
            else
            {
                Console.WriteLine($"[NoiseControl] Failed to connect to {bluetoothAddress:X12}");
                ConnectionStateChanged?.Invoke(this, false);
            }

            return connected;
        }

        /// <summary>
        /// Sets the noise control mode.
        /// </summary>
        public async Task<bool> SetModeAsync(NoiseControlMode mode)
        {
            ThrowIfDisposed();

            Console.WriteLine($"[NoiseControl] SetModeAsync: {mode}");

            if (!_aapNoiseControl.IsAvailable)
            {
                Console.WriteLine("[NoiseControl] Cannot set mode - not connected");
                return false;
            }

            var aapMode = ConvertToAAPMode(mode);
            var result = await _aapNoiseControl.SetModeAsync(aapMode);

            if (result == AAPCommandResult.Success)
            {
                _currentMode = mode;
                Console.WriteLine($"[NoiseControl] Mode set to {mode}");
                return true;
            }
            else
            {
                Console.WriteLine($"[NoiseControl] Failed to set mode: {result}");
                return false;
            }
        }

        /// <summary>
        /// Sets noise control to Off.
        /// </summary>
        public Task<bool> SetOffAsync() => SetModeAsync(NoiseControlMode.Off);

        /// <summary>
        /// Sets noise control to Active Noise Cancellation.
        /// </summary>
        public Task<bool> SetNoiseCancellationAsync() => SetModeAsync(NoiseControlMode.NoiseCancellation);

        /// <summary>
        /// Sets noise control to Transparency.
        /// </summary>
        public Task<bool> SetTransparencyAsync() => SetModeAsync(NoiseControlMode.Transparency);

        /// <summary>
        /// Sets noise control to Adaptive (AirPods Pro 2 only).
        /// </summary>
        public Task<bool> SetAdaptiveAsync() => SetModeAsync(NoiseControlMode.Adaptive);

        /// <summary>
        /// Cycles through noise control modes.
        /// </summary>
        public async Task<bool> CycleModeAsync()
        {
            var nextMode = _currentMode switch
            {
                NoiseControlMode.Off => NoiseControlMode.NoiseCancellation,
                NoiseControlMode.NoiseCancellation => NoiseControlMode.Transparency,
                NoiseControlMode.Transparency => NoiseControlMode.Off,
                NoiseControlMode.Adaptive => NoiseControlMode.Off,
                _ => NoiseControlMode.Off
            };

            return await SetModeAsync(nextMode);
        }

        /// <summary>
        /// Gets the current mode from the device.
        /// </summary>
        public async Task<NoiseControlMode?> GetCurrentModeAsync()
        {
            ThrowIfDisposed();

            var aapMode = await _aapNoiseControl.GetCurrentModeAsync();
            return aapMode.HasValue ? ConvertFromAAPMode(aapMode.Value) : null;
        }

        /// <summary>
        /// Disconnects from the AirPods.
        /// </summary>
        public void Disconnect()
        {
            ThrowIfDisposed();

            _aapNoiseControl.Disconnect();
            _connectedAddress = null;
            ConnectionStateChanged?.Invoke(this, false);
            Console.WriteLine("[NoiseControl] Disconnected");
        }

        /// <summary>
        /// Checks if a specific noise control mode is supported.
        /// </summary>
        public bool IsModeSupported(NoiseControlMode mode)
        {
            return _aapNoiseControl.IsModeSupported(ConvertToAAPMode(mode));
        }

        private static AAPNoiseMode ConvertToAAPMode(NoiseControlMode mode)
        {
            return mode switch
            {
                NoiseControlMode.Off => AAPNoiseMode.Off,
                NoiseControlMode.NoiseCancellation => AAPNoiseMode.NoiseCancellation,
                NoiseControlMode.Transparency => AAPNoiseMode.Transparency,
                NoiseControlMode.Adaptive => AAPNoiseMode.Adaptive,
                _ => AAPNoiseMode.Off
            };
        }

        private static NoiseControlMode ConvertFromAAPMode(AAPNoiseMode mode)
        {
            return mode switch
            {
                AAPNoiseMode.Off => NoiseControlMode.Off,
                AAPNoiseMode.NoiseCancellation => NoiseControlMode.NoiseCancellation,
                AAPNoiseMode.Transparency => NoiseControlMode.Transparency,
                AAPNoiseMode.Adaptive => NoiseControlMode.Adaptive,
                _ => NoiseControlMode.Off
            };
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(NoiseControlService));
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                _aapNoiseControl.Dispose();
                _aapConnection.Dispose();

                if (_ownsDriverService)
                {
                    _driverService.Dispose();
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}
