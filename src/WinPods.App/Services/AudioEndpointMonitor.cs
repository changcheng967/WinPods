using System.Runtime.InteropServices;

namespace WinPods.App.Services
{
    /// <summary>
    /// Monitors audio endpoint changes to detect when AirPods are removed/inserted.
    /// Uses polling to check for default audio device changes every 1 second.
    /// When AirPods are removed, Windows auto-switches audio to another device.
    /// When AirPods are inserted, Windows switches back to AirPods.
    /// </summary>
    public class AudioEndpointMonitor : IDisposable
    {
        private System.Threading.Timer? _pollTimer;
        private bool _isDisposed;
        private string? _currentAudioDeviceId;
        private bool _isAirPodsCurrentDevice;

        // Events
        public event EventHandler<AudioEndpointEventArgs>? AudioSwitchedFromAirPods;
        public event EventHandler<AudioEndpointEventArgs>? AudioSwitchedToAirPods;

        /// <summary>
        /// Gets the current default audio device name.
        /// </summary>
        public string? CurrentDeviceName { get; private set; }

        /// <summary>
        /// Gets whether AirPods are the current audio device.
        /// </summary>
        public bool IsAirPodsCurrentDevice => _isAirPodsCurrentDevice;

        /// <summary>
        /// Initializes the audio endpoint monitor.
        /// </summary>
        public Task InitializeAsync()
        {
            try
            {
                Console.WriteLine("[AudioEndpointMonitor] Initializing...");

                // Get the current default audio device
                var currentDevice = GetDefaultAudioDevice();
                if (currentDevice != null)
                {
                    _currentAudioDeviceId = currentDevice.Id;
                    CurrentDeviceName = currentDevice.Name;
                    Console.WriteLine($"[AudioEndpointMonitor] ✓ Current device: {CurrentDeviceName}");
                    Console.WriteLine($"[AudioEndpointMonitor]   Device ID: {currentDevice.Id}");

                    // Check if it's AirPods
                    if (IsAirPodsDevice(CurrentDeviceName))
                    {
                        _isAirPodsCurrentDevice = true;
                        Console.WriteLine("[AudioEndpointMonitor] ✓✓✓ Detected AirPods as current audio device");
                    }
                    else
                    {
                        Console.WriteLine("[AudioEndpointMonitor] ℹ Current device is NOT AirPods");
                    }
                }
                else
                {
                    Console.WriteLine("[AudioEndpointMonitor] ❌ Failed to get default audio device!");
                }

                // Start polling timer (check every 1 second)
                _pollTimer = new System.Threading.Timer(
                    OnPollTimer,
                    null,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1));

                Console.WriteLine("[AudioEndpointMonitor] ✓ Started polling for audio device changes (every 1 second)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioEndpointMonitor] ❌ Initialization error: {ex.Message}");
                Console.WriteLine($"[AudioEndpointMonitor] Stack: {ex.StackTrace}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Polling timer callback - checks for audio device changes.
        /// </summary>
        private void OnPollTimer(object? state)
        {
            try
            {
                if (_isDisposed)
                    return;

                var currentDevice = GetDefaultAudioDevice();
                if (currentDevice == null || string.IsNullOrEmpty(currentDevice.Id))
                {
                    // Only log this once per minute to avoid spam
                    return;
                }

                // Check if device changed
                if (currentDevice.Id != _currentAudioDeviceId)
                {
                    bool wasAirPods = _isAirPodsCurrentDevice;
                    bool isAirPods = IsAirPodsDevice(currentDevice.Name);
                    string? previousDeviceName = CurrentDeviceName;

                    Console.WriteLine("");
                    Console.WriteLine("========== AUDIO ENDPOINT CHANGE DETECTED ==========");
                    Console.WriteLine($"[AudioEndpointMonitor] Device changed: {previousDeviceName} -> {currentDevice.Name}");
                    Console.WriteLine($"[AudioEndpointMonitor] Was AirPods: {wasAirPods}, Is AirPods: {isAirPods}");
                    Console.WriteLine($"[AudioEndpointMonitor] Previous ID: {_currentAudioDeviceId}");
                    Console.WriteLine($"[AudioEndpointMonitor] New ID: {currentDevice.Id}");
                    Console.WriteLine("=====================================================");
                    Console.WriteLine("");

                    // Update state
                    _currentAudioDeviceId = currentDevice.Id;
                    CurrentDeviceName = currentDevice.Name;
                    _isAirPodsCurrentDevice = isAirPods;

                    // Trigger events
                    if (wasAirPods && !isAirPods)
                    {
                        // Switched FROM AirPods TO another device
                        Console.WriteLine($"[AudioEndpointMonitor] 🎧 AirPods REMOVED! Audio switched to {currentDevice.Name}");
                        AudioSwitchedFromAirPods?.Invoke(this, new AudioEndpointEventArgs(
                            previousDeviceName ?? "AirPods",
                            currentDevice.Name,
                            wasAirPods,
                            isAirPods));
                    }
                    else if (!wasAirPods && isAirPods)
                    {
                        // Switched TO AirPods FROM another device
                        Console.WriteLine($"[AudioEndpointMonitor] 🎧 AirPods INSERTED! Audio switched from {previousDeviceName}");
                        AudioSwitchedToAirPods?.Invoke(this, new AudioEndpointEventArgs(
                            previousDeviceName ?? "Unknown",
                            currentDevice.Name,
                            wasAirPods,
                            isAirPods));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioEndpointMonitor] ❌ Error in polling timer: {ex.Message}");
                Console.WriteLine($"[AudioEndpointMonitor] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Checks if a device name indicates it's AirPods.
        /// </summary>
        private bool IsAirPodsDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return false;

            // Check for AirPods-related keywords
            return deviceName.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                   deviceName.Contains("Air Pods", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the current default audio device using NAudio-style P/Invoke.
        /// </summary>
        private AudioDeviceInfo? GetDefaultAudioDevice()
        {
            nint enumeratorPtr = nint.Zero;
            nint devicePtr = nint.Zero;
            nint propsPtr = nint.Zero;

            try
            {
                // Copy static readonly Guids to local variables
                Guid clsid = CLSID_MMDeviceEnumerator;
                Guid iid = IID_IMMDeviceEnumerator;

                int hr = CoCreateInstance(
                    ref clsid,
                    nint.Zero,
                    CLSCTX.ALL,
                    ref iid,
                    out enumeratorPtr);

                if (hr != 0 || enumeratorPtr == 0)
                {
                    Console.WriteLine($"[AudioEndpointMonitor] ❌ CoCreateInstance failed with HRESULT: 0x{hr:X8}");
                    return null;
                }

                var enumerator = Marshal.GetObjectForIUnknown(enumeratorPtr) as IMMDeviceEnumerator;
                if (enumerator == null)
                {
                    Console.WriteLine("[AudioEndpointMonitor] ❌ Failed to get IMMDeviceEnumerator");
                    return null;
                }

                // Try eConsole role first, then fall back to eMultimedia
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out devicePtr);

                // If eConsole fails, try eMultimedia
                if (hr != 0 || devicePtr == 0)
                {
                    Console.WriteLine($"[AudioEndpointMonitor] ⚠ eConsole role failed (0x{hr:X8}), trying eMultimedia...");
                    hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out devicePtr);
                }

                // If eMultimedia also fails, try eCommunications
                if (hr != 0 || devicePtr == 0)
                {
                    Console.WriteLine($"[AudioEndpointMonitor] ⚠ eMultimedia role failed (0x{hr:X8}), trying eCommunications...");
                    hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eCommunications, out devicePtr);
                }

                if (hr != 0 || devicePtr == 0)
                {
                    Console.WriteLine($"[AudioEndpointMonitor] ❌ GetDefaultAudioEndpoint failed with HRESULT: 0x{hr:X8}");
                    return null;
                }

                var device = Marshal.GetObjectForIUnknown(devicePtr) as IMMDevice;
                if (device == null)
                {
                    Console.WriteLine("[AudioEndpointMonitor] ❌ Failed to get IMMDevice");
                    return null;
                }

                hr = device.GetId(out string? id);
                if (hr != 0 || string.IsNullOrEmpty(id))
                {
                    Console.WriteLine($"[AudioEndpointMonitor] ❌ device.GetId failed with HRESULT: 0x{hr:X8}");
                    return null;
                }

                // Try to get friendly name
                string name = "Unknown Device";
                hr = device.OpenPropertyStore(0, out propsPtr);
                if (hr == 0 && propsPtr != 0)
                {
                    var props = Marshal.GetObjectForIUnknown(propsPtr) as IPropertyStore;
                    if (props != null)
                    {
                        PROPVARIANT nameVar;
                        PropertyKey pkey = PKEY_Device_FriendlyName;
                        hr = props.GetValue(ref pkey, out nameVar);
                        if (hr == 0 && nameVar.pwszVal != nint.Zero)
                        {
                            name = Marshal.PtrToStringAuto(nameVar.pwszVal) ?? "Unknown Device";
                        }
                        PropVariantClear(ref nameVar);
                    }
                }

                return new AudioDeviceInfo { Id = id, Name = name };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioEndpointMonitor] ❌ Exception getting default device: {ex.Message}");
                Console.WriteLine($"[AudioEndpointMonitor] Exception type: {ex.GetType().Name}");
                return null;
            }
            finally
            {
                // Cleanup COM pointers
                if (propsPtr != 0)
                    Marshal.Release(propsPtr);
                if (devicePtr != 0)
                    Marshal.Release(devicePtr);
                if (enumeratorPtr != 0)
                    Marshal.Release(enumeratorPtr);
            }
        }

        /// <summary>
        /// Disposes the monitor.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _pollTimer?.Dispose();
            _isDisposed = true;
        }

        #region COM Interop

        private static readonly Guid CLSID_MMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");
        private static readonly Guid IID_IMMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
        private static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            ref Guid rclsid,
            nint pUnkOuter,
            CLSCTX dwClsContext,
            ref Guid riid,
            out nint ppv);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        [Flags]
        private enum CLSCTX : uint
        {
            INPROC_SERVER = 0x1,
            INPROC_HANDLER = 0x2,
            LOCAL_SERVER = 0x4,
            REMOTE_SERVER = 0x10,
            ALL = 0x13
        }

        private enum EDataFlow
        {
            eRender = 0,
            eCapture = 1,
            eAll = 2
        }

        private enum ERole
        {
            eConsole = 0,
            eMultimedia = 1,
            eCommunications = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public union_podAnonymous Anonymous;
            public nint pwszVal;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct union_podAnonymous
        {
            [FieldOffset(0)] public sbyte iVal;
            [FieldOffset(0)] public uint uintVal;
        }

        #region COM Interfaces

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out nint ppDevice);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

            [PreserveSig]
            int OpenPropertyStore(int stgmAccess, out nint ppProperties);
        }

        [ComImport, Guid("9CDBCA4B-3663-4FAF-BCBF-40B5523B2C3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint cProps);

            [PreserveSig]
            int GetAt(uint iProp, out PropertyKey pkey);

            [PreserveSig]
            int GetValue(ref PropertyKey key, out PROPVARIANT pv);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// Event args for audio endpoint changes.
    /// </summary>
    public class AudioEndpointEventArgs : EventArgs
    {
        public string FromDevice { get; }
        public string ToDevice { get; }
        public bool WasAirPods { get; }
        public bool IsAirPods { get; }

        public AudioEndpointEventArgs(string fromDevice, string toDevice, bool wasAirPods, bool isAirPods)
        {
            FromDevice = fromDevice;
            ToDevice = toDevice;
            WasAirPods = wasAirPods;
            IsAirPods = isAirPods;
        }
    }

    /// <summary>
    /// Audio device information.
    /// </summary>
    public class AudioDeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
