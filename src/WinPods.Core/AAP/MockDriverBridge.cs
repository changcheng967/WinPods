namespace WinPods.Core.AAP;

/// <summary>
/// Mock implementation of IDriverBridge for unit testing.
/// Simulates driver behavior without requiring the actual kernel driver.
/// </summary>
public class MockDriverBridge : IDriverBridge
{
    private readonly Queue<byte[]> _receiveQueue = new();
    private readonly List<byte[]> _sentData = new();
    private bool _isDisposed;
    private bool _isOpen;
    private bool _isConnected;
    private ulong _connectedAddress;
    private DriverStatus _simulatedStatus = DriverStatus.Installed;
    private bool _simulateConnectFailure;
    private bool _simulateSendFailure;
    private bool _simulateReceiveFailure;
    private Exception? _simulatedException;

    /// <inheritdoc/>
    public DriverStatus Status => _simulatedStatus;

    /// <inheritdoc/>
    public bool IsDriverInstalled => _simulatedStatus != DriverStatus.NotInstalled;

    /// <inheritdoc/>
    public bool IsConnected => _isConnected;

    /// <inheritdoc/>
    public ulong? ConnectedAddress => _isConnected ? _connectedAddress : null;

    /// <summary>
    /// Gets all data that was sent through this mock.
    /// </summary>
    public IReadOnlyList<byte[]> SentData => _sentData.AsReadOnly();

    /// <summary>
    /// Gets the number of pending receive buffers.
    /// </summary>
    public int PendingReceiveCount => _receiveQueue.Count;

    #region Simulation Configuration

    /// <summary>
    /// Simulates the driver being not installed.
    /// </summary>
    public void SimulateDriverNotInstalled()
    {
        _simulatedStatus = DriverStatus.NotInstalled;
    }

    /// <summary>
    /// Simulates the driver being installed.
    /// </summary>
    public void SimulateDriverInstalled()
    {
        _simulatedStatus = DriverStatus.Installed;
    }

    /// <summary>
    /// Simulates a connection failure.
    /// </summary>
    public void SimulateConnectFailure(bool fail = true)
    {
        _simulateConnectFailure = fail;
    }

    /// <summary>
    /// Simulates a send failure.
    /// </summary>
    public void SimulateSendFailure(bool fail = true)
    {
        _simulateSendFailure = fail;
    }

    /// <summary>
    /// Simulates a receive failure.
    /// </summary>
    public void SimulateReceiveFailure(bool fail = true)
    {
        _simulateReceiveFailure = fail;
    }

    /// <summary>
    /// Simulates an exception being thrown on the next operation.
    /// </summary>
    public void SimulateException(Exception ex)
    {
        _simulatedException = ex;
    }

    /// <summary>
    /// Queues data to be returned by Receive calls.
    /// </summary>
    public void QueueReceiveData(byte[] data)
    {
        _receiveQueue.Enqueue(data);
    }

    /// <summary>
    /// Queues an AAP response for a specific command.
    /// </summary>
    public void QueueAAPResponse(AAPCommandOpcode opcode, AAPIdentifier identifier, byte[]? data = null)
    {
        var response = AAPProtocol.BuildResponse(opcode, identifier, data);
        QueueReceiveData(response);
    }

    /// <summary>
    /// Clears all sent data history.
    /// </summary>
    public void ClearSentData()
    {
        _sentData.Clear();
    }

    /// <summary>
    /// Clears all queued receive data.
    /// </summary>
    public void ClearReceiveQueue()
    {
        _receiveQueue.Clear();
    }

    #endregion

    /// <inheritdoc/>
    public bool Open()
    {
        ThrowIfDisposed();
        ThrowIfSimulatedException();

        if (_simulatedStatus == DriverStatus.NotInstalled)
        {
            Console.WriteLine("[MockDriverBridge] Open failed: driver not installed");
            return false;
        }

        _isOpen = true;
        Console.WriteLine("[MockDriverBridge] Device opened");
        return true;
    }

    /// <inheritdoc/>
    public bool Connect(ulong bluetoothAddress, ushort psm = 0x1001, int timeoutMs = 5000)
    {
        ThrowIfDisposed();
        ThrowIfSimulatedException();

        if (!_isOpen && !Open())
            return false;

        if (_simulateConnectFailure)
        {
            Console.WriteLine("[MockDriverBridge] Connect failed: simulated failure");
            return false;
        }

        _isConnected = true;
        _connectedAddress = bluetoothAddress;
        _simulatedStatus = DriverStatus.Connected;

        Console.WriteLine($"[MockDriverBridge] Connected to {bluetoothAddress:X12}");
        return true;
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        ThrowIfDisposed();

        _isConnected = false;
        _connectedAddress = 0;
        _simulatedStatus = DriverStatus.Installed;

        Console.WriteLine("[MockDriverBridge] Disconnected");
    }

    /// <inheritdoc/>
    public bool Send(byte[] data, int timeoutMs = 5000)
    {
        ThrowIfDisposed();
        ThrowIfSimulatedException();

        if (!_isConnected)
        {
            Console.WriteLine("[MockDriverBridge] Send failed: not connected");
            return false;
        }

        if (_simulateSendFailure)
        {
            Console.WriteLine("[MockDriverBridge] Send failed: simulated failure");
            return false;
        }

        _sentData.Add((byte[])data.Clone());
        Console.WriteLine($"[MockDriverBridge] Sent {data.Length} bytes");
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> SendAsync(byte[] data, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return Send(data, timeoutMs);
    }

    /// <inheritdoc/>
    public int Receive(byte[] buffer, int timeoutMs = 5000)
    {
        ThrowIfDisposed();
        ThrowIfSimulatedException();

        if (!_isConnected)
        {
            Console.WriteLine("[MockDriverBridge] Receive failed: not connected");
            return -1;
        }

        if (_simulateReceiveFailure)
        {
            Console.WriteLine("[MockDriverBridge] Receive failed: simulated failure");
            return -1;
        }

        if (_receiveQueue.Count == 0)
        {
            Console.WriteLine("[MockDriverBridge] Receive failed: no data queued");
            return -1;
        }

        var data = _receiveQueue.Dequeue();
        int bytesToCopy = Math.Min(data.Length, buffer.Length);
        Buffer.BlockCopy(data, 0, buffer, 0, bytesToCopy);

        Console.WriteLine($"[MockDriverBridge] Received {bytesToCopy} bytes");
        return bytesToCopy;
    }

    /// <inheritdoc/>
    public async Task<(int bytesReceived, byte[] data)> ReceiveAsync(int maxBytes = 1024, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[maxBytes];
        int received = Receive(buffer, timeoutMs);

        if (received < 0)
            return (0, Array.Empty<byte>());

        var result = new byte[received];
        Buffer.BlockCopy(buffer, 0, result, 0, received);
        return (received, result);
    }

    /// <inheritdoc/>
    public AAPConnectionState GetConnectionState()
    {
        ThrowIfDisposed();

        if (_simulatedStatus == DriverStatus.NotInstalled)
            return AAPConnectionState.DriverNotInstalled;

        if (!_isOpen)
            return AAPConnectionState.DriverInstalled;

        if (!_isConnected)
            return AAPConnectionState.Disconnected;

        return AAPConnectionState.Connected;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isOpen = false;
        _isConnected = false;
        _receiveQueue.Clear();
        _sentData.Clear();

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(MockDriverBridge));
    }

    private void ThrowIfSimulatedException()
    {
        if (_simulatedException != null)
        {
            var ex = _simulatedException;
            _simulatedException = null;
            throw ex;
        }
    }
}
