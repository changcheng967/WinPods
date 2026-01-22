using WinPods.Core.Bluetooth;
using WinPods.Core.Models;

namespace WinPods.Core.StateManagement
{
    /// <summary>
    /// Manages AirPods device state by tracking and merging dual-pod advertisements.
    /// </summary>
    public class DeviceManager : IDisposable
    {
        private readonly object _lock = new();

        // Track left and right advertisements separately
        private AirPodsAdvertisement? _leftAdvertisement;
        private AirPodsAdvertisement? _rightAdvertisement;
        private AirPodsState? _currentState;
        private byte? _lastLidOpenCount;

        private BleScanner? _scanner;
        private bool _isDisposed;

        /// <summary>
        /// Event raised when the device state changes.
        /// </summary>
        public event EventHandler<AirPodsState>? StateChanged;

        /// <summary>
        /// Event raised when the case lid is opened.
        /// </summary>
        public event EventHandler? LidOpened;

        /// <summary>
        /// Gets the current device state.
        /// </summary>
        public AirPodsState? CurrentState
        {
            get
            {
                lock (_lock)
                {
                    return _currentState;
                }
            }
        }

        /// <summary>
        /// Gets whether a device is currently connected and tracked.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _currentState != null && _currentState.IsConnected;
                }
            }
        }

        /// <summary>
        /// Initializes the device manager and starts scanning.
        /// </summary>
        public void Initialize()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(DeviceManager));
            }

            lock (_lock)
            {
                if (_scanner != null)
                {
                    return;  // Already initialized
                }

                // Create and start the BLE scanner
                _scanner = new BleScanner();
                _scanner.AdvertisementReceived += OnAdvertisementReceived;
                _scanner.StatusChanged += OnScannerStatusChanged;
                _scanner.Start();
            }
        }

        /// <summary>
        /// Stops scanning and releases resources.
        /// </summary>
        public void Shutdown()
        {
            if (_isDisposed)
            {
                return;
            }

            lock (_lock)
            {
                if (_scanner != null)
                {
                    _scanner.AdvertisementReceived -= OnAdvertisementReceived;
                    _scanner.StatusChanged -= OnScannerStatusChanged;
                    _scanner.Stop();
                    _scanner.Dispose();
                    _scanner = null;
                }

                // Clear state
                _leftAdvertisement = null;
                _rightAdvertisement = null;
                _currentState = null;
                _lastLidOpenCount = null;
            }
        }

        /// <summary>
        /// Handles incoming BLE advertisements.
        /// </summary>
        private void OnAdvertisementReceived(object? sender, AirPodsAdvertisement adv)
        {
            lock (_lock)
            {
                try
                {
                    // Determine which side is broadcasting
                    bool isLeftBroadcast = adv.IsLeftBroadcast;

                    // Get the previous advertisement from this side
                    var previousAdv = isLeftBroadcast ? _leftAdvertisement : _rightAdvertisement;

                    // Validate this is the same device (random MAC addresses!)
                    if (!ValidateAdvertisement(adv, previousAdv))
                    {
                        return;  // Skip this advertisement
                    }

                    // Update the appropriate side
                    if (isLeftBroadcast)
                    {
                        _leftAdvertisement = adv;
                    }
                    else
                    {
                        _rightAdvertisement = adv;
                    }

                    // Check for lid open event
                    CheckLidOpen(adv);

                    // Merge state from both sides
                    var newState = MergeState();
                    if (newState != null)
                    {
                        // Check if state actually changed
                        if (_currentState == null || !StatesEqual(_currentState, newState))
                        {
                            _currentState = newState;
                            StateChanged?.Invoke(this, newState);
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore errors and continue scanning
                }
            }
        }

        /// <summary>
        /// Validates that a new advertisement is from the same device.
        /// </summary>
        private bool ValidateAdvertisement(
            AirPodsAdvertisement newAdv,
            AirPodsAdvertisement? oldAdv)
        {
            // First advertisement from this side is always valid
            if (oldAdv == null)
            {
                return true;
            }

            // Model must match
            if (newAdv.Model != oldAdv.Model)
            {
                return false;
            }

            // Battery levels shouldn't jump more than 10% between updates
            // (This handles the random MAC address challenge)
            // Skip validation if value is 0xFF (unknown)
            if (newAdv.LeftBattery != 0xFF && oldAdv.LeftBattery != 0xFF)
            {
                if (Math.Abs(newAdv.LeftBattery - oldAdv.LeftBattery) > 10)
                {
                    return false;
                }
            }

            if (newAdv.RightBattery != 0xFF && oldAdv.RightBattery != 0xFF)
            {
                if (Math.Abs(newAdv.RightBattery - oldAdv.RightBattery) > 10)
                {
                    return false;
                }
            }

            if (newAdv.CaseBattery != 0xFF && oldAdv.CaseBattery != 0xFF)
            {
                if (Math.Abs(newAdv.CaseBattery - oldAdv.CaseBattery) > 10)
                {
                    return false;
                }
            }

            // RSSI shouldn't jump more than 50 dBm
            if (Math.Abs(newAdv.RSSI - oldAdv.RSSI) > 50)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the case lid was opened.
        /// </summary>
        private void CheckLidOpen(AirPodsAdvertisement adv)
        {
            if (_lastLidOpenCount.HasValue && adv.LidOpenCount != _lastLidOpenCount.Value)
            {
                // Lid was opened!
                LidOpened?.Invoke(this, EventArgs.Empty);
            }

            _lastLidOpenCount = adv.LidOpenCount;
        }

        /// <summary>
        /// Merges state from left and right advertisements.
        /// </summary>
        private AirPodsState? MergeState()
        {
            // Need at least one advertisement
            if (_leftAdvertisement == null && _rightAdvertisement == null)
            {
                return null;
            }

            // Determine which advertisement is more recent
            var left = _leftAdvertisement;
            var right = _rightAdvertisement;

            // Use the most recent advertisement as the base
            var baseAdv = (left?.Timestamp ?? DateTime.MinValue) > (right?.Timestamp ?? DateTime.MinValue)
                ? left
                : right;

            if (baseAdv == null)
            {
                return null;
            }

            // Merge battery data (prioritize more recent data for each component)
            byte leftBattery = GetMostRecentValue(
                left?.LeftBattery,
                right?.LeftBattery,
                left?.Timestamp,
                right?.Timestamp,
                (byte)0);

            byte rightBattery = GetMostRecentValue(
                left?.RightBattery,
                right?.RightBattery,
                left?.Timestamp,
                right?.Timestamp,
                (byte)0);

            byte caseBattery = GetMostRecentValue(
                left?.CaseBattery,
                right?.CaseBattery,
                left?.Timestamp,
                right?.Timestamp,
                (byte)0);

            // Merge charging flags
            bool leftCharging = left?.LeftCharging == true || right?.LeftCharging == true;
            bool rightCharging = left?.RightCharging == true || right?.RightCharging == true;
            bool caseCharging = left?.CaseCharging == true || right?.CaseCharging == true;

            // Determine pod status from raw status bytes
            PodStatus status = PodStatus.None;

            // Parse status from available advertisements
            // Status byte format (after removing broadcast bit):
            // - Low nibble (bits 0-3): left pod status
            // - High nibble (bits 4-6): right pod status
            // Common values: 0x3 = in ear, 0x5 = in case

            byte leftStatusRaw = 0;
            byte rightStatusRaw = 0;

            // Extract status from each advertisement (bit 7 indicates broadcast side)
            if (left != null)
            {
                byte leftAdvStatus = (byte)(left.Status & 0x7F); // Remove broadcast bit
                if ((left.Status & 0x80) == 0) // Left pod broadcasting
                {
                    leftStatusRaw = (byte)(leftAdvStatus & 0x0F); // Low nibble is left
                }
                else // Right pod broadcasting in left advertisement?
                {
                    rightStatusRaw = (byte)((leftAdvStatus >> 4) & 0x07); // High nibble is right
                }
            }

            if (right != null)
            {
                byte rightAdvStatus = (byte)(right.Status & 0x7F); // Remove broadcast bit
                if ((right.Status & 0x80) == 0) // Left pod broadcasting
                {
                    leftStatusRaw = (byte)(rightAdvStatus & 0x0F); // Low nibble is left
                }
                else // Right pod broadcasting
                {
                    rightStatusRaw = (byte)((rightAdvStatus >> 4) & 0x07); // High nibble is right
                }
            }

            // Parse individual earbud states
            // 0x3 = in ear, 0x5 = in case
            bool leftInEar = leftStatusRaw == 0x03;
            bool rightInEar = rightStatusRaw == 0x03;
            bool leftInCase = leftStatusRaw == 0x05;
            bool rightInCase = rightStatusRaw == 0x05;

            // Build PodStatus flags
            if (leftInEar)
                status |= PodStatus.LeftInEar;
            if (rightInEar)
                status |= PodStatus.RightInEar;
            if (leftInCase)
                status |= PodStatus.LeftInCase;
            if (rightInCase)
                status |= PodStatus.RightInCase;

            // Calculate combined status byte for ear detection
            // Format: [bit 7: 0] [bits 4-6: right status] [bits 0-3: left status]
            byte combinedStatus = (byte)((rightStatusRaw << 4) | leftStatusRaw);

            // Parse ear detection state
            var earDetection = EarStatusParser.ParseFromStatus(combinedStatus);

            // Create battery info, handling 0xFF (unknown) properly
            BatteryInfo leftBatteryInfo = leftBattery == 0xFF
                ? BatteryInfo.Unknown
                : BatteryInfo.FromRawValue((byte)(leftBattery / 10), leftCharging);

            BatteryInfo rightBatteryInfo = rightBattery == 0xFF
                ? BatteryInfo.Unknown
                : BatteryInfo.FromRawValue((byte)(rightBattery / 10), rightCharging);

            BatteryInfo caseBatteryInfo = caseBattery == 0xFF
                ? BatteryInfo.Unknown
                : BatteryInfo.FromRawValue((byte)(caseBattery / 10), caseCharging);

            // Get most recent Bluetooth address
            ulong? bluetoothAddress = null;
            if (left?.BluetoothAddress != null && right?.BluetoothAddress != null)
            {
                bluetoothAddress = (left.Timestamp > right.Timestamp) ? left.BluetoothAddress : right.BluetoothAddress;
            }
            else
            {
                bluetoothAddress = left?.BluetoothAddress ?? right?.BluetoothAddress;
            }

            return new AirPodsState
            {
                Model = baseAdv.Model,
                Color = baseAdv.Color,
                Battery = new BatteryStatus
                {
                    Left = leftBatteryInfo,
                    Right = rightBatteryInfo,
                    Case = caseBatteryInfo
                },
                LidOpenCount = baseAdv.LidOpenCount,
                RSSI = baseAdv.RSSI,
                LastUpdate = baseAdv.Timestamp,
                PodStatus = status,
                EarDetection = earDetection,
                StatusByte = combinedStatus,
                BluetoothAddress = bluetoothAddress
            };
        }

        /// <summary>
        /// Gets the most recent value between two advertisements.
        /// </summary>
        private T GetMostRecentValue<T>(
            T? leftValue,
            T? rightValue,
            DateTime? leftTime,
            DateTime? rightTime,
            T defaultValue) where T : struct
        {
            if (leftValue.HasValue && rightValue.HasValue)
            {
                return (leftTime ?? DateTime.MinValue) > (rightTime ?? DateTime.MinValue)
                    ? leftValue.Value
                    : rightValue.Value;
            }

            return leftValue ?? rightValue ?? defaultValue;
        }

        /// <summary>
        /// Compares two states for equality.
        /// </summary>
        private bool StatesEqual(AirPodsState a, AirPodsState b)
        {
            if (a.Model != b.Model) return false;
            if (a.RSSI != b.RSSI) return false;
            if (a.LidOpenCount != b.LidOpenCount) return false;

            // Compare battery percentages
            if (a.Battery.Left.Percentage != b.Battery.Left.Percentage) return false;
            if (a.Battery.Right.Percentage != b.Battery.Right.Percentage) return false;
            if (a.Battery.Case.Percentage != b.Battery.Case.Percentage) return false;

            // Compare charging status
            if (a.Battery.Left.IsCharging != b.Battery.Left.IsCharging) return false;
            if (a.Battery.Right.IsCharging != b.Battery.Right.IsCharging) return false;
            if (a.Battery.Case.IsCharging != b.Battery.Case.IsCharging) return false;

            return true;
        }

        /// <summary>
        /// Handles scanner status changes.
        /// </summary>
        private void OnScannerStatusChanged(object? sender, BleScannerStatus status)
        {
            // Could expose scanner status if needed
        }

        /// <summary>
        /// Disposes the device manager.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                Shutdown();
                _isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
