using Windows.Devices.Bluetooth.Advertisement;
using WinPods.Core.Models;
using System.IO;

namespace WinPods.Core.Bluetooth
{
    /// <summary>
    /// Scans for AirPods devices via BLE advertisements.
    /// </summary>
    public class BleScanner : IDisposable
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "WinPods.log");

        private static readonly object _logLock = new object();

        static BleScanner()
        {
            // Initialize log file if it doesn't exist
            lock (_logLock)
            {
                try
                {
                    if (!File.Exists(LogFilePath))
                    {
                        File.WriteAllText(LogFilePath, $"=== WinPods Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        private static void Log(string message)
        {
            lock (_logLock)
            {
                try
                {
                    string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    File.AppendAllText(LogFilePath, timestampedMessage + "\n");
                    System.Diagnostics.Debug.WriteLine(timestampedMessage);
                }
                catch
                {
                    // Ignore logging errors
                }
            }
        }

        private const ushort AppleCompanyId = 0x004C;

        private BluetoothLEAdvertisementWatcher? _watcher;
        private bool _isDisposed;

        /// <summary>
        /// Event raised when a valid AirPods advertisement is received.
        /// </summary>
        public event EventHandler<AirPodsAdvertisement>? AdvertisementReceived;

        /// <summary>
        /// Event raised when the scanner status changes.
        /// </summary>
        public event EventHandler<BleScannerStatus>? StatusChanged;

        /// <summary>
        /// Gets whether the scanner is currently running.
        /// </summary>
        public bool IsScanning { get; private set; }

        /// <summary>
        /// Gets the current status of the scanner.
        /// </summary>
        public BleScannerStatus Status { get; private set; }

        /// <summary>
        /// Starts scanning for AirPods devices.
        /// </summary>
        public void Start()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(BleScanner));
            }

            if (IsScanning)
            {
                return;  // Already scanning
            }

            try
            {
                // Create and configure the watcher
                _watcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active
                };

                // Subscribe to events
                _watcher.Received += OnAdvertisementReceived;
                _watcher.Stopped += OnWatcherStopped;

                // Start scanning
                _watcher.Start();

                IsScanning = true;
                Status = BleScannerStatus.Scanning;
                StatusChanged?.Invoke(this, Status);

                // Log that scanning has started
                Log("[SCANNER] Started scanning for AirPods devices...");
            }
            catch (Exception ex)
            {
                Status = BleScannerStatus.Error;
                StatusChanged?.Invoke(this, Status);
                throw new InvalidOperationException("Failed to start BLE scanner", ex);
            }
        }

        /// <summary>
        /// Stops scanning for AirPods devices.
        /// </summary>
        public void Stop()
        {
            if (_isDisposed || !IsScanning)
            {
                return;
            }

            try
            {
                if (_watcher != null)
                {
                    _watcher.Received -= OnAdvertisementReceived;
                    _watcher.Stopped -= OnWatcherStopped;
                    _watcher.Stop();
                    _watcher = null;
                }

                IsScanning = false;
                Status = BleScannerStatus.Stopped;
                StatusChanged?.Invoke(this, Status);
            }
            catch (Exception)
            {
                // Ignore errors during stop
            }
        }

        /// <summary>
        /// Handles BLE advertisement received events.
        /// </summary>
        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                // Filter for Apple manufacturer data
                foreach (var mfgData in args.Advertisement.ManufacturerData)
                {
                    if (mfgData.CompanyId != AppleCompanyId)
                    {
                        continue;
                    }

                    // Extract data bytes
                    var data = new byte[mfgData.Data.Length];
                    Windows.Storage.Streams.DataReader.FromBuffer(mfgData.Data).ReadBytes(data);

                    // Validate it looks like AirPods data
                    if (!ProtocolParser.IsValidAirPodsData(data))
                    {
                        continue;
                    }

                    // Parse the protocol
                    var result = ProtocolParser.Parse(data, args);
                    if (result != null)
                    {
                        // Notify subscribers
                        AdvertisementReceived?.Invoke(this, result);
                    }
                }
            }
            catch (Exception)
            {
                // Ignore parsing errors - continue scanning
            }
        }

        /// <summary>
        /// Handles watcher stopped events.
        /// </summary>
        private void OnWatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            IsScanning = false;

            // Set status to stopped (error checking can be added later if needed)
            Status = BleScannerStatus.Stopped;
            StatusChanged?.Invoke(this, Status);
        }

        /// <summary>
        /// Disposes the scanner and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                Stop();
                _isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Status of the BLE scanner.
    /// </summary>
    public enum BleScannerStatus
    {
        /// <summary>
        /// Scanner is idle/not started.
        /// </summary>
        Idle,

        /// <summary>
        /// Scanner is actively scanning.
        /// </summary>
        Scanning,

        /// <summary>
        /// Scanner has stopped.
        /// </summary>
        Stopped,

        /// <summary>
        /// Scanner encountered an error.
        /// </summary>
        Error
    }
}
