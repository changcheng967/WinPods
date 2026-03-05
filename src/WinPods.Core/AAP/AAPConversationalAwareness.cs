namespace WinPods.Core.AAP;

/// <summary>
/// Provides conversational awareness functionality via the AAP protocol.
/// Conversational awareness automatically lowers volume when you speak.
/// </summary>
public class AAPConversationalAwareness : IDisposable
{
    private readonly AAPConnection _connection;
    private readonly bool _ownsConnection;
    private readonly object _lock = new();
    private bool _isDisposed;
    private bool _enabled;
    private byte _volumeLevel = 50;

    /// <summary>
    /// Event raised when conversational awareness state changes.
    /// </summary>
    public event EventHandler<bool>? EnabledChanged;

    /// <summary>
    /// Event raised when volume level changes.
    /// </summary>
    public event EventHandler<byte>? VolumeChanged;

    /// <summary>
    /// Gets whether conversational awareness is enabled.
    /// </summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Gets or sets the volume level (0-100).
    /// </summary>
    public byte VolumeLevel
    {
        get => _volumeLevel;
        set => _volumeLevel = Math.Clamp(value, (byte)0, (byte)100);
    }

    /// <summary>
    /// Gets whether conversational awareness is available.
    /// </summary>
    public bool IsAvailable => _connection.IsConnected;

    /// <summary>
    /// Creates a new conversational awareness instance using the specified connection.
    /// </summary>
    public AAPConversationalAwareness(AAPConnection connection, bool ownsConnection = false)
    {
        _connection = connection;
        _ownsConnection = ownsConnection;
    }

    /// <summary>
    /// Creates a new conversational awareness instance with a fresh connection.
    /// </summary>
    public AAPConversationalAwareness() : this(new AAPConnection(), ownsConnection: true)
    {
    }

    /// <summary>
    /// Connects to AirPods.
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
    /// Enables or disables conversational awareness.
    /// </summary>
    public async Task<AAPCommandResult> SetEnabledAsync(bool enabled)
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
            return AAPCommandResult.NotConnected;

        // Build command
        // Packet format: [header] [opcode: 0x0009] [identifier: 0x1D] [enabled byte] [volume] [padding]
        byte[] data = new byte[4];
        data[0] = enabled ? (byte)0x01 : (byte)0x00;
        data[1] = _volumeLevel;
        data[2] = 0x00;
        data[3] = 0x00;

        Console.WriteLine($"[AAPConversationalAwareness] Setting enabled: {enabled}");

        var response = await _connection.SendAndWaitForResponseAsync(
            AAPCommandOpcode.ControlCommand,
            AAPIdentifier.ConversationAwareness,
            data,
            timeoutMs: 3000).ConfigureAwait(false);

        if (response == null)
        {
            // Assume success
            _enabled = enabled;
            EnabledChanged?.Invoke(this, enabled);
            return AAPCommandResult.Success;
        }

        if (response.Value.Opcode == AAPCommandOpcode.Acknowledgement)
        {
            _enabled = enabled;
            EnabledChanged?.Invoke(this, enabled);
            return AAPCommandResult.Success;
        }

        return AAPCommandResult.ProtocolError;
    }

    /// <summary>
    /// Sets the volume level for conversational awareness.
    /// </summary>
    public async Task<AAPCommandResult> SetVolumeLevelAsync(byte volume)
    {
        ThrowIfDisposed();

        if (!_connection.IsConnected)
            return AAPCommandResult.NotConnected;

        volume = Math.Clamp(volume, (byte)0, (byte)100);

        byte[] data = new byte[4];
        data[0] = _enabled ? (byte)0x01 : (byte)0x00;
        data[1] = volume;
        data[2] = 0x00;
        data[3] = 0x00;

        Console.WriteLine($"[AAPConversationalAwareness] Setting volume: {volume}");

        var response = await _connection.SendAndWaitForResponseAsync(
            AAPCommandOpcode.ControlCommand,
            AAPIdentifier.ConversationAwareness,
            data,
            timeoutMs: 3000).ConfigureAwait(false);

        if (response == null || response.Value.Opcode == AAPCommandOpcode.Acknowledgement)
        {
            _volumeLevel = volume;
            VolumeChanged?.Invoke(this, volume);
            return AAPCommandResult.Success;
        }

        return AAPCommandResult.ProtocolError;
    }

    /// <summary>
    /// Enables conversational awareness.
    /// </summary>
    public Task<AAPCommandResult> EnableAsync() => SetEnabledAsync(true);

    /// <summary>
    /// Disables conversational awareness.
    /// </summary>
    public Task<AAPCommandResult> DisableAsync() => SetEnabledAsync(false);

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AAPConversationalAwareness));
    }

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
