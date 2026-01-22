using System;
using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace WinPods.App.Services
{
    /// <summary>
    /// Noise control modes for AirPods.
    /// </summary>
    public enum NoiseControlMode
    {
        Off = 0,
        NoiseCancellation = 1,
        Transparency = 2
    }

    /// <summary>
    /// Service for controlling AirPods noise control via BLE GATT.
    /// </summary>
    public class NoiseControlService : IDisposable
    {
        // GATT Service UUID for AirPods (Apple custom service)
        private static readonly Guid AirPodsServiceUuid = Guid.Parse("0000FD02-0000-1000-8000-00805F9B34FB");

        // GATT Characteristic UUID for Ancillary Data (includes noise control)
        private static readonly Guid AncillaryDataCharacteristicUuid = Guid.Parse("0000FD03-0000-1000-8000-00805F9B34FB");

        // Alternative: Device Information Service
        private static readonly Guid DeviceInfoServiceUuid = Guid.Parse("0000180A-0000-1000-8000-00805F9B34FB");

        // Alternative: Apple Custom Service
        private static readonly Guid AppleCustomServiceUuid = Guid.Parse("06D1E5C7-2CB1-4E4E-A7B1-4B96F4F49F94");

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _controlCharacteristic;
        private NoiseControlMode _currentMode = NoiseControlMode.Off;

        /// <summary>
        /// Event raised when noise control mode changes.
        /// </summary>
        public event EventHandler<NoiseControlMode>? ModeChanged;

        /// <summary>
        /// Gets the current noise control mode.
        /// </summary>
        public NoiseControlMode CurrentMode => _currentMode;

        /// <summary>
        /// Gets whether the service is connected to AirPods.
        /// </summary>
        public bool IsConnected => _device != null && _controlCharacteristic != null;

        /// <summary>
        /// Connects to the AirPods and initializes GATT service.
        /// </summary>
        public async Task<bool> ConnectAsync(ulong bluetoothAddress)
        {
            try
            {
                Console.WriteLine($"[NoiseControl] ========== Connection Attempt Started ==========");
                Console.WriteLine($"[NoiseControl] Connecting to device {bluetoothAddress:X12}...");

                // Connect to the Bluetooth LE device
                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (_device == null)
                {
                    Console.WriteLine("[NoiseControl] ❌ Failed to connect to device");
                    return false;
                }

                Console.WriteLine($"[NoiseControl] ✓ Connected to {_device.Name}");
                Console.WriteLine($"[NoiseControl] Device ID: {_device.DeviceId}");
                Console.WriteLine($"[NoiseControl] Connection Status: {_device.ConnectionStatus}");

                // Get ALL GATT services for debugging
                var allServicesResult = await _device.GetGattServicesAsync();
                if (allServicesResult.Status != GattCommunicationStatus.Success)
                {
                    Console.WriteLine($"[NoiseControl] ❌ Failed to get GATT services: {allServicesResult.Status}");
                    Console.WriteLine($"[NoiseControl] Protocol Error: {allServicesResult.ProtocolError}");
                    return false;
                }

                var allServices = allServicesResult.Services;
                Console.WriteLine($"[NoiseControl] ========== Found {allServices.Count} GATT Services ==========");

                // List ALL services
                for (int i = 0; i < allServices.Count; i++)
                {
                    var service = allServices[i];
                    Console.WriteLine($"[NoiseControl] Service {i}: {service.Uuid}");

                    // Get all characteristics for this service
                    var charResult = await service.GetCharacteristicsAsync();
                    if (charResult.Status == GattCommunicationStatus.Success)
                    {
                        Console.WriteLine($"[NoiseControl]   Has {charResult.Characteristics.Count} characteristics:");

                        foreach (var characteristic in charResult.Characteristics)
                        {
                            var props = characteristic.CharacteristicProperties;
                            Console.WriteLine($"[NoiseControl]     - {characteristic.Uuid}");
                            Console.WriteLine($"[NoiseControl]       Properties: {props}");
                            Console.WriteLine($"[NoiseControl]       Read: {(props & GattCharacteristicProperties.Read) != 0}");
                            Console.WriteLine($"[NoiseControl]       Write: {(props & GattCharacteristicProperties.Write) != 0}");
                            Console.WriteLine($"[NoiseControl]       WriteNoResponse: {(props & GattCharacteristicProperties.WriteWithoutResponse) != 0}");
                            Console.WriteLine($"[NoiseControl]       Notify: {(props & GattCharacteristicProperties.Notify) != 0}");
                            Console.WriteLine($"[NoiseControl]       Indicate: {(props & GattCharacteristicProperties.Indicate) != 0}");

                            // If writable, save it
                            if ((props & GattCharacteristicProperties.Write) != 0 ||
                                (props & GattCharacteristicProperties.WriteWithoutResponse) != 0)
                            {
                                if (_controlCharacteristic == null)
                                {
                                    _controlCharacteristic = characteristic;
                                    Console.WriteLine($"[NoiseControl] ✓✓✓ SAVED as control characteristic!");
                                }
                            }
                        }
                    }
                }

                if (_controlCharacteristic == null)
                {
                    Console.WriteLine("[NoiseControl] ❌❌❌ NO WRITABLE CHARACTERISTIC FOUND!");
                    Console.WriteLine("[NoiseControl] This means AirPods don't expose control GATT characteristics on Windows");
                    Console.WriteLine("[NoiseControl] Noise control requires low-level Bluetooth stack access (not available on Windows)");
                    return false;
                }

                Console.WriteLine($"[NoiseControl] ✓✓✓ SUCCESS: Using characteristic {_controlCharacteristic.Uuid}");
                Console.WriteLine($"[NoiseControl] ================================================");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NoiseControl] ❌ Connection failed: {ex.Message}");
                Console.WriteLine($"[NoiseControl] Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Sets the noise control mode using AACP (Apple Audio Control Protocol).
        /// </summary>
        public async Task<bool> SetModeAsync(NoiseControlMode mode)
        {
            try
            {
                Console.WriteLine($"[NoiseControl] ========== Setting Mode ==========");
                Console.WriteLine($"[NoiseControl] Requested mode: {mode}");

                if (_controlCharacteristic == null)
                {
                    Console.WriteLine("[NoiseControl] ❌ Control characteristic not available!");
                    Console.WriteLine("[NoiseControl] Call ConnectAsync() first to find a writable characteristic");
                    return false;
                }

                if (_device == null)
                {
                    Console.WriteLine("[NoiseControl] ❌ Device not connected");
                    return false;
                }

                // AACP Packet format (from librepods reverse engineering):
                // 04 00 04 00 [opcode, little endian] [identifier] [data1] [data2] [data3] [data4]
                // Opcode 0x09 = Control Command
                // Identifier 0x0D = ListeningMode
                // Mode values: 0x01 = Off, 0x02 = ANC, 0x03 = Transparency, 0x04 = Adaptive

                byte aacpMode = mode switch
                {
                    NoiseControlMode.Off => 0x01,
                    NoiseControlMode.NoiseCancellation => 0x02,
                    NoiseControlMode.Transparency => 0x03,
                    _ => 0x01
                };

                byte[] command = new byte[11];
                command[0] = 0x04;  // AACP Header
                command[1] = 0x00;
                command[2] = 0x04;
                command[3] = 0x00;
                command[4] = 0x09;  // Opcode: Control Command (little endian)
                command[5] = 0x00;
                command[6] = 0x0D;  // Identifier: ListeningMode
                command[7] = aacpMode;  // Mode value
                command[8] = 0x00;  // Unused
                command[9] = 0x00;  // Unused
                command[10] = 0x00; // Unused

                string hexPacket = BitConverter.ToString(command);
                Console.WriteLine($"[NoiseControl] AACP Packet: {hexPacket}");
                Console.WriteLine($"[NoiseControl] Target Characteristic: {_controlCharacteristic.Uuid}");
                Console.WriteLine($"[NoiseControl] Characteristic Properties: {_controlCharacteristic.CharacteristicProperties}");
                Console.WriteLine($"[NoiseControl] Device Connection Status: {_device.ConnectionStatus}");

                var writer = new DataWriter();
                writer.WriteBytes(command);
                var buffer = writer.DetachBuffer();

                Console.WriteLine($"[NoiseControl] Buffer length: {buffer.Length} bytes");

                // Try write without response first (faster)
                Console.WriteLine("[NoiseControl] Attempting WriteValueAsync...");
                var writeResult = await _controlCharacteristic.WriteValueAsync(buffer);

                Console.WriteLine($"[NoiseControl] Write result: {writeResult}");

                if (writeResult == GattCommunicationStatus.Success)
                {
                    _currentMode = mode;
                    ModeChanged?.Invoke(this, mode);
                    Console.WriteLine($"[NoiseControl] ✓✓✓ SUCCESS! Mode set to {mode}");
                    Console.WriteLine($"[NoiseControl] ========================================");
                    return true;
                }
                else if (writeResult == GattCommunicationStatus.Unreachable)
                {
                    Console.WriteLine("[NoiseControl] ❌ Device unreachable - may need to reconnect");
                }
                else if (writeResult == GattCommunicationStatus.ProtocolError)
                {
                    Console.WriteLine("[NoiseControl] ❌ Protocol error - packet format may be wrong");
                }
                else if (writeResult == GattCommunicationStatus.AccessDenied)
                {
                    Console.WriteLine("[NoiseControl] ❌ Access denied - characteristic may not be writable");
                }

                Console.WriteLine($"[NoiseControl] ❌ FAILED to set mode to {mode}");
                Console.WriteLine($"[NoiseControl] ========================================");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NoiseControl] ❌ SetMode failed: {ex.Message}");
                Console.WriteLine($"[NoiseControl] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[NoiseControl] Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the AirPods.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _controlCharacteristic = null;
                _device?.Dispose();
                _device = null;
                Debug.WriteLine("[NoiseControl] Disconnected");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoiseControl] Disconnect error: {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes the noise control service.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }
    }
}
