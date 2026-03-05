namespace WinPods.Core.AAP;

/// <summary>
/// Noise control modes for AirPods (AAP protocol values).
/// </summary>
public enum AAPNoiseMode : byte
{
    Off = 0x01,
    NoiseCancellation = 0x02,
    Transparency = 0x03,
    Adaptive = 0x04
}

/// <summary>
/// AAP opcodes for command messages.
/// </summary>
public enum AAPCommandOpcode : ushort
{
    ControlCommand = 0x0009,    // General control commands
    ConfigurationSet = 0x0014,  // Configuration changes
    Acknowledgement = 0x0004    // Acknowledge a message
}

/// <summary>
/// AAP identifiers for specific commands.
/// </summary>
public enum AAPIdentifier : byte
{
    ListeningMode = 0x0D,              // Noise control mode
    ConversationAwareness = 0x1D,      // Conversational awareness toggle
    EarDetection = 0x0B,               // Ear detection status
    BatteryLevel = 0x00,               // Battery level query
    DeviceInfo = 0x44,                 // Device information
    FirmwareVersion = 0x01,            // Firmware version
    SerialNumber = 0x02                // Serial number
}

/// <summary>
/// Connection states for the AAP driver.
/// </summary>
public enum AAPConnectionState
{
    /// <summary>Driver is not installed.</summary>
    DriverNotInstalled,
    /// <summary>Driver is installed but device is not open.</summary>
    DriverInstalled,
    /// <summary>Attempting to connect to L2CAP channel.</summary>
    Connecting,
    /// <summary>Connected to AirPods via L2CAP.</summary>
    Connected,
    /// <summary>Connection failed or lost.</summary>
    Disconnected,
    /// <summary>An error occurred.</summary>
    Error
}

/// <summary>
/// Status of the WinPodsAAP driver.
/// </summary>
public enum DriverStatus
{
    /// <summary>Driver is not installed on the system.</summary>
    NotInstalled,
    /// <summary>Driver is installed but not connected to any device.</summary>
    Installed,
    /// <summary>Driver is connected to an L2CAP channel.</summary>
    Connected,
    /// <summary>Driver encountered an error.</summary>
    Error
}

/// <summary>
/// Result of an AAP command.
/// </summary>
public enum AAPCommandResult
{
    Success,
    DriverNotInstalled,
    NotConnected,
    Timeout,
    InvalidResponse,
    ProtocolError,
    IOError
}

/// <summary>
/// Detailed device information from AAP.
/// </summary>
public class AAPDeviceData
{
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? DeviceName { get; set; }
    public ushort ModelId { get; set; }
    public byte RegionCode { get; set; }
}

/// <summary>
/// Precise battery information from AAP (more accurate than BLE advertisements).
/// </summary>
public class AAPBatteryInfo
{
    public byte LeftPercentage { get; set; }
    public byte RightPercentage { get; set; }
    public byte CasePercentage { get; set; }
    public bool LeftCharging { get; set; }
    public bool RightCharging { get; set; }
    public bool CaseCharging { get; set; }
    public DateTime LastUpdate { get; set; }
}

/// <summary>
/// Ear detection state from AAP notifications.
/// </summary>
public class AAPEarDetectionInfo
{
    public bool LeftInEar { get; set; }
    public bool RightInEar { get; set; }
    public DateTime LastUpdate { get; set; }
}

/// <summary>
/// Conversational awareness settings.
/// </summary>
public class AAPConversationalAwarenessSettings
{
    public bool Enabled { get; set; }
    public byte VolumeLevel { get; set; } // 0-100
}

/// <summary>
/// Input structure for L2CAP connection.
/// </summary>
public struct L2CAPConnectInput
{
    /// <summary>Bluetooth device address (6 bytes).</summary>
    public ulong BluetoothAddress;
    /// <summary>Protocol/Service Multiplexer (0x1001 for AAP).</summary>
    public ushort PSM;
}

/// <summary>
/// Output structure for L2CAP connection.
/// </summary>
public struct L2CAPConnectOutput
{
    /// <summary>Whether connection succeeded.</summary>
    public int Success;
    /// <summary>Channel ID if successful.</summary>
    public ushort ChannelId;
}

/// <summary>
/// Input structure for send/receive operations.
/// </summary>
public struct L2CAPTransferInput
{
    /// <summary>Number of bytes to transfer.</summary>
    public int BufferSize;
    /// <summary>Timeout in milliseconds (0 = infinite).</summary>
    public int TimeoutMs;
}

/// <summary>
/// Output structure for receive operations.
/// </summary>
public struct L2CAPReceiveOutput
{
    /// <summary>Number of bytes received.</summary>
    public int BytesReceived;
    /// <summary>Error code (0 = success).</summary>
    public int ErrorCode;
}

/// <summary>
/// Driver status output.
/// </summary>
public struct DriverStatusOutput
{
    /// <summary>Connection state.</summary>
    public int ConnectionState;
    /// <summary>Bluetooth address of connected device.</summary>
    public ulong ConnectedAddress;
}
