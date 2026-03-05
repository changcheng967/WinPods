namespace WinPods.Core.AAP;

/// <summary>
/// Provides ear detection functionality via the AAP protocol.
/// More reliable than BLE advertisement parsing.
/// </summary>
public class AAPEarDetection : IDisposable
{
    private readonly AAPConnection _connection;
    private readonly bool _ownsConnection;
    private readonly object _lock = new();
    private bool _isDisposed;
    private bool _lastLeftInEar;
    private bool _lastRightInEar;

    /// <summary>
    /// Event raised when ear detection state changes.
    /// </summary>
    public event EventHandler<AAPEarDetectionInfo>? EarDetectionChanged;

    /// <summary>
    /// Event raised when both earbuds are removed.
    /// </summary>
    public event EventHandler? BothRemoved;

    /// <summary>
    /// Event raised when both earbuds are inserted.
    /// </summary>
    public event EventHandler? BothInserted;

    /// <summary>
    /// Event raised when left earbud is inserted.
    /// </summary>
    public event EventHandler? LeftInserted;

    /// <summary>
    /// Event raised when right earbud is inserted.
    /// </summary>
    public event EventHandler? RightInserted;

    /// <summary>
    /// Gets the current ear detection state.
    /// </summary>
    public AAPEarDetectionInfo? CurrentState => _connection.LastEarDetection;

    /// <summary>
    /// Gets whether ear detection is available.
    /// </summary>
    public bool IsAvailable => _connection.IsConnected;

    /// <summary>
    /// Gets whether both earbuds are currently in ear.
    /// </summary>
    public bool BothInEar => CurrentState?.LeftInEar == true && CurrentState?.RightInEar == true;

    /// <summary>
    /// Creates a new ear detection instance using the specified connection.
    /// </summary>
    public AAPEarDetection(AAPConnection connection, bool ownsConnection = false)
    {
        _connection = connection;
        _ownsConnection = ownsConnection;

        // Subscribe to connection events
        _connection.EarDetectionChanged += OnEarDetectionChanged;
    }

    /// <summary>
    /// Creates a new ear detection instance with a fresh connection.
    /// </summary>
    public AAPEarDetection() : this(new AAPConnection(), ownsConnection: true)
    {
    }

    /// <summary>
    /// Connects to AirPods for ear detection.
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
    /// Disconnects from AirPods.
    /// </summary>
    public void Disconnect()
    {
        ThrowIfDisposed();
        _connection.Disconnect();
    }

    /// <summary>
    /// Gets the current ear detection state from the device.
    /// </summary>
    public async Task<AAPEarDetectionInfo?> GetCurrentStateAsync()
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
            return null;

        // Return cached value if available
        if (_connection.LastEarDetection != null)
            return _connection.LastEarDetection;

        return null;
    }

    /// <summary>
    /// Handles ear detection changes from the connection.
    /// </summary>
    private void OnEarDetectionChanged(object? sender, AAPEarDetectionInfo info)
    {
        // Fire general event
        EarDetectionChanged?.Invoke(this, info);

        // Fire specific events based on state changes
        bool leftChanged = info.LeftInEar != _lastLeftInEar;
        bool rightChanged = info.RightInEar != _lastRightInEar;

        // Both inserted
        if (!_lastLeftInEar && !_lastRightInEar && info.LeftInEar && info.RightInEar)
        {
            BothInserted?.Invoke(this, EventArgs.Empty);
        }
        // Both removed
        else if (_lastLeftInEar && _lastRightInEar && !info.LeftInEar && !info.RightInEar)
        {
            BothRemoved?.Invoke(this, EventArgs.Empty);
        }

        // Individual changes
        if (leftChanged && info.LeftInEar)
            LeftInserted?.Invoke(this, EventArgs.Empty);

        if (rightChanged && info.RightInEar)
            RightInserted?.Invoke(this, EventArgs.Empty);

        // Update last state
        _lastLeftInEar = info.LeftInEar;
        _lastRightInEar = info.RightInEar;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AAPEarDetection));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _connection.EarDetectionChanged -= OnEarDetectionChanged;

            if (_ownsConnection)
            {
                _connection.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
