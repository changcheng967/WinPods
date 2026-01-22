using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Storage.Streams;

namespace WinPods.App.Services
{
    /// <summary>
    /// Attempts to trigger Windows to connect AirPods audio when case opens.
    /// Uses multiple methods to nudge the Bluetooth stack.
    /// </summary>
    public class ConnectionTriggerService
    {
        /// <summary>
        /// Tries to trigger Windows Bluetooth audio connection.
        /// Uses multiple approaches to nudge the stack.
        /// </summary>
        public async Task<bool> TryTriggerConnectionAsync(ulong bluetoothAddress)
        {
            try
            {
                Console.WriteLine($"[ConnectionTrigger] ========== Triggering Connection for {bluetoothAddress:X12} ==========");

                BluetoothLEDevice? device = null;

                // Attempt 1: Get device by address (this sometimes triggers Windows to connect)
                Console.WriteLine("[ConnectionTrigger] Attempt 1: Getting BluetoothLEDevice...");
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (device == null)
                {
                    Console.WriteLine("[ConnectionTrigger] ❌ Could not get device");
                    return false;
                }
                Console.WriteLine($"[ConnectionTrigger] ✓ Got device: {device.Name}");

                // Small delay to let Windows process
                await Task.Delay(100);

                // Attempt 2: Request access to device
                Console.WriteLine("[ConnectionTrigger] Attempt 2: Requesting access...");
                try
                {
                    var accessResult = await device.RequestAccessAsync();
                    Console.WriteLine($"[ConnectionTrigger] Access request result: {accessResult}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConnectionTrigger] Access request failed (this is OK): {ex.Message}");
                }

                await Task.Delay(100);

                // Attempt 3: Get GATT services (pokes the Bluetooth stack)
                Console.WriteLine("[ConnectionTrigger] Attempt 3: Getting GATT services...");
                try
                {
                    var gattResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    Console.WriteLine($"[ConnectionTrigger] GATT services result: {gattResult.Status}, Services: {gattResult.Services?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConnectionTrigger] GATT services failed (expected if audio connected): {ex.Message}");
                }

                await Task.Delay(100);

                // Attempt 4: Find device via selector and access properties
                Console.WriteLine("[ConnectionTrigger] Attempt 5: Finding via device selector...");
                try
                {
                    var selector = BluetoothDevice.GetDeviceSelectorFromBluetoothAddress(bluetoothAddress);
                    var devices = await DeviceInformation.FindAllAsync(selector);
                    Console.WriteLine($"[ConnectionTrigger] Found {devices.Count} devices via selector");

                    foreach (var devInfo in devices)
                    {
                        Console.WriteLine($"[ConnectionTrigger] Checking device: {devInfo.Name}");

                        // Access the device object (sometimes triggers connection)
                        var btDevice = await BluetoothDevice.FromIdAsync(devInfo.Id);
                        if (btDevice != null)
                        {
                            // Access various properties to trigger activity
                            var name = btDevice.Name;
                            var status = btDevice.ConnectionStatus;
                            Console.WriteLine($"[ConnectionTrigger] Accessed device: {name}, Status: {status}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConnectionTrigger] Device selector failed: {ex.Message}");
                }

                // Attempt 6: Get device information
                Console.WriteLine("[ConnectionTrigger] Attempt 6: Getting device information...");
                try
                {
                    var deviceInfo = await DeviceInformation.CreateFromIdAsync(device.DeviceId);
                    Console.WriteLine($"[ConnectionTrigger] Device info: {deviceInfo.Name}, IsPaired: {deviceInfo.Pairing.IsPaired}, CanConnect: {deviceInfo.Pairing.CanPair}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConnectionTrigger] Device info failed: {ex.Message}");
                }

                Console.WriteLine("[ConnectionTrigger] ✓ All trigger attempts completed");
                return true; // Attempts made, audio connection will be checked separately
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConnectionTrigger] ❌ Connection trigger failed: {ex.Message}");
                Console.WriteLine($"[ConnectionTrigger] Exception type: {ex.GetType().Name}");
                return false;
            }
        }
    }
}
