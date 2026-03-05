using WinPods.Core.AAP;

namespace WinPods.App.Services;

/// <summary>
/// Service for detecting and managing the WinPodsAAP kernel driver.
/// </summary>
public class DriverService : IDisposable
{
    private readonly IDriverBridge _driverBridge;
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// Event raised when driver status changes.
    /// </summary>
    public event EventHandler<DriverStatus>? StatusChanged;

    /// <summary>
    /// Gets the current driver status.
    /// </summary>
    public DriverStatus Status => _driverBridge.Status;

    /// <summary>
    /// Gets whether the driver is installed.
    /// </summary>
    public bool IsInstalled => _driverBridge.IsDriverInstalled;

    /// <summary>
    /// Gets whether there is an active connection through the driver.
    /// </summary>
    public bool IsConnected => _driverBridge.IsConnected;

    /// <summary>
    /// Gets the Bluetooth address of the connected device.
    /// </summary>
    public ulong? ConnectedAddress => _driverBridge.ConnectedAddress;

    /// <summary>
    /// Creates a new DriverService with the real driver bridge.
    /// </summary>
    public DriverService()
    {
        _driverBridge = new DriverBridge();
    }

    /// <summary>
    /// Creates a new DriverService with a custom driver bridge (for testing).
    /// </summary>
    public DriverService(IDriverBridge driverBridge)
    {
        _driverBridge = driverBridge;
    }

    /// <summary>
    /// Opens the driver device for communication.
    /// </summary>
    /// <returns>True if the driver was opened successfully.</returns>
    public bool Open()
    {
        ThrowIfDisposed();

        bool success = _driverBridge.Open();
        if (success)
        {
            Console.WriteLine("[DriverService] Driver opened successfully");
            StatusChanged?.Invoke(this, _driverBridge.Status);
        }
        else
        {
            Console.WriteLine("[DriverService] Failed to open driver");
        }

        return success;
    }

    /// <summary>
    /// Connects to an L2CAP channel on the specified device.
    /// </summary>
    /// <param name="bluetoothAddress">The Bluetooth address of the device.</param>
    /// <param name="psm">The Protocol/Service Multiplexer (0x1001 for AAP).</param>
    /// <returns>True if connection succeeded.</returns>
    public bool Connect(ulong bluetoothAddress, ushort psm = 0x1001)
    {
        ThrowIfDisposed();

        if (!_driverBridge.IsDriverInstalled)
        {
            Console.WriteLine("[DriverService] Driver not installed");
            return false;
        }

        bool success = _driverBridge.Connect(bluetoothAddress, psm);
        if (success)
        {
            Console.WriteLine($"[DriverService] Connected to {bluetoothAddress:X12}");
            StatusChanged?.Invoke(this, _driverBridge.Status);
        }
        else
        {
            Console.WriteLine($"[DriverService] Failed to connect to {bluetoothAddress:X12}");
        }

        return success;
    }

    /// <summary>
    /// Disconnects from the current L2CAP channel.
    /// </summary>
    public void Disconnect()
    {
        ThrowIfDisposed();

        _driverBridge.Disconnect();
        Console.WriteLine("[DriverService] Disconnected");
        StatusChanged?.Invoke(this, _driverBridge.Status);
    }

    /// <summary>
    /// Gets the underlying driver bridge for direct access.
    /// </summary>
    public IDriverBridge GetBridge() => _driverBridge;

    /// <summary>
    /// Checks if the driver is available and working.
    /// </summary>
    public bool CheckDriverAvailable()
    {
        if (!_driverBridge.IsDriverInstalled)
        {
            return false;
        }

        // Try to open the driver
        if (!_driverBridge.Open())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets installation instructions for the driver.
    /// </summary>
    public static string GetInstallInstructions()
    {
        return @"To enable noise control and enhanced features, you need to install the WinPodsAAP driver.

INSTALLATION STEPS:

1. Download the WinPodsAAP driver from:
   https://github.com/changcheng967/WinPods/releases

2. Extract the driver files to a folder on your computer.

3. Open Command Prompt as Administrator.

4. Navigate to the driver folder and run:
   pnputil /add-driver WinPodsAAP.inf /install

5. Restart WinPods.

NOTE: The driver requires Windows 10/11 x64 or ARM64.
The driver is signed and will work without test mode.

WHY IS THIS NEEDED?
AirPods control commands use the Apple Accessory Protocol (AAP) which
runs over Bluetooth L2CAP, not BLE GATT. Windows blocks userspace apps
from opening L2CAP sockets, so a kernel driver is required.

All other WinPods features (battery monitoring, popup, media controls)
work without the driver. The driver only unlocks noise control and
enhanced ear detection.";
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(DriverService));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _driverBridge.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
