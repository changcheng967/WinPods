namespace WinPods.Core.AAP;

/// <summary>
/// Provides device information queries via the AAP protocol.
/// Can read firmware version, serial number, and other device details.
/// </summary>
public class AAPDeviceInfo : IDisposable
{
    private readonly AAPConnection _connection;
    private readonly bool _ownsConnection;
    private readonly object _lock = new();
    private bool _isDisposed;

    // Cached device info
    private string? _serialNumber;
    private string? _firmwareVersion;
    private ushort _modelId;

    /// <summary>
    /// Gets the cached serial number.
    /// </summary>
    public string? SerialNumber => _serialNumber;

    /// <summary>
    /// Gets the cached firmware version.
    /// </summary>
    public string? FirmwareVersion => _firmwareVersion;

    /// <summary>
    /// Gets the cached model ID.
    /// </summary>
    public ushort ModelId => _modelId;

    /// <summary>
    /// Gets whether device info is available.
    /// </summary>
    public bool IsAvailable => _connection.IsConnected;

    /// <summary>
    /// Creates a new device info instance using the specified connection.
    /// </summary>
    public AAPDeviceInfo(AAPConnection connection, bool ownsConnection = false)
    {
        _connection = connection;
        _ownsConnection = ownsConnection;
    }

    /// <summary>
    /// Creates a new device info instance with a fresh connection.
    /// </summary>
    public AAPDeviceInfo() : this(new AAPConnection(), ownsConnection: true)
    {
    }

    /// <summary>
    /// Connects to AirPods for device info queries.
    /// </summary>
    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        ThrowIfDisposed();

        if (!_connection.IsDriverInstalled)
            return false;

        if (!_connection.Initialize())
            return false;

        return await _connection.ConnectAsync(bluetoothAddress).ConfigureAwait(false);
    }

    /// <summary>
    /// Queries the serial number from the device.
    /// </summary>
    public async Task<string?> GetSerialNumberAsync()
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
            return null;

        if (_serialNumber != null)
            return _serialNumber;

        var response = await _connection.SendAndWaitForResponseAsync(
            AAPCommandOpcode.ControlCommand,
            AAPIdentifier.SerialNumber,
            null,
            3000).ConfigureAwait(false);

        if (response == null || response.Value.Data.Length == 0)
            return null;

        // Serial number is typically encoded as ASCII
        _serialNumber = System.Text.Encoding.ASCII.GetString(response.Value.Data).TrimEnd('\0');
        return _serialNumber;
    }

    /// <summary>
    /// Queries the firmware version from the device.
    /// </summary>
    public async Task<string?> GetFirmwareVersionAsync()
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
            return null;

        if (_firmwareVersion != null)
            return _firmwareVersion;

        var response = await _connection.SendAndWaitForResponseAsync(
            AAPCommandOpcode.ControlCommand,
            AAPIdentifier.FirmwareVersion,
            null,
            3000).ConfigureAwait(false);

        if (response == null || response.Value.Data.Length < 2)
            return null;

        // Firmware version is typically 2 bytes: major.minor
        byte major = response.Value.Data[0];
        byte minor = response.Value.Data[1];
        _firmwareVersion = $"{major}.{minor:D2}";
        return _firmwareVersion;
    }

    /// <summary>
    /// Queries all device information.
    /// </summary>
    public async Task<AAPDeviceData?> GetAllInfoAsync()
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
            return null;

        // Query all available info
        await GetSerialNumberAsync().ConfigureAwait(false);
        await GetFirmwareVersionAsync().ConfigureAwait(false);

        return new AAPDeviceData
        {
            SerialNumber = _serialNumber,
            FirmwareVersion = _firmwareVersion,
            ModelId = _modelId
        };
    }

    /// <summary>
    /// Clears cached device info (forces refresh on next query).
    /// </summary>
    public void ClearCache()
    {
        _serialNumber = null;
        _firmwareVersion = null;
        _modelId = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AAPDeviceInfo));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_ownsConnection)
            {
                _connection.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
