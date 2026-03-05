# WinPodsAAP Kernel Driver

## Overview

WinPodsAAP is a minimal KMDF Bluetooth L2CAP profile driver that bridges L2CAP connections to userspace via DeviceIoControl. This enables Windows applications to communicate with AirPods using the Apple Accessory Protocol (AAP).

## Why is this needed?

AirPods control commands use the Apple Accessory Protocol (AAP), which runs over classic Bluetooth L2CAP on PSM 0x1001 - NOT over BLE GATT. Windows blocks userspace applications from opening L2CAP sockets to paired audio devices. This driver provides a workaround by implementing the L2CAP connection in kernel mode and exposing it via IOCTLs.

## Features

- Connect to L2CAP channels on any PSM (default: 0x1001 for AAP)
- Send/receive raw data over the L2CAP channel
- Graceful disconnection and cleanup
- Thread-safe operations
- Compatible with Windows 10/11 x64 and ARM64

## Building

### Prerequisites

- Windows 11 SDK (10.0.26100.0 or later)
- Windows Driver Kit (WDK)
- Visual Studio 2022 with "Desktop development with C++" workload
- Windows Hardware Dev Kit (optional, for testing)

### Build Steps

1. Open Visual Studio as Administrator
2. Open `WinPodsAAP.vcxproj` from the `driver/WinPodsAAP/` folder
3. Select the desired configuration (Debug or Release) and platform (x64 or ARM64)
4. Build the project (Build > Build Solution)
5. The driver files will be in `driver/WinPodsAAP\x64\Debug\` or `driver/WinPodsAAP\x64\Release\`

### Output Files

- `WinPodsAAP.sys` - The driver binary
- `WinPodsAAP.inf` - Driver installation file
- `WinPodsAAP.cat` - Driver catalog file (created during build)

## Installation

### Method 1: Using PnPUtil (Recommended for testing)

1. Open Command Prompt as Administrator
2. Navigate to the driver folder:
   ```cmd
   cd path\to\driver\WinPodsAAP
   ```
3. Install the driver:
   ```cmd
   pnputil /add-driver WinPodsAAP.inf /install
   ```
4. The driver will be installed and start automatically when needed

### Method 2: Using Device Manager (For production)

1. Open Device Manager
2. Right-click on your computer name and select "Add legacy hardware"
3. Select "Install the hardware that I manually select from a list"
4. Select "Show All Devices"
5. Click "Have Disk"
6. Browse to the `WinPodsAAP.inf` file
7. Complete the installation wizard

### Method 3: Right-click INF (Simplest)

1. Right-click on `WinPodsAAP.inf`
2. Select "Install"
3. Accept the UAC prompt if it appears

## Driver Signing

### For Testing (Test Signing Mode)

The driver needs to be signed to load on Windows. For development/testing, enable test signing mode:

1. Open Command Prompt as Administrator
2. Enable test signing:
   ```cmd
   bcdedit /set testsigning on
   ```
3. Restart your computer
4. Install the driver using PnPUtil

### For Production (Attestation Signing)

For production distribution, the driver must be attestation-signed by Microsoft. This requires:

1. A valid EV (Extended Validation) code signing certificate
2. Submit the driver to the Windows Hardware Compatibility Program
3. Pass the Hardware Lab Kit (HLK) tests
4. Get Microsoft attestation signature

This process ensures the driver works on end-user machines without test mode.

## IOCTL Interface

The driver exposes the following IOCTLs:

| IOCTL | Code | Input | Output | Description |
|-------|------|-------|--------|-------------|
| `IOCTL_WINPODS_CONNECT` | 0x800 | `L2CAP_CONNECT_INPUT` | `L2CAP_CONNECT_OUTPUT` | Connect to L2CAP channel |
| `IOCTL_WINPODS_DISCONNECT` | 0x801 | - | - | Disconnect from channel |
| `IOCTL_WINPODS_SEND` | 0x802 | `L2CAP_TRANSFER_INPUT` + data | - | Send data |
| `IOCTL_WINPODS_RECEIVE` | 0x803 | `L2CAP_TRANSFER_INPUT` | `L2CAP_RECEIVE_OUTPUT` + data | Receive data |
| `IOCTL_WINPODS_GET_STATUS` | 0x804 | - | `DRIVER_STATUS_OUTPUT` | Get driver status |

### Structures

```c
typedef struct _L2CAP_CONNECT_INPUT {
    ULONGLONG BluetoothAddress;  // Device MAC address
    USHORT PSM;                 // Protocol/Service Multiplexer (0x1001 for AAP)
} L2CAP_CONNECT_INPUT;

typedef struct _L2CAP_CONNECT_OUTPUT {
    ULONG Success;      // Non-zero if connected
    USHORT ChannelId;   // L2CAP channel ID
} L2CAP_CONNECT_OUTPUT;

typedef struct _L2CAP_TRANSFER_INPUT {
    ULONG BufferSize;   // Size of data buffer
    ULONG TimeoutMs;    // Timeout in milliseconds
} L2CAP_TRANSFER_INPUT;

typedef struct _L2CAP_RECEIVE_OUTPUT {
    ULONG BytesReceived; // Number of bytes received
    ULONG ErrorCode;     // 0 = success
} L2CAP_RECEIVE_OUTPUT;

typedef struct _DRIVER_STATUS_OUTPUT {
    ULONG ConnectionState;    // 0=Disconnected, 1=Connecting, 2=Connected
    ULONGLONG ConnectedAddress; // Connected device address
} DRIVER_STATUS_OUTPUT;
```

## Usage from C#

See `DriverBridge.cs` in the WinPods.Core project for the complete implementation.

```csharp
// Open the driver
var bridge = new DriverBridge();
if (!bridge.Open())
{
    Console.WriteLine("Driver not installed");
    return;
}

// Connect to AirPods
ulong airPodsAddress = 0x001122334455; // Your AirPods MAC address
if (!bridge.Connect(airPodsAddress, 0x1001))
{
    Console.WriteLine("Failed to connect");
    return;
}

// Send noise control command
byte[] command = new byte[] { 0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0D, 0x02, 0x00, 0x00, 0x00 };
bridge.Send(command);

// Receive response
byte[] buffer = new byte[1024];
int received = bridge.Receive(buffer);
```

## Device Interface GUID

```
{E3A4B7F8-1C2D-4A5B-9E6F-0D1A2B3C4D5E}
```

This GUID is used to locate the driver device interface from userspace.

## Troubleshooting

### Driver not loading

1. Check if test signing is enabled (for development):
   ```cmd
   bcdedit /enum | findstr testsigning
   ```
2. Check the driver is installed:
   ```cmd
   pnputil /enum-drivers | findstr WinPodsAAP
   ```
3. Check Device Manager for error codes

### Connection failures

1. Ensure AirPods are paired in Windows Bluetooth settings
2. Verify the Bluetooth address is correct
3. Check that no other app is using the driver (exclusive access)

### Debug logging

Enable debug logging in the registry:

```cmd
reg add "HKLM\SYSTEM\CurrentControlSet\Services\WinPodsAAP\Parameters" /v LogLevel /t REG_DWORD /d 0xFF /f
```

View logs in WinDbg or DebugView.

## License

This driver is part of the WinPods project and is licensed under the MIT License.

## References

- [Microsoft Bluetooth L2CAP Echo Sample](https://github.com/microsoft/Windows-Driver-Samples/tree/main/bluetooth/l2cap/echo)
- [AAP Protocol Definition](https://github.com/tyalie/AAP-Protocol-Defintion)
- [LibrePods (Kotlin implementation)](https://github.com/kavishdevar/librepods)
