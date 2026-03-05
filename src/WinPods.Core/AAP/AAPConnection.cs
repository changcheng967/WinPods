namespace WinPods.Core.AAP;

/// <summary>
/// Manages the AAP connection to AirPods via the WinPodsAAP driver.
/// Handles connection lifecycle, auto-reconnect, and provides async operations.
/// </summary>
public class AAPConnection : IDisposable
{
    private readonly IDriverBridge _driver;
    private readonly bool _ownsDriver;
    private readonly object _lock = new();

    private bool _isDisposed;
    private bool _isConnecting;
    private ulong? _connectedAddress;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;

    // State tracking
    private AAPBatteryInfo? _lastBatteryInfo;
    private AAPEarDetectionInfo? _lastEarDetection;
    private AAPNoiseMode? _currentNoiseMode;

    /// <summary>
    /// Event raised when battery information is received.
    /// </summary>
    public event EventHandler<AAPBatteryInfo>? BatteryUpdated;

    /// <summary>
    /// Event raised when ear detection state changes.
    /// </summary>
    public event EventHandler<AAPEarDetectionInfo>? EarDetectionChanged;

    /// <summary>
    /// Event raised when noise control mode changes.
    /// </summary>
    public event EventHandler<AAPNoiseMode>? NoiseModeChanged;

    /// <summary>
    /// Event raised when the connection state changes.
    /// </summary>
    public event EventHandler<AAPConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Event raised when raw data is received (for debugging).
    /// </summary>
    public event EventHandler<byte[]>? DataReceived;

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public AAPConnectionState ConnectionState => _driver.GetConnectionState();

    /// <summary>
    /// Gets whether there is an active connection.
    /// </summary>
    public bool IsConnected => _driver.IsConnected;

    /// <summary>
    /// Gets whether the driver is installed.
    /// </summary>
    public bool IsDriverInstalled => _driver.IsDriverInstalled;

    /// <summary>
    /// Gets the currently connected Bluetooth address.
    /// </summary>
    public ulong? ConnectedAddress => _connectedAddress;

    /// <summary>
    /// Gets the last known battery information.
    /// </summary>
    public AAPBatteryInfo? LastBatteryInfo => _lastBatteryInfo;

    /// <summary>
    /// Gets the last known ear detection state.
    /// </summary>
    public AAPEarDetectionInfo? LastEarDetection => _lastEarDetection;

    /// <summary>
    /// Gets the current noise control mode.
    /// </summary>
    public AAPNoiseMode? CurrentNoiseMode => _currentNoiseMode;

    /// <summary>
    /// Creates a new AAP connection using the real driver.
    /// </summary>
    public AAPConnection() : this(new DriverBridge(), ownsDriver: true)
    {
    }

    /// <summary>
    /// Creates a new AAP connection with a custom driver bridge (for testing).
    /// </summary>
    /// <param name="driver">The driver bridge to use.</param>
    /// <param name="ownsDriver">Whether to dispose the driver when this connection is disposed.</param>
    public AAPConnection(IDriverBridge driver, bool ownsDriver = false)
    {
        _driver = driver;
        _ownsDriver = ownsDriver;
    }

    /// <summary>
    /// Initializes the connection and opens the driver.
    /// </summary>
    /// <returns>True if the driver was opened successfully.</returns>
    public bool Initialize()
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            if (!_driver.IsDriverInstalled)
            {
                Console.WriteLine("[AAPConnection] Driver not installed");
                return false;
            }

            if (!_driver.Open())
            {
                Console.WriteLine("[AAPConnection] Failed to open driver");
                return false;
            }

            Console.WriteLine("[AAPConnection] Driver opened successfully");
            return true;
        }
    }

    /// <summary>
    /// Connects to AirPods via L2CAP.
    /// </summary>
    /// <param name="bluetoothAddress">The Bluetooth address of the AirPods.</param>
    /// <param name="timeoutMs">Connection timeout in milliseconds.</param>
    /// <returns>True if connection succeeded.</returns>
    public async Task<bool> ConnectAsync(ulong bluetoothAddress, int timeoutMs = 5000)
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            if (_isConnecting)
            {
                Console.WriteLine("[AAPConnection] Connection already in progress");
                return false;
            }

            if (_driver.IsConnected && _connectedAddress == bluetoothAddress)
            {
                Console.WriteLine("[AAPConnection] Already connected to this device");
                return true;
            }

            _isConnecting = true;
        }

        try
        {
            ConnectionStateChanged?.Invoke(this, AAPConnectionState.Connecting);

            // Disconnect existing connection
            if (_driver.IsConnected)
            {
                _driver.Disconnect();
                await Task.Delay(100).ConfigureAwait(false);
            }

            // Open driver if needed
            if (!_driver.IsDriverInstalled || (_driver.Status == DriverStatus.NotInstalled))
            {
                Console.WriteLine("[AAPConnection] Driver not available");
                ConnectionStateChanged?.Invoke(this, AAPConnectionState.DriverNotInstalled);
                return false;
            }

            // Connect to L2CAP
            bool success = await Task.Run(() => _driver.Connect(bluetoothAddress, 0x1001, timeoutMs)).ConfigureAwait(false);

            if (!success)
            {
                Console.WriteLine($"[AAPConnection] Failed to connect to {bluetoothAddress:X12}");
                ConnectionStateChanged?.Invoke(this, AAPConnectionState.Disconnected);
                return false;
            }

            _connectedAddress = bluetoothAddress;
            Console.WriteLine($"[AAPConnection] Connected to {bluetoothAddress:X12}");

            // Start receive loop
            StartReceiveLoop();

            ConnectionStateChanged?.Invoke(this, AAPConnectionState.Connected);
            return true;
        }
        finally
        {
            lock (_lock)
            {
                _isConnecting = false;
            }
        }
    }

    /// <summary>
    /// Disconnects from the current AirPods.
    /// </summary>
    public void Disconnect()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            StopReceiveLoop();
            _driver.Disconnect();
            _connectedAddress = null;
            _lastBatteryInfo = null;
            _lastEarDetection = null;
            _currentNoiseMode = null;

            ConnectionStateChanged?.Invoke(this, AAPConnectionState.Disconnected);
            Console.WriteLine("[AAPConnection] Disconnected");
        }
    }

    /// <summary>
    /// Sends a command and waits for a response.
    /// </summary>
    /// <param name="opcode">The command opcode.</param>
    /// <param name="identifier">The command identifier.</param>
    /// <param name="data">Optional data payload.</param>
    /// <param name="timeoutMs">Response timeout in milliseconds.</param>
    /// <returns>The response packet, or null if timed out.</returns>
    public async Task<(AAPCommandOpcode Opcode, AAPIdentifier Identifier, byte[] Data)?> SendAndWaitForResponseAsync(
        AAPCommandOpcode opcode,
        AAPIdentifier identifier,
        byte[]? data = null,
        int timeoutMs = 3000)
    {
        ThrowIfDisposed();

        if (!_driver.IsConnected)
        {
            Console.WriteLine("[AAPConnection] Cannot send - not connected");
            return null;
        }

        var packet = AAPProtocol.BuildPacket(opcode, identifier, data);
        Console.WriteLine($"[AAPConnection] Sending: {AAPProtocol.FormatPacket(packet)}");

        bool sent = await _driver.SendAsync(packet, timeoutMs).ConfigureAwait(false);
        if (!sent)
        {
            Console.WriteLine("[AAPConnection] Send failed");
            return null;
        }

        // Wait for response with matching identifier
        var cts = new CancellationTokenSource(timeoutMs);
        (AAPCommandOpcode Opcode, AAPIdentifier Identifier, byte[] Data)? response = null;

        EventHandler<byte[]> handler = (s, rawData) =>
        {
            var parsed = AAPProtocol.ParsePacket(rawData);
            if (parsed.HasValue && parsed.Value.Identifier == identifier)
            {
                response = parsed;
                cts.Cancel();
            }
        };

        DataReceived += handler;

        try
        {
            await Task.Delay(timeoutMs, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when response is received
        }
        finally
        {
            DataReceived -= handler;
        }

        return response;
    }

    /// <summary>
    /// Sends a command without waiting for a response.
    /// </summary>
    public async Task<bool> SendAsync(AAPCommandOpcode opcode, AAPIdentifier identifier, byte[]? data = null, int timeoutMs = 3000)
    {
        ThrowIfDisposed();

        if (!_driver.IsConnected)
        {
            Console.WriteLine("[AAPConnection] Cannot send - not connected");
            return false;
        }

        var packet = AAPProtocol.BuildPacket(opcode, identifier, data);
        Console.WriteLine($"[AAPConnection] Sending: {AAPProtocol.FormatPacket(packet)}");

        return await _driver.SendAsync(packet, timeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the background receive loop.
    /// </summary>
    private void StartReceiveLoop()
    {
        lock (_lock)
        {
            if (_receiveLoopCts != null)
                return;

            _receiveLoopCts = new CancellationTokenSource();
            _receiveLoopTask = ReceiveLoopAsync(_receiveLoopCts.Token);
        }
    }

    /// <summary>
    /// Stops the background receive loop.
    /// </summary>
    private void StopReceiveLoop()
    {
        lock (_lock)
        {
            _receiveLoopCts?.Cancel();
            _receiveLoopCts?.Dispose();
            _receiveLoopCts = null;

            if (_receiveLoopTask != null)
            {
                try
                {
                    _receiveLoopTask.Wait(1000);
                }
                catch { }
                _receiveLoopTask = null;
            }
        }
    }

    /// <summary>
    /// Background receive loop that processes incoming AAP data.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[AAPConnection] Receive loop started");

        while (!cancellationToken.IsCancellationRequested && _driver.IsConnected)
        {
            try
            {
                var (bytesReceived, data) = await _driver.ReceiveAsync(1024, 1000, cancellationToken).ConfigureAwait(false);

                if (bytesReceived > 0 && data.Length > 0)
                {
                    Console.WriteLine($"[AAPConnection] Received: {AAPProtocol.FormatPacket(data)}");
                    ProcessReceivedData(data);
                    DataReceived?.Invoke(this, data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AAPConnection] Receive error: {ex.Message}");
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        Console.WriteLine("[AAPConnection] Receive loop ended");
    }

    /// <summary>
    /// Processes received AAP data and fires appropriate events.
    /// </summary>
    private void ProcessReceivedData(byte[] data)
    {
        var parsed = AAPProtocol.ParsePacket(data);
        if (!parsed.HasValue)
        {
            Console.WriteLine("[AAPConnection] Failed to parse received data");
            return;
        }

        var (opcode, identifier, payload) = parsed.Value;

        // Handle different message types
        switch (identifier)
        {
            case AAPIdentifier.BatteryLevel:
                var batteryInfo = AAPProtocol.ParseBatteryData(payload);
                if (batteryInfo != null)
                {
                    _lastBatteryInfo = batteryInfo;
                    BatteryUpdated?.Invoke(this, batteryInfo);
                }
                break;

            case AAPIdentifier.EarDetection:
                var earDetection = AAPProtocol.ParseEarDetectionData(payload);
                if (earDetection != null)
                {
                    _lastEarDetection = earDetection;
                    EarDetectionChanged?.Invoke(this, earDetection);
                }
                break;

            case AAPIdentifier.ListeningMode:
                if (payload.Length > 0)
                {
                    var mode = (AAPNoiseMode)payload[0];
                    if (Enum.IsDefined(typeof(AAPNoiseMode), mode))
                    {
                        _currentNoiseMode = mode;
                        NoiseModeChanged?.Invoke(this, mode);
                    }
                }
                break;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AAPConnection));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            StopReceiveLoop();
            Disconnect();

            if (_ownsDriver)
            {
                _driver.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~AAPConnection()
    {
        Dispose();
    }
}
