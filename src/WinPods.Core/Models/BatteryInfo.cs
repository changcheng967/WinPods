namespace WinPods.Core.Models
{
    /// <summary>
    /// Battery information for a single pod or case.
    /// </summary>
    public record BatteryInfo
    {
        /// <summary>
        /// Battery percentage (0-100).
        /// </summary>
        public byte Percentage { get; init; }

        /// <summary>
        /// Whether the device is currently charging.
        /// </summary>
        public bool IsCharging { get; init; }

        /// <summary>
        /// Whether the battery status is available/known.
        /// </summary>
        public bool IsAvailable { get; init; }

        /// <summary>
        /// Creates an unknown/unavailable battery status.
        /// </summary>
        public static BatteryInfo Unknown => new()
        {
            Percentage = 0,
            IsCharging = false,
            IsAvailable = false
        };

        /// <summary>
        /// Creates a battery info from raw values.
        /// </summary>
        public static BatteryInfo FromRawValue(byte value, bool isCharging)
        {
            // 0x0F (15) means "unknown" or "disconnected"
            if (value == 0x0F || value > 10)
            {
                return Unknown;
            }

            return new BatteryInfo
            {
                Percentage = (byte)(value * 10),  // Convert 0-10 to 0-100
                IsCharging = isCharging,
                IsAvailable = true
            };
        }

        public override string ToString()
        {
            if (!IsAvailable)
                return "N/A";

            return $"{Percentage}%{(IsCharging ? " (Charging)" : "")}";
        }
    }

    /// <summary>
    /// Complete battery status for all components.
    /// </summary>
    public record BatteryStatus
    {
        /// <summary>
        /// Left pod battery.
        /// </summary>
        public BatteryInfo Left { get; init; } = BatteryInfo.Unknown;

        /// <summary>
        /// Right pod battery.
        /// </summary>
        public BatteryInfo Right { get; init; } = BatteryInfo.Unknown;

        /// <summary>
        /// Case battery.
        /// </summary>
        public BatteryInfo Case { get; init; } = BatteryInfo.Unknown;

        /// <summary>
        /// Gets the average battery percentage of available pods.
        /// </summary>
        public byte AveragePodBattery
        {
            get
            {
                var available = new List<byte>();

                if (Left.IsAvailable)
                    available.Add(Left.Percentage);

                if (Right.IsAvailable)
                    available.Add(Right.Percentage);

                return available.Count > 0
                    ? (byte)available.Average(x => (double)x)
                    : (byte)0;
            }
        }

        /// <summary>
        /// Checks if any component has low battery (<= 20%).
        /// </summary>
        public bool IsLowBattery =>
            (Left.IsAvailable && Left.Percentage <= 20) ||
            (Right.IsAvailable && Right.Percentage <= 20) ||
            (Case.IsAvailable && Case.Percentage <= 20);

        /// <summary>
        /// Checks if any component is charging.
        /// </summary>
        public bool IsAnyCharging =>
            Left.IsCharging || Right.IsCharging || Case.IsCharging;

        public override string ToString()
        {
            return $"L: {Left} | R: {Right} | Case: {Case}";
        }
    }
}
