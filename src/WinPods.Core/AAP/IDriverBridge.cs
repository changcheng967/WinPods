namespace WinPods.Core.AAP;

/// <summary>
/// Interface for communicating with the WinPodsAAP kernel driver.
/// Allows for mock implementations in unit tests.
/// </summary>
public interface IDriverBridge : IDisposable
{
    /// <summary>
    /// Gets the current driver status.
    /// </summary>
    DriverStatus Status { get; }

    /// <summary>
    /// Gets whether the driver is installed and available.
    /// </summary>
    bool IsDriverInstalled { get; }

    /// <summary>
    /// Gets whether there is an active L2CAP connection.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the Bluetooth address of the currently connected device.
    /// </summary>
    ulong? ConnectedAddress { get; }

    /// <summary>
    /// Opens the driver device interface.
    /// </summary>
    /// <returns>True if the driver device was opened successfully.</returns>
    bool Open();

    /// <summary>
    /// Connects to an L2CAP channel on the specified device.
    /// </summary>
    /// <param name="bluetoothAddress">The Bluetooth address of the device.</param>
    /// <param name="psm">The Protocol/Service Multiplexer (0x1001 for AAP).</param>
    /// <param name="timeoutMs">Connection timeout in milliseconds.</param>
    /// <returns>True if connection succeeded.</returns>
    bool Connect(ulong bluetoothAddress, ushort psm = 0x1001, int timeoutMs = 5000);

    /// <summary>
    /// Disconnects from the current L2CAP channel.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Sends raw data over the L2CAP channel.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="timeoutMs">Send timeout in milliseconds.</param>
    /// <returns>True if send succeeded.</returns>
    bool Send(byte[] data, int timeoutMs = 5000);

    /// <summary>
    /// Sends data asynchronously.
    /// </summary>
    Task<bool> SendAsync(byte[] data, int timeoutMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives raw data from the L2CAP channel.
    /// </summary>
    /// <param name="buffer">Buffer to receive data into.</param>
    /// <param name="timeoutMs">Receive timeout in milliseconds.</param>
    /// <returns>Number of bytes received, or -1 on error.</returns>
    int Receive(byte[] buffer, int timeoutMs = 5000);

    /// <summary>
    /// Receives data asynchronously.
    /// </summary>
    Task<(int bytesReceived, byte[] data)> ReceiveAsync(int maxBytes = 1024, int timeoutMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current connection status from the driver.
    /// </summary>
    /// <returns>The current driver connection state.</returns>
    AAPConnectionState GetConnectionState();
}
