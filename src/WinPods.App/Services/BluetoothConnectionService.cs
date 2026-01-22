using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;

namespace WinPods.App.Services
{
    /// <summary>
    /// Manages Bluetooth pairing and connection for AirPods.
    /// </summary>
    public class BluetoothConnectionService : IDisposable
    {
        private BluetoothLEDevice? _device;
        private readonly System.Threading.SemaphoreSlim _semaphore = new System.Threading.SemaphoreSlim(1, 1);

        /// <summary>
        /// Event raised when connection status changes.
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// Gets the current connection status.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Gets the current connection status.
        /// </summary>
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        /// <summary>
        /// Attempts to connect to AirPods given their Bluetooth address.
        /// </summary>
        public async Task<ConnectionResult> ConnectAsync(ulong bluetoothAddress, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                Console.WriteLine($"[BluetoothConnection] ========== Connection Attempt ==========");
                Console.WriteLine($"[BluetoothConnection] Target address: {bluetoothAddress:X12}");

                UpdateStatus(ConnectionStatus.Connecting);

                // Get the device
                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (_device == null)
                {
                    Console.WriteLine("[BluetoothConnection] ❌ Failed to get device");
                    UpdateStatus(ConnectionStatus.Disconnected);
                    return ConnectionResult.CreateFailed("Unable to access device");
                }

                Console.WriteLine($"[BluetoothConnection] ✓ Device found: {_device.Name}");
                Console.WriteLine($"[BluetoothConnection] ConnectionStatus: {_device.ConnectionStatus}");

                // Check if paired
                var deviceInfo = await DeviceInformation.CreateFromIdAsync(_device.DeviceId);
                bool isPaired = deviceInfo.Pairing.IsPaired;

                Console.WriteLine($"[BluetoothConnection] IsPaired: {isPaired}");

                if (!isPaired)
                {
                    // Need to pair first
                    Console.WriteLine("[BluetoothConnection] Attempting to pair...");
                    UpdateStatus(ConnectionStatus.Pairing);

                    // Check if pairing is supported
                    if (!deviceInfo.Pairing.CanPair)
                    {
                        Console.WriteLine("[BluetoothConnection] ❌ Pairing not supported");
                        UpdateStatus(ConnectionStatus.Disconnected);
                        return ConnectionResult.CreateFailed("Pairing not supported");
                    }

                    // Attempt pairing
                    var pairingResult = await deviceInfo.Pairing.PairAsync();
                    if (pairingResult.Status == DevicePairingResultStatus.Paired
                        || pairingResult.Status == DevicePairingResultStatus.AlreadyPaired)
                    {
                        Console.WriteLine("[BluetoothConnection] ✓✓✓ Pairing successful!");
                    }
                    else
                    {
                        Console.WriteLine($"[BluetoothConnection] ❌ Pairing failed: {pairingResult.Status}");
                        UpdateStatus(ConnectionStatus.Disconnected);
                        return ConnectionResult.CreateFailed($"Pairing failed: {pairingResult.Status}");
                    }
                }

                // Now connect the audio profile
                Console.WriteLine("[BluetoothConnection] Requesting audio connection...");

                // Trigger connection by getting GATT services (this causes Windows to connect the audio profile)
                var gattResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (gattResult.Status == GattCommunicationStatus.Success)
                {
                    Console.WriteLine("[BluetoothConnection] ✓✓✓ SUCCESS - AirPods connected!");
                    UpdateStatus(ConnectionStatus.Connected);

                    // Monitor connection status changes
                    _device.ConnectionStatusChanged += OnConnectionStatusChanged;

                    return ConnectionResult.CreateSuccess();
                }
                else
                {
                    Console.WriteLine($"[BluetoothConnection] ⚠ GATT services failed (status: {gattResult.Status})");
                    Console.WriteLine("[BluetoothConnection] But device may still be connected for audio...");

                    // Check actual connection status
                    if (_device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                    {
                        UpdateStatus(ConnectionStatus.Connected);
                        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
                        return ConnectionResult.CreateSuccess();
                    }

                    UpdateStatus(ConnectionStatus.Disconnected);
                    return ConnectionResult.CreateFailed("Failed to connect audio profile");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BluetoothConnection] ❌ Connection failed: {ex.Message}");
                Console.WriteLine($"[BluetoothConnection] Exception: {ex.GetType().Name}");
                UpdateStatus(ConnectionStatus.Disconnected);
                return ConnectionResult.CreateFailed(ex.Message);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Disconnects from the AirPods.
        /// </summary>
        public async Task DisconnectAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_device != null)
                {
                    Console.WriteLine("[BluetoothConnection] Disconnecting...");

                    _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    _device.Dispose();
                    _device = null;

                    UpdateStatus(ConnectionStatus.Disconnected);
                    Console.WriteLine("[BluetoothConnection] ✓ Disconnected");
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Gets the current Bluetooth device.
        /// </summary>
        public async Task<BluetoothLEDevice?> GetDeviceAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                return _device;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Handles connection status changes from the device.
        /// </summary>
        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            Console.WriteLine($"[BluetoothConnection] Connection status changed: {sender.ConnectionStatus}");

            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                UpdateStatus(ConnectionStatus.Connected);
            }
            else
            {
                UpdateStatus(ConnectionStatus.Disconnected);
            }
        }

        /// <summary>
        /// Updates the connection status and raises the event.
        /// </summary>
        private void UpdateStatus(ConnectionStatus status)
        {
            bool wasConnected = IsConnected;
            Status = status;
            IsConnected = status == ConnectionStatus.Connected;

            if (wasConnected != IsConnected || status == ConnectionStatus.Connected)
            {
                ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(status, IsConnected));
            }
        }

        /// <summary>
        /// Checks if device is currently connected.
        /// </summary>
        public async Task<bool> CheckConnectionAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_device == null)
                    return false;

                var isConnected = _device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (isConnected && !IsConnected)
                {
                    UpdateStatus(ConnectionStatus.Connected);
                }
                else if (!isConnected && IsConnected)
                {
                    UpdateStatus(ConnectionStatus.Disconnected);
                }

                return isConnected;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Disposes the service.
        /// </summary>
        public void Dispose()
        {
            DisconnectAsync().Wait();
            _semaphore.Dispose();
        }
    }

    /// <summary>
    /// Connection status enum.
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Pairing,
        Connected
    }

    /// <summary>
    /// Connection result.
    /// </summary>
    public record ConnectionResult(
        bool IsSuccess,
        string? ErrorMessage = null
    )
    {
        public static ConnectionResult CreateSuccess() => new ConnectionResult(true);
        public static ConnectionResult CreateFailed(string error) => new ConnectionResult(false, error);
    }

    /// <summary>
    /// Event args for connection state changes.
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public ConnectionStatus Status { get; }
        public bool IsConnected { get; }

        public ConnectionStateChangedEventArgs(ConnectionStatus status, bool isConnected)
        {
            Status = status;
            IsConnected = isConnected;
        }
    }
}
