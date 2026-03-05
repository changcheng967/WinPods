using System.Runtime.InteropServices;

namespace WinPods.Core.AAP;

/// <summary>
/// P/Invoke implementation to communicate with the WinPodsAAP kernel driver.
/// </summary>
public sealed class DriverBridge : IDriverBridge
{
    #region Constants

    // Device interface GUID: {E3A4B7F8-1C2D-4A5B-9E6F-0D1A2B3C4D5E}
    public static readonly Guid DeviceInterfaceGuid = new("E3A4B7F8-1C2D-4A5B-9E6F-0D1A2B3C4D5E");

    // IOCTL definitions (must match driver)
    private const uint FILE_DEVICE_WINPODS = 0x8000;
    private const uint FILE_ANY_ACCESS = 0;
    private const uint METHOD_BUFFERED = 0;

    private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access)
        => (deviceType << 16) | (access << 14) | (function << 2) | method;

    private static readonly uint IOCTL_WINPODS_CONNECT = CTL_CODE(FILE_DEVICE_WINPODS, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS);
    private static readonly uint IOCTL_WINPODS_DISCONNECT = CTL_CODE(FILE_DEVICE_WINPODS, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS);
    private static readonly uint IOCTL_WINPODS_SEND = CTL_CODE(FILE_DEVICE_WINPODS, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS);
    private static readonly uint IOCTL_WINPODS_RECEIVE = CTL_CODE(FILE_DEVICE_WINPODS, 0x803, METHOD_BUFFERED, FILE_ANY_ACCESS);
    private static readonly uint IOCTL_WINPODS_GET_STATUS = CTL_CODE(FILE_DEVICE_WINPODS, 0x804, METHOD_BUFFERED, FILE_ANY_ACCESS);

    // Win32 constants
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int INVALID_HANDLE_VALUE = -1;

    #endregion

    #region Native Methods

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Get_Device_Interface_List_Size(
        out uint pulLen,
        ref Guid InterfaceClassGuid,
        IntPtr pDeviceId,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List(
        ref Guid InterfaceClassGuid,
        IntPtr pDeviceId,
        [Out] char[] buffer,
        uint bufferLength,
        uint ulFlags);

    #endregion

    private IntPtr _deviceHandle = IntPtr.Zero;
    private bool _isDisposed;
    private readonly object _lock = new();

    /// <inheritdoc/>
    public DriverStatus Status
    {
        get
        {
            if (!IsDriverInstalled)
                return DriverStatus.NotInstalled;

            if (_deviceHandle == IntPtr.Zero)
                return DriverStatus.Installed;

            var state = GetConnectionState();
            return state == AAPConnectionState.Connected
                ? DriverStatus.Connected
                : DriverStatus.Installed;
        }
    }

    /// <inheritdoc/>
    public bool IsDriverInstalled => GetDeviceInterfacePath() != null;

    /// <inheritdoc/>
    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _deviceHandle != IntPtr.Zero && GetConnectionState() == AAPConnectionState.Connected;
            }
        }
    }

    /// <inheritdoc/>
    public ulong? ConnectedAddress
    {
        get
        {
            lock (_lock)
            {
                if (_deviceHandle == IntPtr.Zero)
                    return null;

                var status = QueryDriverStatus();
                return status.Success ? status.ConnectedAddress : null;
            }
        }
    }

    /// <summary>
    /// Gets the device interface path for the WinPodsAAP driver.
    /// </summary>
    private static string? GetDeviceInterfacePath()
    {
        try
        {
            var guid = DeviceInterfaceGuid;

            // Get required buffer size
            int result = CM_Get_Device_Interface_List_Size(out uint size, ref guid, IntPtr.Zero, 0);
            if (result != 0 || size == 0)
                return null;

            // Get the interface path
            var buffer = new char[size];
            result = CM_Get_Device_Interface_List(ref guid, IntPtr.Zero, buffer, size, 0);
            if (result != 0)
                return null;

            // Convert to string (null-terminated)
            var path = new string(buffer).TrimEnd('\0');
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool Open()
    {
        lock (_lock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DriverBridge));

            if (_deviceHandle != IntPtr.Zero)
                return true; // Already open

            var path = GetDeviceInterfacePath();
            if (path == null)
            {
                Console.WriteLine("[DriverBridge] Driver device interface not found");
                return false;
            }

            _deviceHandle = CreateFile(
                path,
                GENERIC_READ | GENERIC_WRITE,
                0,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (_deviceHandle.ToInt64() == INVALID_HANDLE_VALUE)
            {
                _deviceHandle = IntPtr.Zero;
                int error = Marshal.GetLastWin32Error();
                Console.WriteLine($"[DriverBridge] Failed to open device: Win32 error {error}");
                return false;
            }

            Console.WriteLine($"[DriverBridge] Device opened successfully: {path}");
            return true;
        }
    }

    /// <inheritdoc/>
    public bool Connect(ulong bluetoothAddress, ushort psm = 0x1001, int timeoutMs = 5000)
    {
        lock (_lock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DriverBridge));

            if (_deviceHandle == IntPtr.Zero && !Open())
            {
                Console.WriteLine("[DriverBridge] Cannot connect - device not open");
                return false;
            }

            // Prepare input buffer
            var input = new L2CAPConnectInput
            {
                BluetoothAddress = bluetoothAddress,
                PSM = psm
            };

            int inputSize = Marshal.SizeOf<L2CAPConnectInput>();
            int outputSize = Marshal.SizeOf<L2CAPConnectOutput>();

            IntPtr inputPtr = IntPtr.Zero;
            IntPtr outputPtr = IntPtr.Zero;

            try
            {
                inputPtr = Marshal.AllocHGlobal(inputSize);
                outputPtr = Marshal.AllocHGlobal(outputSize);

                Marshal.StructureToPtr(input, inputPtr, false);

                bool success = DeviceIoControl(
                    _deviceHandle,
                    IOCTL_WINPODS_CONNECT,
                    inputPtr,
                    (uint)inputSize,
                    outputPtr,
                    (uint)outputSize,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!success || bytesReturned < outputSize)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[DriverBridge] Connect IOCTL failed: Win32 error {error}");
                    return false;
                }

                var output = Marshal.PtrToStructure<L2CAPConnectOutput>(outputPtr);
                Console.WriteLine($"[DriverBridge] Connect result: Success={output.Success}, ChannelId={output.ChannelId}");

                return output.Success != 0;
            }
            finally
            {
                if (inputPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(inputPtr);
                if (outputPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(outputPtr);
            }
        }
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        lock (_lock)
        {
            if (_deviceHandle == IntPtr.Zero)
                return;

            bool success = DeviceIoControl(
                _deviceHandle,
                IOCTL_WINPODS_DISCONNECT,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);

            Console.WriteLine($"[DriverBridge] Disconnect: {(success ? "success" : "failed")}");
        }
    }

    /// <inheritdoc/>
    public bool Send(byte[] data, int timeoutMs = 5000)
    {
        lock (_lock)
        {
            if (_deviceHandle == IntPtr.Zero)
            {
                Console.WriteLine("[DriverBridge] Send failed: device not open");
                return false;
            }

            if (data == null || data.Length == 0)
                return false;

            // Create input with timeout and data
            var transferInput = new L2CAPTransferInput
            {
                BufferSize = data.Length,
                TimeoutMs = timeoutMs
            };

            int inputHeaderSize = Marshal.SizeOf<L2CAPTransferInput>();
            int totalInputSize = inputHeaderSize + data.Length;

            IntPtr inputPtr = IntPtr.Zero;

            try
            {
                inputPtr = Marshal.AllocHGlobal(totalInputSize);

                // Write header
                Marshal.StructureToPtr(transferInput, inputPtr, false);

                // Write data after header
                Marshal.Copy(data, 0, inputPtr + inputHeaderSize, data.Length);

                bool success = DeviceIoControl(
                    _deviceHandle,
                    IOCTL_WINPODS_SEND,
                    inputPtr,
                    (uint)totalInputSize,
                    IntPtr.Zero,
                    0,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[DriverBridge] Send IOCTL failed: Win32 error {error}");
                    return false;
                }

                Console.WriteLine($"[DriverBridge] Sent {data.Length} bytes");
                return true;
            }
            finally
            {
                if (inputPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(inputPtr);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SendAsync(byte[] data, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Send(data, timeoutMs), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public int Receive(byte[] buffer, int timeoutMs = 5000)
    {
        lock (_lock)
        {
            if (_deviceHandle == IntPtr.Zero)
            {
                Console.WriteLine("[DriverBridge] Receive failed: device not open");
                return -1;
            }

            if (buffer == null || buffer.Length == 0)
                return -1;

            var transferInput = new L2CAPTransferInput
            {
                BufferSize = buffer.Length,
                TimeoutMs = timeoutMs
            };

            int inputSize = Marshal.SizeOf<L2CAPTransferInput>();
            int outputHeaderSize = Marshal.SizeOf<L2CAPReceiveOutput>();
            int totalOutputSize = outputHeaderSize + buffer.Length;

            IntPtr inputPtr = IntPtr.Zero;
            IntPtr outputPtr = IntPtr.Zero;

            try
            {
                inputPtr = Marshal.AllocHGlobal(inputSize);
                outputPtr = Marshal.AllocHGlobal(totalOutputSize);

                Marshal.StructureToPtr(transferInput, inputPtr, false);

                bool success = DeviceIoControl(
                    _deviceHandle,
                    IOCTL_WINPODS_RECEIVE,
                    inputPtr,
                    (uint)inputSize,
                    outputPtr,
                    (uint)totalOutputSize,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!success || bytesReturned < outputHeaderSize)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[DriverBridge] Receive IOCTL failed: Win32 error {error}");
                    return -1;
                }

                var header = Marshal.PtrToStructure<L2CAPReceiveOutput>(outputPtr);

                if (header.ErrorCode != 0)
                {
                    Console.WriteLine($"[DriverBridge] Receive error: {header.ErrorCode}");
                    return -1;
                }

                int bytesToCopy = Math.Min(header.BytesReceived, buffer.Length);
                if (bytesToCopy > 0)
                {
                    Marshal.Copy(outputPtr + outputHeaderSize, buffer, 0, bytesToCopy);
                }

                Console.WriteLine($"[DriverBridge] Received {bytesToCopy} bytes");
                return bytesToCopy;
            }
            finally
            {
                if (inputPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(inputPtr);
                if (outputPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(outputPtr);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<(int bytesReceived, byte[] data)> ReceiveAsync(int maxBytes = 1024, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var buffer = new byte[maxBytes];
            int received = Receive(buffer, timeoutMs);
            if (received < 0)
                return (0, Array.Empty<byte>());

            var result = new byte[received];
            Buffer.BlockCopy(buffer, 0, result, 0, received);
            return (received, result);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public AAPConnectionState GetConnectionState()
    {
        var status = QueryDriverStatus();
        if (!status.Success)
            return AAPConnectionState.Error;

        return status.ConnectionState switch
        {
            0 => AAPConnectionState.Disconnected,
            1 => AAPConnectionState.Connecting,
            2 => AAPConnectionState.Connected,
            _ => AAPConnectionState.Error
        };
    }

    /// <summary>
    /// Queries the driver for current status.
    /// </summary>
    private (bool Success, int ConnectionState, ulong ConnectedAddress) QueryDriverStatus()
    {
        if (_deviceHandle == IntPtr.Zero)
            return (false, 0, 0);

        int outputSize = Marshal.SizeOf<DriverStatusOutput>();
        IntPtr outputPtr = IntPtr.Zero;

        try
        {
            outputPtr = Marshal.AllocHGlobal(outputSize);

            bool success = DeviceIoControl(
                _deviceHandle,
                IOCTL_WINPODS_GET_STATUS,
                IntPtr.Zero,
                0,
                outputPtr,
                (uint)outputSize,
                out uint bytesReturned,
                IntPtr.Zero);

            if (!success || bytesReturned < outputSize)
                return (false, 0, 0);

            var status = Marshal.PtrToStructure<DriverStatusOutput>(outputPtr);
            return (true, status.ConnectionState, status.ConnectedAddress);
        }
        finally
        {
            if (outputPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(outputPtr);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_deviceHandle != IntPtr.Zero)
            {
                Disconnect();
                CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~DriverBridge()
    {
        Dispose();
    }
}
