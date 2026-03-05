namespace WinPods.Core.AAP;

/// <summary>
/// Provides noise control functionality via the AAP protocol.
/// Supports ANC, Transparency, Off, and Adaptive modes.
/// </summary>
public class AAPNoiseControl : IDisposable
{
    private readonly AAPConnection _connection;
    private readonly bool _ownsConnection;
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// Event raised when noise control mode changes.
    /// </summary>
    public event EventHandler<AAPNoiseMode>? ModeChanged;

    /// <summary>
    /// Gets the current noise control mode.
    /// </summary>
    public AAPNoiseMode? CurrentMode => _connection.CurrentNoiseMode;

    /// <summary>
    /// Gets whether noise control is available (connected and driver installed).
    /// </summary>
    public bool IsAvailable => _connection.IsConnected;

    /// <summary>
    /// Creates a new noise control instance using the specified connection.
    /// </summary>
    /// <param name="connection">The AAP connection to use.</param>
    /// <param name="ownsConnection">Whether to dispose the connection when this instance is disposed.</param>
    public AAPNoiseControl(AAPConnection connection, bool ownsConnection = false)
    {
        _connection = connection;
        _ownsConnection = ownsConnection;

        // Forward mode change events
        _connection.NoiseModeChanged += (s, mode) => ModeChanged?.Invoke(this, mode);
    }

    /// <summary>
    /// Creates a new noise control instance with a fresh connection.
    /// </summary>
    public AAPNoiseControl() : this(new AAPConnection(), ownsConnection: true)
    {
    }

    /// <summary>
    /// Connects to AirPods for noise control.
    /// </summary>
    /// <param name="bluetoothAddress">The Bluetooth address of the AirPods.</param>
    /// <returns>True if connection succeeded.</returns>
    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        ThrowIfDisposed();

        if (!_connection.IsDriverInstalled)
        {
            Console.WriteLine("[AAPNoiseControl] Driver not installed");
            return false;
        }

        if (!_connection.Initialize())
        {
            Console.WriteLine("[AAPNoiseControl] Failed to initialize driver");
            return false;
        }

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
    /// Sets the noise control mode.
    /// </summary>
    /// <param name="mode">The desired noise control mode.</param>
    /// <returns>The result of the command.</returns>
    public async Task<AAPCommandResult> SetModeAsync(AAPNoiseMode mode)
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
        {
            Console.WriteLine("[AAPNoiseControl] Cannot set mode - not connected");
            return AAPCommandResult.NotConnected;
        }

        // Build noise control command
        // Packet format: [header] [opcode: 0x0009] [identifier: 0x0D] [mode byte] [padding]
        byte[] data = new byte[4];
        data[0] = (byte)mode;
        data[1] = 0x00; // Unused
        data[2] = 0x00; // Unused
        data[3] = 0x00; // Unused

        Console.WriteLine($"[AAPNoiseControl] Setting mode to {mode}");

        // Send command and wait for acknowledgement
        var response = await _connection.SendAndWaitForResponseAsync(
            AAPCommandOpcode.ControlCommand,
            AAPIdentifier.ListeningMode,
            data,
            timeoutMs: 3000).ConfigureAwait(false);

        if (response == null)
        {
            // Command was sent but no response - assume success
            Console.WriteLine("[AAPNoiseControl] No response received - assuming success");
            ModeChanged?.Invoke(this, mode);
            return AAPCommandResult.Success;
        }

        // Check if it's an acknowledgement
        if (response.Value.Opcode == AAPCommandOpcode.Acknowledgement)
        {
            Console.WriteLine($"[AAPNoiseControl] Mode change acknowledged");
            ModeChanged?.Invoke(this, mode);
            return AAPCommandResult.Success;
        }

        // Check for error response
        if (response.Value.Data.Length > 0 && response.Value.Data[0] != 0)
        {
            Console.WriteLine($"[AAPNoiseControl] Error response: {response.Value.Data[0]}");
            return AAPCommandResult.ProtocolError;
        }

        return AAPCommandResult.Success;
    }

    /// <summary>
    /// Sets noise control to Off.
    /// </summary>
    public Task<AAPCommandResult> SetOffAsync() => SetModeAsync(AAPNoiseMode.Off);

    /// <summary>
    /// Sets noise control to Active Noise Cancellation.
    /// </summary>
    public Task<AAPCommandResult> SetNoiseCancellationAsync() => SetModeAsync(AAPNoiseMode.NoiseCancellation);

    /// <summary>
    /// Sets noise control to Transparency.
    /// </summary>
    public Task<AAPCommandResult> SetTransparencyAsync() => SetModeAsync(AAPNoiseMode.Transparency);

    /// <summary>
    /// Sets noise control to Adaptive (AirPods Pro 2).
    /// </summary>
    public Task<AAPCommandResult> SetAdaptiveAsync() => SetModeAsync(AAPNoiseMode.Adaptive);

    /// <summary>
    /// Queries the current noise control mode from the device.
    /// </summary>
    /// <returns>The current mode, or null if unable to determine.</returns>
    public async Task<AAPNoiseMode?> GetCurrentModeAsync()
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
        {
            return null;
        }

        // Return cached value if available
        if (_connection.CurrentNoiseMode.HasValue)
        {
            return _connection.CurrentNoiseMode.Value;
        }

        // Could query device here, but AAP typically sends notifications
        // when the mode changes, so we rely on cached state.
        return null;
    }

    /// <summary>
    /// Cycles through noise control modes: Off -> ANC -> Transparency -> Off.
    /// </summary>
    public async Task<AAPCommandResult> CycleModeAsync()
    {
        var current = await GetCurrentModeAsync().ConfigureAwait(false);

        var nextMode = current switch
        {
            AAPNoiseMode.Off => AAPNoiseMode.NoiseCancellation,
            AAPNoiseMode.NoiseCancellation => AAPNoiseMode.Transparency,
            AAPNoiseMode.Transparency => AAPNoiseMode.Off,
            AAPNoiseMode.Adaptive => AAPNoiseMode.Off,
            _ => AAPNoiseMode.Off
        };

        return await SetModeAsync(nextMode).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if a specific noise control mode is supported by the connected device.
    /// Note: This requires device model information which may not always be available.
    /// </summary>
    public bool IsModeSupported(AAPNoiseMode mode)
    {
        // All devices support Off
        if (mode == AAPNoiseMode.Off)
            return true;

        // ANC and Transparency require AirPods Pro, Pro 2, or AirPods 4
        // Adaptive is only on AirPods Pro 2
        // For now, assume all modes are supported if connected
        return _connection.IsConnected;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AAPNoiseControl));
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
