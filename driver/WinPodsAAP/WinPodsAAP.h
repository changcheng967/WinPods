/**
 * WinPodsAAP - KMDF L2CAP Bridge Driver for AirPods AAP Protocol
 *
 * This driver bridges L2CAP connections to userspace via DeviceIoControl,
 * enabling Windows applications to communicate with AirPods using the
 * Apple Accessory Protocol (AAP).
 *
 * Based on Microsoft's bthecho L2CAP sample driver pattern.
 * Reference: https://github.com/microsoft/Windows-driver-samples/tree/main/bluetooth/bthecho
 */

#ifndef _WINPODSAAP_H_
#define _WINPODSAAP_H_

#include <ntddk.h>
#include <wdf.h>
#include <bthddi.h>
#include <bthguid.h>
#include <bthsdpddi.h>
#include <bthdef.h>

//=============================================================================
// Driver Configuration
//=============================================================================

// Device interface GUID: {E3A4B7F8-1C2D-4A5B-9E6F-0D1A2B3C4D5E}
DEFINE_GUID(GUID_WINPODSAAP_INTERFACE,
    0xe3a4b7f8, 0x1c2d, 0x4a5b, 0x9e, 0x6f, 0x0d, 0x1a, 0x2b, 0x3c, 0x4d, 0x5e);

// IOCTL definitions - these MUST match the C# DriverBridge.cs
#define FILE_DEVICE_WINPODS     0x8000
#define IOCTL_WINPODS_CONNECT   CTL_CODE(FILE_DEVICE_WINPODS, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_WINPODS_DISCONNECT CTL_CODE(FILE_DEVICE_WINPODS, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_WINPODS_SEND      CTL_CODE(FILE_DEVICE_WINPODS, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_WINPODS_RECEIVE   CTL_CODE(FILE_DEVICE_WINPODS, 0x803, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_WINPODS_GET_STATUS CTL_CODE(FILE_DEVICE_WINPODS, 0x804, METHOD_BUFFERED, FILE_ANY_ACCESS)

// Connection states
typedef enum _WINPODS_CONNECTION_STATE {
    WinPodsDisconnected = 0,
    WinPodsConnecting = 1,
    WinPodsConnected = 2
} WINPODS_CONNECTION_STATE;

// IOCTL input/output structures - MUST match C# DriverBridge.cs
typedef struct _WINPODS_CONNECT_INPUT {
    ULONGLONG BluetoothAddress;
    USHORT PSM;
} WINPODS_CONNECT_INPUT, *PWINPODS_CONNECT_INPUT;

typedef struct _WINPODS_CONNECT_OUTPUT {
    ULONG Success;
    USHORT ChannelId;
} WINPODS_CONNECT_OUTPUT, *PWINPODS_CONNECT_OUTPUT;

typedef struct _WINPODS_TRANSFER_INPUT {
    ULONG BufferSize;
    ULONG TimeoutMs;
} WINPODS_TRANSFER_INPUT, *PWINPODS_TRANSFER_INPUT;

typedef struct _WINPODS_RECEIVE_OUTPUT {
    ULONG BytesReceived;
    ULONG ErrorCode;
} WINPODS_RECEIVE_OUTPUT, *PWINPODS_RECEIVE_OUTPUT;

typedef struct _WINPODS_STATUS_OUTPUT {
    ULONG ConnectionState;
    ULONGLONG ConnectedAddress;
} WINPODS_STATUS_OUTPUT, *PWINPODS_STATUS_OUTPUT;

//=============================================================================
// Device Context
//=============================================================================

typedef struct _DEVICE_CONTEXT {
    // Bluetooth profile driver interface (from bus driver)
    // Provides BthAllocateBrb, BthFreeBrb for BRB management
    BTH_PROFILE_DRIVER_INTERFACE BthInterface;

    // I/O target for BRB submission via IOCTL_INTERNAL_BTH_SUBMIT_BRB
    WDFIOTARGET IoTarget;

    // Flag indicating if we have a valid Bluetooth interface
    BOOLEAN HasBthInterface;

    // L2CAP connection state
    WINPODS_CONNECTION_STATE ConnectionState;
    BTH_ADDR RemoteAddress;
    USHORT PSM;
    L2CAP_CHANNEL_HANDLE ChannelHandle;

    // Receive buffer management
    PUCHAR ReceiveBuffer;
    SIZE_T ReceiveBufferSize;
    SIZE_T ReceiveDataLength;

    // Synchronization
    WDFSPINLOCK Lock;

    // Completion events
    KEVENT ConnectCompleteEvent;
    KEVENT TransferCompleteEvent;

} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, GetDeviceContext)

//=============================================================================
// Function Prototypes
//=============================================================================

// Driver entry
DRIVER_INITIALIZE DriverEntry;

// WDF callbacks
EVT_WDF_DRIVER_DEVICE_ADD WinPodsEvtDeviceAdd;
EVT_WDF_OBJECT_CONTEXT_CLEANUP WinPodsEvtDriverContextCleanup;
EVT_WDF_OBJECT_CONTEXT_CLEANUP WinPodsEvtDeviceContextCleanup;

// IOCTL handlers
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL WinPodsEvtIoDeviceControl;

// BRB helper functions
NTSTATUS
WinPodsAllocateBrb(
    _In_ BRB_TYPE BrbType,
    _Out_ PBRB* Brb
);

VOID
WinPodsFreeBrb(
    _In_ PBRB Brb
);

NTSTATUS
WinPodsSubmitBrbSynchronously(
    _In_ PDEVICE_CONTEXT Context,
    _Inout_ PBRB Brb
);

// L2CAP operation functions
NTSTATUS
WinPodsConnectL2CAP(
    _In_ PDEVICE_CONTEXT Context,
    _In_ BTH_ADDR RemoteAddress,
    _In_ USHORT PSM
);

NTSTATUS
WinPodsDisconnectL2CAP(
    _In_ PDEVICE_CONTEXT Context
);

NTSTATUS
WinPodsSendData(
    _In_ PDEVICE_CONTEXT Context,
    _In_ PUCHAR Buffer,
    _In_ SIZE_T BufferSize,
    _In_ ULONG TimeoutMs
);

NTSTATUS
WinPodsReceiveData(
    _In_ PDEVICE_CONTEXT Context,
    _Out_ PUCHAR Buffer,
    _In_ SIZE_T BufferSize,
    _Out_ PSIZE_T BytesReceived,
    _In_ ULONG TimeoutMs
);

// L2CAP indication callback (from Bluetooth stack)
// Signature matches INDICATION_CALLBACK from bthddi.h
VOID
WinPodsL2capIndicationCallback(
    _In_ L2CAP_CHANNEL_HANDLE ChannelHandle,
    _In_ ULONG Indication,
    _In_ PVOID Parameters,
    _In_ ULONG ParameterLength,
    _In_opt_ PVOID Context
);

#endif // _WINPODSAAP_H_
