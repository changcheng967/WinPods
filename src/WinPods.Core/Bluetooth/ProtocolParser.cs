using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth.Advertisement;
using WinPods.Core.Models;
using System.IO;

namespace WinPods.Core.Bluetooth
{
    /// <summary>
    /// Parses Apple Proximity Pairing messages from BLE advertisements.
    /// </summary>
    public static class ProtocolParser
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "WinPods.log");

        private static readonly object _logLock = new object();

        static ProtocolParser()
        {
            // Initialize log file if it doesn't exist
            lock (_logLock)
            {
                try
                {
                    if (!File.Exists(LogFilePath))
                    {
                        File.WriteAllText(LogFilePath, $"=== WinPods Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        private static void Log(string message)
        {
            lock (_logLock)
            {
                try
                {
                    string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    File.AppendAllText(LogFilePath, timestampedMessage + "\n");
                    System.Diagnostics.Debug.WriteLine(timestampedMessage);
                }
                catch
                {
                    // Ignore logging errors
                }
            }
        }

        // Protocol constants
        private const ushort AppleCompanyId = 0x004C;
        private const byte ProximityPairingType = 0x07;
        private const byte Prefix = 0x01;
        private const byte Suffix = 0x00;
        private const int MinimumPacketLength = 27;

        /// <summary>
        /// Apple Continuity Protocol packet structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AirPodsPacket
        {
            public byte Prefix;              // 0x01
            public ushort ModelId;           // Device model (little-endian)
            public byte Status;              // Status flags
            public byte BatteryData;         // Current/Other batteries
            public byte ChargingAndCase;     // Charging flags + Case battery
            public byte LidOpenCount;        // Lid open counter
            public byte Color;               // Device color
            public byte Suffix;              // 0x00
            // Followed by encrypted payload (16 bytes)
        }

        /// <summary>
        /// Attempts to parse an AirPods advertisement from BLE manufacturer data.
        /// </summary>
        /// <param name="data">Raw manufacturer data bytes</param>
        /// <param name="args">BLE advertisement event arguments</param>
        /// <returns>Parsed advertisement or null if invalid</returns>
        public static AirPodsAdvertisement? Parse(byte[] data, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                // Validate minimum length
                if (data == null || data.Length < MinimumPacketLength)
                {
                    return null;
                }

                // Check for Proximity Pairing message structure
                // Format: [Type (1 byte)] [Length (1 byte)] [Prefix (1 byte)] [Data...]
                int offset = 0;

                while (offset < data.Length - 2)
                {
                    byte msgType = data[offset];
                    byte msgLength = data[offset + 1];

                    // Look for Proximity Pairing type
                    if (msgType == ProximityPairingType)
                    {
                        // Extract the packet data (skip type and length bytes)
                        int packetStart = offset + 2;

                        if (packetStart + 9 > data.Length)  // Minimum needed bytes
                        {
                            break;
                        }

                        // Parse the packet
                        var packet = MemoryMarshal.Read<AirPodsPacket>(
                            data.AsSpan(packetStart));

                        // Validate prefix and suffix
                        if (packet.Prefix != Prefix || packet.Suffix != Suffix)
                        {
                            break;
                        }

                        // Decode the advertisement
                        return DecodeAdvertisement(packet, args);
                    }

                    // Move to next message
                    offset += 2 + msgLength;
                }

                return null;
            }
            catch (Exception)
            {
                // Return null on any parsing error
                return null;
            }
        }

        /// <summary>
        /// Decodes an AirPods advertisement from a parsed packet.
        /// </summary>
        private static AirPodsAdvertisement DecodeAdvertisement(
            AirPodsPacket packet,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Determine which side is broadcasting (bit 7 of Status)
            bool isLeftBroadcast = (packet.Status & 0x80) != 0;

            // Extract raw battery data (handle flipped logic)
            // BatteryData byte: [High nibble: current pod] [Low nibble: other pod]
            byte currentBatteryRaw = (byte)(packet.BatteryData & 0x0F);
            byte otherBatteryRaw = (byte)((packet.BatteryData >> 4) & 0x0F);

            // 0x0F (15) means "unknown" or "not available" - don't multiply by 10
            byte currentBattery = currentBatteryRaw == 0x0F ? (byte)0xFF : (byte)(currentBatteryRaw * 10);
            byte otherBattery = otherBatteryRaw == 0x0F ? (byte)0xFF : (byte)(otherBatteryRaw * 10);

            // Map to left/right based on broadcast side
            byte leftBattery = isLeftBroadcast ? currentBattery : otherBattery;
            byte rightBattery = isLeftBroadcast ? otherBattery : currentBattery;

            // Extract charging flags and case battery
            // ChargingAndCase byte: [Bits 4-7: case battery] [Bit 2: case charging]
            //                        [Bit 1: right charging] [Bit 0: left charging]
            bool leftCharging = (packet.ChargingAndCase & 0x01) != 0;
            bool rightCharging = (packet.ChargingAndCase & 0x02) != 0;
            bool caseCharging = (packet.ChargingAndCase & 0x04) != 0;

            // Extract case battery from high nibble (bits 4-7)
            byte caseBatteryRaw = (byte)((packet.ChargingAndCase >> 4) & 0x0F);

            // Case battery logic:
            // - 0x0F (15) means "unknown" or "not available"
            // - Values >= 0x0A (10) should be capped at 100%
            // - Otherwise: (raw + 1) * 10 to match iPhone display
            byte caseBattery = caseBatteryRaw switch
            {
                0x0F => 0xFF,  // Unknown/not available
                >= 0x0A => 100,  // Cap at 100% for values >= 10
                _ => (byte)((caseBatteryRaw + 1) * 10)  // (raw + 1) * 10 for accurate % display
            };

            // Parse model ID (little-endian)
            ushort modelId = packet.ModelId;
            var model = DeviceModelHelper.FromModelId(modelId);

            // Debug logging with ALL raw values for debugging
            string logMessage = $"ModelID: 0x{modelId:X4} ({model}) | " +
                $"Battery: L:{(leftBattery == 0xFF ? "--" : leftBattery.ToString())}% " +
                $"R:{(rightBattery == 0xFF ? "--" : rightBattery.ToString())}% " +
                $"C:{(caseBattery == 0xFF ? "--" : caseBattery.ToString())}% | " +
                $"Raw: L:0x{currentBatteryRaw:X} R:0x{otherBatteryRaw:X} C:0x{caseBatteryRaw:X} | " +
                $"ChargingByte: 0x{packet.ChargingAndCase:X2} (L:{leftCharging} R:{rightCharging} C:{caseCharging}) | " +
                $"Status: 0x{packet.Status:X2} | " +
                $"RSSI: {args.RawSignalStrengthInDBm} dBm";
            Log($"[PROTOCOL] {logMessage}");

            // Additional details for unknown models
            if (model == AirPodsModel.Unknown)
            {
                Log($"*** UNKNOWN MODEL DETECTED *** ModelID: 0x{modelId:X4} (decimal: {modelId}) - Please add this to DeviceModels.cs");
            }

            return new AirPodsAdvertisement
            {
                Model = model,
                Color = (DeviceColor)packet.Color,
                LeftBattery = leftBattery,
                RightBattery = rightBattery,
                CaseBattery = caseBattery,
                LeftCharging = leftCharging,
                RightCharging = rightCharging,
                CaseCharging = caseCharging,
                LidOpenCount = packet.LidOpenCount,
                RSSI = args.RawSignalStrengthInDBm,
                BluetoothAddress = args.BluetoothAddress,
                Timestamp = DateTime.UtcNow,
                Status = packet.Status
            };
        }

        /// <summary>
        /// Validates if a manufacturer data section could be an AirPods device.
        /// </summary>
        public static bool IsValidAirPodsData(byte[] data)
        {
            if (data == null || data.Length < 10)
            {
                return false;
            }

            // Look for Proximity Pairing type (0x07)
            for (int i = 0; i < data.Length - 2; i++)
            {
                if (data[i] == ProximityPairingType)
                {
                    // Check if prefix follows
                    int dataStart = i + 2;
                    if (dataStart < data.Length && data[dataStart] == Prefix)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
