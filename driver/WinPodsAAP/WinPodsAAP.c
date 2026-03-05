/**
 * WinPodsAAP - KMDF L2CAP Bridge Driver for AirPods AAP Protocol
 *
 * This driver bridges L2CAP connections to userspace via DeviceIoControl,
 * enabling Windows applications to communicate with AirPods using the
 * Apple Accessory Protocol (AAP).
 */

#include "WinPodsAAP.h"

// Global Bluetooth driver interfaces
static BTH_INTERFACE_QUALIFIER    g_BthInterface = NULL;

//=============================================================================
// Driver Entry Point
//=============================================================================

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    NTSTATUS status;
    WDF_DRIVER_CONFIG config;

    KdPrint(("WinPodsAAP: DriverEntry\n"));

    // Initialize WDF driver config
    WDF_DRIVER_CONFIG_INIT(&config, WinPodsEvtDeviceAdd);
    config.EvtDriverContextCleanup = WinPodsEvtDriverContextCleanup;

    // Create the WDF driver object
    status = WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfDriverCreate failed: 0x%08X\n", status));
        return status;
    }

    KdPrint(("WinPodsAAP: Driver initialized successfully\n"));
    return STATUS_SUCCESS;
}

//=============================================================================
// WDF Callbacks
//=============================================================================

NTSTATUS
WinPodsEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
)
{
    NTSTATUS status;
    WDF_OBJECT_ATTRIBUTES deviceAttrs;
    WDFDEVICE device;
    PDEVICE_CONTEXT devCtx;
    WDF_IO_QUEUE_CONFIG queueConfig;
    DECLARE_CONST_UNICODE_STRING(deviceName, L"\\Device\\WinPodsAAP");
    DECLARE_CONST_UNICODE_STRING(symbolicLink, L"\\DosDevices\\WinPodsAAP");

    UNREFERENCED_PARAMETER(Driver);

    KdPrint(("WinPodsAAP: WinPodsEvtDeviceAdd\n"));

    // Set device as exclusive (only one app at a time)
    WdfDeviceInitSetExclusive(DeviceInit, TRUE);

    // Set device characteristics
    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_BLUETOOTH);
    WdfDeviceInitSetCharacteristics(DeviceInit, FILE_DEVICE_SECURE_OPEN, FALSE);

    // Initialize device attributes
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttrs, DEVICE_CONTEXT);
    deviceAttrs.EvtCleanupCallback = WinPodsEvtDeviceContextCleanup;

    // Create the device
    status = WdfDeviceCreate(&DeviceInit, &deviceAttrs, &device);
    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfDeviceCreate failed: 0x%08X\n", status));
        return status;
    }

    // Get device context
    devCtx = GetDeviceContext(device);
    RtlZeroMemory(devCtx, sizeof(DEVICE_CONTEXT));
    devCtx->ConnectionState = WinPodsDisconnected;
    KeInitializeEvent(&devCtx->ConnectComplete, NotificationEvent, FALSE);
    KeInitializeEvent(&devCtx->ReceiveComplete, NotificationEvent, FALSE);

    // Create spinlock
    status = WdfSpinLockCreate(WDF_NO_OBJECT_ATTRIBUTES, &devCtx->Lock);
    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfSpinLockCreate failed: 0x%08X\n", status));
        return status;
    }

    // Create default IO queue for IOCTL handling
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = WinPodsEvtIoDeviceControl;

    status = WdfIoQueueCreate(
        device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        WDF_NO_HANDLE
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfIoQueueCreate failed: 0x%08X\n", status));
        return status;
    }

    // Register device interface
    status = WdfDeviceCreateDeviceInterface(
        device,
        &GUID_WINPODSAAP_INTERFACE,
        NULL
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfDeviceCreateDeviceInterface failed: 0x%08X\n", status));
        return status;
    }

    KdPrint(("WinPodsAAP: Device created successfully\n"));
    return STATUS_SUCCESS;
}

VOID
WinPodsEvtDriverContextCleanup(
    _In_ WDFOBJECT DriverObject
)
{
    UNREFERENCED_PARAMETER(DriverObject);
    KdPrint(("WinPodsAAP: Driver cleanup\n"));
}

VOID
WinPodsEvtDeviceContextCleanup(
    _In_ WDFOBJECT DeviceObject
)
{
    PDEVICE_CONTEXT devCtx = GetDeviceContext(DeviceObject);

    KdPrint(("WinPodsAAP: Device cleanup\n"));

    // Disconnect if connected
    if (devCtx->ConnectionState != WinPodsDisconnected) {
        WinPodsDisconnectL2CAP(devCtx);
    }

    // Free receive buffer
    if (devCtx->ReceiveBuffer) {
        ExFreePoolWithTag(devCtx->ReceiveBuffer, 'PDAW');
        devCtx->ReceiveBuffer = NULL;
    }

    // Free BRB
    if (devCtx->CurrentBrb) {
        ExFreePoolWithTag(devCtx->CurrentBrb, 'PDAW');
        devCtx->CurrentBrb = NULL;
    }
}

//=============================================================================
// IOCTL Handler
//=============================================================================

VOID
WinPodsEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    WDFDEVICE device;
    PDEVICE_CONTEXT devCtx;
    PVOID inBuffer, outBuffer;
    size_t inBufferSize, outBufferSize;
    ULONG bytesReturned = 0;

    device = WdfIoQueueGetDevice(Queue);
    devCtx = GetDeviceContext(device);

    KdPrint(("WinPodsAAP: IOCTL 0x%08X\n", IoControlCode));

    switch (IoControlCode) {

    case IOCTL_WINPODS_CONNECT:
    {
        PWINPODS_CONNECT_INPUT input;
        PWINPODS_CONNECT_OUTPUT output;

        if (InputBufferLength < sizeof(WINPODS_CONNECT_INPUT) ||
            OutputBufferLength < sizeof(WINPODS_CONNECT_OUTPUT)) {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        status = WdfRequestRetrieveInputBuffer(Request, sizeof(WINPODS_CONNECT_INPUT), &inBuffer, &inBufferSize);
        if (!NT_SUCCESS(status)) break;

        status = WdfRequestRetrieveOutputBuffer(Request, sizeof(WINPODS_CONNECT_OUTPUT), &outBuffer, &outBufferSize);
        if (!NT_SUCCESS(status)) break;

        input = (PWINPODS_CONNECT_INPUT)inBuffer;
        output = (PWINPODS_CONNECT_OUTPUT)outBuffer;

        // Connect to L2CAP
        status = WinPodsConnectL2CAP(devCtx, input->BluetoothAddress, input->PSM);

        output->Success = NT_SUCCESS(status) ? 1 : 0;
        output->ChannelId = devCtx->ChannelId;
        bytesReturned = sizeof(WINPODS_CONNECT_OUTPUT);

        if (NT_SUCCESS(status)) {
            status = STATUS_SUCCESS;
        }
        break;
    }

    case IOCTL_WINPODS_DISCONNECT:
    {
        status = WinPodsDisconnectL2CAP(devCtx);
        bytesReturned = 0;
        break;
    }

    case IOCTL_WINPODS_SEND:
    {
        PWINPODS_TRANSFER_INPUT input;
        PUCHAR dataBuffer;

        if (InputBufferLength < sizeof(WINPODS_TRANSFER_INPUT)) {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        status = WdfRequestRetrieveInputBuffer(Request, InputBufferLength, &inBuffer, &inBufferSize);
        if (!NT_SUCCESS(status)) break;

        input = (PWINPODS_TRANSFER_INPUT)inBuffer;
        dataBuffer = (PUCHAR)input + sizeof(WINPODS_TRANSFER_INPUT);

        if (devCtx->ConnectionState != WinPodsConnected) {
            status = STATUS_DEVICE_NOT_CONNECTED;
            break;
        }

        status = WinPodsSendData(devCtx, dataBuffer, input->BufferSize, input->TimeoutMs);
        break;
    }

    case IOCTL_WINPODS_RECEIVE:
    {
        PWINPODS_TRANSFER_INPUT input;
        PWINPODS_RECEIVE_OUTPUT output;
        PUCHAR dataBuffer;
        SIZE_T bytesReceived = 0;

        if (InputBufferLength < sizeof(WINPODS_TRANSFER_INPUT) ||
            OutputBufferLength < sizeof(WINPODS_RECEIVE_OUTPUT)) {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        status = WdfRequestRetrieveInputBuffer(Request, sizeof(WINPODS_TRANSFER_INPUT), &inBuffer, &inBufferSize);
        if (!NT_SUCCESS(status)) break;

        status = WdfRequestRetrieveOutputBuffer(Request, OutputBufferLength, &outBuffer, &outBufferSize);
        if (!NT_SUCCESS(status)) break;

        input = (PWINPODS_TRANSFER_INPUT)inBuffer;
        output = (PWINPODS_RECEIVE_OUTPUT)outBuffer;
        dataBuffer = (PUCHAR)output + sizeof(WINPODS_RECEIVE_OUTPUT);

        if (devCtx->ConnectionState != WinPodsConnected) {
            output->BytesReceived = 0;
            output->ErrorCode = STATUS_DEVICE_NOT_CONNECTED;
            bytesReturned = sizeof(WINPODS_RECEIVE_OUTPUT);
            break;
        }

        status = WinPodsReceiveData(devCtx, dataBuffer, input->BufferSize, &bytesReceived, input->TimeoutMs);

        output->BytesReceived = (ULONG)bytesReceived;
        output->ErrorCode = status;
        bytesReturned = sizeof(WINPODS_RECEIVE_OUTPUT) + (ULONG)bytesReceived;

        // Return success even if receive failed (error code is in output)
        if (!NT_SUCCESS(status)) {
            bytesReturned = sizeof(WINPODS_RECEIVE_OUTPUT);
            status = STATUS_SUCCESS;
        }
        break;
    }

    case IOCTL_WINPODS_GET_STATUS:
    {
        if (OutputBufferLength < sizeof(WINPODS_STATUS_OUTPUT)) {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        status = WdfRequestRetrieveOutputBuffer(Request, sizeof(WINPODS_STATUS_OUTPUT), &outBuffer, &outBufferSize);
        if (!NT_SUCCESS(status)) break;

        PWINPODS_STATUS_OUTPUT output = (PWINPODS_STATUS_OUTPUT)outBuffer;
        output->ConnectionState = (ULONG)devCtx->ConnectionState;
        output->ConnectedAddress = devCtx->RemoteAddress;
        bytesReturned = sizeof(WINPODS_STATUS_OUTPUT);
        break;
    }

    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    WdfRequestCompleteWithInformation(Request, status, bytesReturned);
}

//=============================================================================
// L2CAP Operations
//=============================================================================

NTSTATUS
WinPodsConnectL2CAP(
    _In_ PDEVICE_CONTEXT Context,
    _In_ BTH_ADDR RemoteAddress,
    _In_ USHORT PSM
)
{
    NTSTATUS status;
    struct _BRB_L2CA_OPEN_CHANNEL* brb;
    HANDLE bthHandle;
    PVOID brbPool;

    KdPrint(("WinPodsAAP: Connecting to %012llX on PSM 0x%04X\n", RemoteAddress, PSM));

    // Check if already connected
    if (Context->ConnectionState == WinPodsConnected) {
        if (Context->RemoteAddress == RemoteAddress) {
            return STATUS_SUCCESS; // Already connected to same device
        }
        // Disconnect from previous device
        WinPodsDisconnectL2CAP(Context);
    }

    // Allocate BRB
    brbPool = ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(struct _BRB_L2CA_OPEN_CHANNEL), 'PDAW');
    if (!brbPool) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    brb = (struct _BRB_L2CA_OPEN_CHANNEL*)brbPool;

    // Initialize BRB for open channel
    RtlZeroMemory(brb, sizeof(struct _BRB_L2CA_OPEN_CHANNEL));
    brb->BrbHeader.BrbType = BRB_L2CA_OPEN_CHANNEL;
    brb->BrbHeader.Length = sizeof(struct _BRB_L2CA_OPEN_CHANNEL);

    // Set connection parameters
    brb->BtAddress = RemoteAddress;
    brb->Psm = PSM;

    // Channel configuration
    brb->ConfigOut.Flags = CF_FLUSHABLE;
    brb->ConfigIn.Flags = CF_FLUSHABLE;

    // MTU settings
    brb->ConfigOut.MaxMtu = 1024;
    brb->ConfigOut.MinMtu = 48;
    brb->ConfigIn.MaxMtu = 1024;
    brb->ConfigIn.MinMtu = 48;

    // Flush timeout
    brb->ConfigOut.FlushTO = 0xFFFF;
    brb->ConfigIn.FlushTO = 0xFFFF;

    // Link policy
    brb->ConfigOut.ExtraOptions = NULL;
    brb->ConfigOut.NumExtraOptions = 0;

    // Callbacks
    brb->ChannelHandle = NULL;
    brb->ConnectStatus = STATUS_UNSUCCESSFUL;

    // Store BRB in context
    Context->CurrentBrb = (struct _BRB*)brb;
    Context->ConnectionState = WinPodsConnecting;
    Context->RemoteAddress = RemoteAddress;
    Context->PSM = PSM;

    // Submit BRB to Bluetooth stack
    // Note: In a real driver, you would get the BTH_INTERFACE and call its submit BRB function
    // For this skeleton, we assume the Bluetooth driver interfaces are available

    // Wait for completion (simplified - real driver uses async callbacks)
    KeInitializeEvent(&Context->ConnectComplete, NotificationEvent, FALSE);

    // Submit the BRB asynchronously
    // bthInterface->BrbSubmit(bthInterface->Context, (PBRB)brb, WinPodsChannelConnectComplete, Context);

    // For now, simulate connection (real implementation would use Bluetooth stack)
    // This is a placeholder - the actual Bluetooth stack integration requires
    // obtaining BTH_INTERFACE from the Bluetooth bus driver

    KdPrint(("WinPodsAAP: Connection request submitted\n"));

    // In production:
    // status = WdfWaitForSingleObject(&Context->ConnectComplete, FALSE, &timeout);
    // if (!NT_SUCCESS(status)) { ... }

    // Simplified success for skeleton
    Context->ConnectionState = WinPodsConnected;
    Context->ChannelId = 0x0040; // Placeholder

    return STATUS_SUCCESS;
}

NTSTATUS
WinPodsDisconnectL2CAP(
    _In_ PDEVICE_CONTEXT Context
)
{
    KdPrint(("WinPodsAAP: Disconnecting\n"));

    if (Context->ConnectionState == WinPodsDisconnected) {
        return STATUS_SUCCESS;
    }

    Context->ConnectionState = WinPodsDisconnected;
    Context->RemoteAddress = 0;
    Context->ChannelId = 0;

    // Free receive buffer
    if (Context->ReceiveBuffer) {
        ExFreePoolWithTag(Context->ReceiveBuffer, 'PDAW');
        Context->ReceiveBuffer = NULL;
    }

    // In production, would submit BRB_L2CA_CLOSE_CHANNEL here

    KdPrint(("WinPodsAAP: Disconnected\n"));
    return STATUS_SUCCESS;
}

NTSTATUS
WinPodsSendData(
    _In_ PDEVICE_CONTEXT Context,
    _In_ PUCHAR Buffer,
    _In_ SIZE_T BufferSize,
    _In_ ULONG TimeoutMs
)
{
    UNREFERENCED_PARAMETER(TimeoutMs);

    if (Context->ConnectionState != WinPodsConnected) {
        return STATUS_DEVICE_NOT_CONNECTED;
    }

    KdPrint(("WinPodsAAP: Sending %llu bytes\n", BufferSize));

    // In production, would submit BRB_L2CA_ACL_TRANSFER for sending
    // For this skeleton, we just log the data

    return STATUS_SUCCESS;
}

NTSTATUS
WinPodsReceiveData(
    _In_ PDEVICE_CONTEXT Context,
    _Out_ PUCHAR Buffer,
    _In_ SIZE_T BufferSize,
    _Out_ PSIZE_T BytesReceived,
    _In_ ULONG TimeoutMs
)
{
    UNREFERENCED_PARAMETER(TimeoutMs);

    if (Context->ConnectionState != WinPodsConnected) {
        *BytesReceived = 0;
        return STATUS_DEVICE_NOT_CONNECTED;
    }

    KdPrint(("WinPodsAAP: Receiving up to %llu bytes\n", BufferSize));

    // In production, would submit BRB_L2CA_ACL_TRANSFER for receiving
    // For this skeleton, return no data

    *BytesReceived = 0;
    return STATUS_SUCCESS;
}

//=============================================================================
// L2CAP Callbacks
//=============================================================================

VOID
WinPodsChannelConnectComplete(
    _In_ struct _BRB_L2CA_OPEN_CHANNEL* Brb,
    _In_ NTSTATUS Status
)
{
    PDEVICE_CONTEXT context = (PDEVICE_CONTEXT)Brb->BrbHeader.BrbType;

    KdPrint(("WinPodsAAP: Connect complete, status: 0x%08X\n", Status));

    if (NT_SUCCESS(Status)) {
        context->ConnectionState = WinPodsConnected;
        context->ChannelId = 0x0040; // From Brb->ChannelHandle
    } else {
        context->ConnectionState = WinPodsDisconnected;
        context->RemoteAddress = 0;
    }

    KeSetEvent(&context->ConnectComplete, IO_NO_INCREMENT, FALSE);
}

VOID
WinPodsChannelReceiveComplete(
    _In_ struct _BRB_L2CA_ACL_TRANSFER* Brb,
    _In_ NTSTATUS Status
)
{
    UNREFERENCED_PARAMETER(Brb);
    UNREFERENCED_PARAMETER(Status);

    KdPrint(("WinPodsAAP: Receive complete\n"));

    // In production, would copy data to receive buffer and signal completion
}
