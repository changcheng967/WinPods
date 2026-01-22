using Windows.Media.Devices;
using Windows.Devices.Enumeration;

namespace WinPods.App.Services
{
    /// <summary>
    /// Monitors audio endpoint to detect when AirPods become the default audio device.
    /// </summary>
    public class AudioConnectionMonitor
    {
        /// <summary>
        /// Waits for AirPods to become the default audio device.
        /// </summary>
        /// <param name="timeoutSeconds">How long to wait before giving up</param>
        /// <returns>True if AirPods connected within timeout</returns>
        public async Task<bool> WaitForAudioConnectionAsync(int timeoutSeconds = 8)
        {
            Console.WriteLine($"[AudioMonitor] ========== Waiting for Audio Connection (timeout: {timeoutSeconds}s) ==========");

            var endTime = DateTime.Now.AddSeconds(timeoutSeconds);
            var checkCount = 0;
            bool loggedAvailableDevices = false;

            while (DateTime.Now < endTime)
            {
                checkCount++;
                bool isConnected = IsAirPodsDefaultAudioDevice();

                Console.WriteLine($"[AudioMonitor] Check #{checkCount}: AirPods is default audio device = {isConnected}");

                // Log available audio devices on first check to help debug
                if (!loggedAvailableDevices)
                {
                    LogAvailableAudioDevices();
                    loggedAvailableDevices = true;
                }

                if (isConnected)
                {
                    Console.WriteLine("[AudioMonitor] ✓✓✓ SUCCESS - AirPods are now the default audio device!");
                    return true;
                }

                // Wait before next check
                await Task.Delay(500);
            }

            Console.WriteLine("[AudioMonitor] ⏱️ Timeout - AirPods did not connect within the time limit");
            return false;
        }

        /// <summary>
        /// Logs all available audio render devices for debugging.
        /// </summary>
        private void LogAvailableAudioDevices()
        {
            try
            {
                var currentDefaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
                Console.WriteLine($"[AudioMonitor] Current default audio device ID: {currentDefaultId}");

                var devices = DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector()).AsTask().Result;
                Console.WriteLine($"[AudioMonitor] Found {devices.Count} audio render devices:");

                foreach (var device in devices)
                {
                    string isDefault = device.Id == currentDefaultId ? " [DEFAULT]" : "";
                    Console.WriteLine($"[AudioMonitor]   - {device.Name}{isDefault}");
                    Console.WriteLine($"[AudioMonitor]     ID: {device.Id}");
                    Console.WriteLine($"[AudioMonitor]     IsEnabled: {device.IsEnabled}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioMonitor] Error logging audio devices: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if AirPods are currently the default audio endpoint.
        /// </summary>
        public bool IsAirPodsDefaultAudioDevice()
        {
            try
            {
                // Get current default audio render device
                var currentDefaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);

                if (string.IsNullOrEmpty(currentDefaultId))
                {
                    return false;
                }

                // Get all audio render devices
                var devices = DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector()).AsTask().Result;

                foreach (var device in devices)
                {
                    // Check if device name contains "AirPods" and is the default
                    if (!string.IsNullOrEmpty(device.Name) &&
                        device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) &&
                        device.Id == currentDefaultId)
                    {
                        Console.WriteLine($"[AudioMonitor] Current default device: {device.Name}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioMonitor] Error checking audio device: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current default audio device name.
        /// </summary>
        public string? GetCurrentDefaultAudioDeviceName()
        {
            try
            {
                var currentDefaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);

                if (string.IsNullOrEmpty(currentDefaultId))
                {
                    return null;
                }

                var devices = DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector()).AsTask().Result;

                foreach (var device in devices)
                {
                    if (device.Id == currentDefaultId)
                    {
                        return device.Name;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioMonitor] Error getting default device name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks if AirPods audio device is available (paired and visible to Windows).
        /// </summary>
        public bool IsAirPodsAudioDeviceAvailable()
        {
            try
            {
                var devices = DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector()).AsTask().Result;

                foreach (var device in devices)
                {
                    if (!string.IsNullOrEmpty(device.Name) &&
                        device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[AudioMonitor] AirPods audio device found: {device.Name}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioMonitor] Error checking AirPods availability: {ex.Message}");
                return false;
            }
        }
    }
}
