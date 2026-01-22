namespace WinPods.Core.Models
{
    /// <summary>
    /// Represents a raw AirPods advertisement received via BLE.
    /// </summary>
    public record AirPodsAdvertisement
    {
        /// <summary>
        /// Device model identifier.
        /// </summary>
        public AirPodsModel Model { get; init; }

        /// <summary>
        /// Device color.
        /// </summary>
        public DeviceColor Color { get; init; }

        /// <summary>
        /// Left pod battery (0-100).
        /// </summary>
        public byte LeftBattery { get; init; }

        /// <summary>
        /// Right pod battery (0-100).
        /// </summary>
        public byte RightBattery { get; init; }

        /// <summary>
        /// Case battery (0-100).
        /// </summary>
        public byte CaseBattery { get; init; }

        /// <summary>
        /// Whether left pod is charging.
        /// </summary>
        public bool LeftCharging { get; init; }

        /// <summary>
        /// Whether right pod is charging.
        /// </summary>
        public bool RightCharging { get; init; }

        /// <summary>
        /// Whether case is charging.
        /// </summary>
        public bool CaseCharging { get; init; }

        /// <summary>
        /// Lid open counter (increments on case open).
        /// </summary>
        public byte LidOpenCount { get; init; }

        /// <summary>
        /// Raw RSSI signal strength in dBm.
        /// </summary>
        public short RSSI { get; init; }

        /// <summary>
        /// Bluetooth address (changes randomly!).
        /// </summary>
        public ulong BluetoothAddress { get; init; }

        /// <summary>
        /// Timestamp when this advertisement was received.
        /// </summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// Status byte from the protocol.
        /// </summary>
        public byte Status { get; init; }

        /// <summary>
        /// Determines which side is broadcasting based on status byte.
        /// </summary>
        public bool IsLeftBroadcast => (Status & 0x80) != 0;

        /// <summary>
        /// Gets human-readable model name.
        /// </summary>
        public string ModelName => DeviceModelHelper.GetDisplayName(Model);

        /// <summary>
        /// Gets battery status as a structured object.
        /// </summary>
        public BatteryStatus BatteryStatus => new()
        {
            Left = BatteryInfo.FromRawValue((byte)(LeftBattery / 10), LeftCharging),
            Right = BatteryInfo.FromRawValue((byte)(RightBattery / 10), RightCharging),
            Case = BatteryInfo.FromRawValue((byte)(CaseBattery / 10), CaseCharging)
        };

        /// <summary>
        /// Checks if this advertisement is recent (within 30 seconds).
        /// </summary>
        public bool IsRecent => (DateTime.UtcNow - Timestamp).TotalSeconds < 30;
    }

    /// <summary>
    /// Represents the merged state of AirPods from both left and right advertisements.
    /// </summary>
    public record AirPodsState
    {
        /// <summary>
        /// Device model.
        /// </summary>
        public AirPodsModel Model { get; init; }

        /// <summary>
        /// Device color.
        /// </summary>
        public DeviceColor Color { get; init; }

        /// <summary>
        /// Merged battery status.
        /// </summary>
        public BatteryStatus Battery { get; init; } = new();

        /// <summary>
        /// Most recent lid open count.
        /// </summary>
        public byte LidOpenCount { get; init; }

        /// <summary>
        /// Most recent RSSI.
        /// </summary>
        public short RSSI { get; init; }

        /// <summary>
        /// Last state update timestamp.
        /// </summary>
        public DateTime LastUpdate { get; init; }

        /// <summary>
        /// Device position status (in ear, in case, etc.).
        /// </summary>
        public PodStatus PodStatus { get; init; }

        /// <summary>
        /// Ear detection state (in ear, in case, etc.).
        /// </summary>
        public EarDetectionState EarDetection { get; init; } = EarDetectionState.BothInCase;

        /// <summary>
        /// Raw status byte from BLE advertisement.
        /// </summary>
        public byte StatusByte { get; init; }

        /// <summary>
        /// Bluetooth address of the device (most recent).
        /// </summary>
        public ulong? BluetoothAddress { get; init; }

        /// <summary>
        /// Human-readable model name.
        /// </summary>
        public string ModelName => DeviceModelHelper.GetDisplayName(Model);

        /// <summary>
        /// Checks if the device is connected and responsive.
        /// </summary>
        public bool IsConnected => (DateTime.UtcNow - LastUpdate).TotalSeconds < 60;

        /// <summary>
        /// Creates an unknown/disconnected state.
        /// </summary>
        public static AirPodsState Disconnected => new()
        {
            Model = AirPodsModel.Unknown,
            Color = DeviceColor.White,
            Battery = new BatteryStatus(),
            LidOpenCount = 0,
            RSSI = -100,
            LastUpdate = DateTime.UtcNow.AddMinutes(-10),
            PodStatus = PodStatus.None,
            EarDetection = EarDetectionState.BothInCase,
            StatusByte = 0x00
        };

        public override string ToString()
        {
            return $"{ModelName} | {Battery} | RSSI: {RSSI} dBm";
        }
    }
}
