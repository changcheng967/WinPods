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

#include "WinPodsAAP.h"

//=============================================================================
// Global Variables
//=============================================================================

// Pool tag for allocations
#define WINPODS_POOL_TAG 'PDAW'

// Default timeouts
#define DEFAULT_CONNECT_TIMEOUT_MS  10000
#define DEFAULT_TRANSFER_TIMEOUT_MS 5000

//=============================================================================
// Forward declarations
//=============================================================================

EVT_WDF_DEVICE_PREPARE_HARDWARE WinPodsEvtDevicePrepareHardware;
EVT_WDF_DEVICE_RELEASE_HARDWARE WinPodsEvtDeviceReleaseHardware;

//=============================================================================
// BRB Helper Functions
//=============================================================================

NTSTATUS
WinPodsAllocateBrb(
    _In_ BRB_TYPE BrbType,
    _Out_ PBRB* Brb
)
{
    PBRB brb;
    SIZE_T brbSize;

    *Brb = NULL;

    // Determine size based on BRB type
    switch (BrbType) {
    case BRB_L2CA_OPEN_CHANNEL:
        brbSize = sizeof(struct _BRB_L2CA_OPEN_CHANNEL);
        break;
    case BRB_L2CA_CLOSE_CHANNEL:
        brbSize = sizeof(struct _BRB_L2CA_CLOSE_CHANNEL);
        break;
    case BRB_L2CA_ACL_TRANSFER:
        brbSize = sizeof(struct _BRB_L2CA_ACL_TRANSFER);
        break;
    default:
        return STATUS_INVALID_PARAMETER;
    }

    // Allocate from non-paged pool
    brb = (PBRB)ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        brbSize,
        WINPODS_POOL_TAG
    );

    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlZeroMemory(brb, brbSize);
    brb->BrbHeader.BrbType = BrbType;
    brb->BrbHeader.Length = (USHORT)brbSize;

    *Brb = brb;
    return STATUS_SUCCESS;
}

VOID
WinPodsFreeBrb(
    _In_ PBRB Brb
)
{
    if (Brb != NULL) {
        ExFreePoolWithTag(Brb, WINPODS_POOL_TAG);
    }
}

NTSTATUS
WinPodsSubmitBrbSynchronously(
    _In_ PDEVICE_CONTEXT Context,
    _Inout_ PBRB Brb,
    _In_ ULONG TimeoutMs
)
{
    NTSTATUS status;
    IO_STATUS_BLOCK ioStatus = {0};

    if (Context->BthInterface.BrbSubmit == NULL) {
        KdPrint(("WinPodsAAP: BthInterface.BrbSubmit is NULL\n"));
        return STATUS_DEVICE_NOT_READY;
    }

    // Submit BRB using the Bluetooth profile driver interface
    status = Context->BthInterface.BrbSubmit(
        Context->BthInterface.Context,
        Brb,
        &ioStatus
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: BrbSubmit failed: 0x%08X\n", status));
    }

    return status;
}

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

VOID
WinPodsEvtDriverContextCleanup(
    _In_ WDFOBJECT DriverObject
)
{
    UNREFERENCED_PARAMETER(DriverObject);
    KdPrint(("WinPodsAAP: Driver cleanup\n"));
}

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
    WDF_PNPPOWER_EVENT_CALLBACKS pnpPowerCallbacks;

    UNREFERENCED_PARAMETER(Driver);

    KdPrint(("WinPodsAAP: WinPodsEvtDeviceAdd\n"));

    // Set device as exclusive (only one app at a time)
    WdfDeviceInitSetExclusive(DeviceInit, TRUE);

    // Set device characteristics
    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_BLUETOOTH);
    WdfDeviceInitSetCharacteristics(DeviceInit, FILE_DEVICE_SECURE_OPEN, FALSE);

    // Register PNP/power callbacks
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpPowerCallbacks);
    pnpPowerCallbacks.EvtDevicePrepareHardware = WinPodsEvtDevicePrepareHardware;
    pnpPowerCallbacks.EvtDeviceReleaseHardware = WinPodsEvtDeviceReleaseHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(DeviceInit, &pnpPowerCallbacks);

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

    // Initialize events
    KeInitializeEvent(&devCtx->ConnectCompleteEvent, NotificationEvent, FALSE);
    KeInitializeEvent(&devCtx->TransferCompleteEvent, NotificationEvent, FALSE);

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

NTSTATUS
WinPodsEvtDevicePrepareHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesRaw,
    _In_ WDFCMRESLIST ResourcesTranslated
)
{
    NTSTATUS status;
    PDEVICE_CONTEXT devCtx;

    UNREFERENCED_PARAMETER(ResourcesRaw);
    UNREFERENCED_PARAMETER(ResourcesTranslated);

    KdPrint(("WinPodsAAP: WinPodsEvtDevicePrepareHardware\n"));

    devCtx = GetDeviceContext(Device);

    // Query the Bluetooth profile driver interface from the bus driver
    // This gives us access to BrbSubmit for L2CAP operations
    RtlZeroMemory(&devCtx->BthInterface, sizeof(BTH_PROFILE_DRIVER_INTERFACE));
    devCtx->BthInterface.InterfaceHeader.Size = sizeof(BTH_PROFILE_DRIVER_INTERFACE);
    devCtx->BthInterface.InterfaceHeader.Version = 1;
    devCtx->BthInterface.InterfaceHeader.Context = (PVOID)Device;

    // Query the interface from the parent bus (Bluetooth driver)
    status = WdfFdoQueryForInterface(
        Device,
        &GUID_BTH_PROFILE_DRIVER_INTERFACE,
        (PINTERFACE)&devCtx->BthInterface,
        sizeof(BTH_PROFILE_DRIVER_INTERFACE),
        1,  // Version
        NULL  // InterfaceSpecificData
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfFdoQueryForInterface failed: 0x%08X\n", status));
        KdPrint(("WinPodsAAP: Note: This driver requires the Bluetooth stack to be available\n"));
        // Continue anyway - we'll fail operations that need the interface
    } else {
        KdPrint(("WinPodsAAP: Successfully queried BTH_PROFILE_DRIVER_INTERFACE\n"));
    }

    return STATUS_SUCCESS;
}

NTSTATUS
WinPodsEvtDeviceReleaseHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesTranslated
)
{
    PDEVICE_CONTEXT devCtx;

    UNREFERENCED_PARAMETER(ResourcesTranslated);

    KdPrint(("WinPodsAAP: WinPodsEvtDeviceReleaseHardware\n"));

    devCtx = GetDeviceContext(Device);

    // Disconnect if still connected
    if (devCtx->ConnectionState != WinPodsDisconnected) {
        WinPodsDisconnectL2CAP(devCtx);
    }

    return STATUS_SUCCESS;
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
        output->ChannelId = (USHORT)(ULONG_PTR)devCtx->ChannelHandle;
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
        dataBuffer = (PUCHAR)((PUCHAR)input + sizeof(WINPODS_TRANSFER_INPUT));

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
        dataBuffer = (PUCHAR)((PUCHAR)output + sizeof(WINPODS_RECEIVE_OUTPUT));

        if (devCtx->ConnectionState != WinPodsConnected) {
            output->BytesReceived = 0;
            output->ErrorCode = STATUS_DEVICE_NOT_CONNECTED;
            bytesReturned = sizeof(WINPODS_RECEIVE_OUTPUT);
            status = STATUS_SUCCESS;
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
// L2CAP Indication Callback
//=============================================================================

VOID
WinPodsL2capIndicationCallback(
    _In_ L2CAP_CHANNEL_HANDLE ChannelHandle,
    _In_ ULONG Indication,
    _In_ PVOID Parameters,
    _In_ ULONG ParameterLength,
    _In_opt_ PVOID Context
)
{
    PDEVICE_CONTEXT devCtx = (PDEVICE_CONTEXT)Context;

    UNREFERENCED_PARAMETER(ChannelHandle);
    UNREFERENCED_PARAMETER(Parameters);
    UNREFERENCED_PARAMETER(ParameterLength);

    KdPrint(("WinPodsAAP: L2CAP Indication: %lu\n", Indication));

    if (devCtx == NULL) {
        return;
    }

    switch (Indication) {
    case L2CAP_CHANNEL_DISCONNECTED:
        KdPrint(("WinPodsAAP: Remote device disconnected\n"));
        WdfSpinLockAcquire(devCtx->Lock);
        devCtx->ConnectionState = WinPodsDisconnected;
        devCtx->ChannelHandle = NULL;
        devCtx->RemoteAddress = 0;
        WdfSpinLockRelease(devCtx->Lock);
        break;

    case L2CAP_CHANNEL_CLOSING:
        KdPrint(("WinPodsAAP: Channel closing\n"));
        break;

    case L2CAP_CHANNEL_CLOSED:
        KdPrint(("WinPodsAAP: Channel closed\n"));
        break;
    }
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
    PBRB brb;
    struct _BRB_L2CA_OPEN_CHANNEL* openBrb;

    KdPrint(("WinPodsAAP: Connecting to %012llX on PSM 0x%04X\n", RemoteAddress, PSM));

    // Check if we have the Bluetooth interface
    if (Context->BthInterface.BrbSubmit == NULL) {
        KdPrint(("WinPodsAAP: No Bluetooth interface available\n"));
        return STATUS_DEVICE_NOT_READY;
    }

    // Check if already connected
    if (Context->ConnectionState == WinPodsConnected) {
        if (Context->RemoteAddress == RemoteAddress) {
            return STATUS_SUCCESS; // Already connected to same device
        }
        // Disconnect from previous device
        WinPodsDisconnectL2CAP(Context);
    }

    // Allocate BRB for open channel
    if (Context->BthInterface.BthAllocateBrb != NULL) {
        brb = Context->BthInterface.BthAllocateBrb(BRB_L2CA_OPEN_CHANNEL, NULL);
    } else {
        status = WinPodsAllocateBrb(BRB_L2CA_OPEN_CHANNEL, &brb);
        if (!NT_SUCCESS(status)) {
            return status;
        }
    }

    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    openBrb = (struct _BRB_L2CA_OPEN_CHANNEL*)brb;

    // Set connection parameters
    openBrb->BtAddress = RemoteAddress;
    openBrb->Psm = PSM;

    // Channel configuration - outbound
    openBrb->ConfigOut.Flags = CF_FLUSHABLE;
    openBrb->ConfigOut.FlushTO = 0xFFFF;

    // MTU settings
    openBrb->ConfigOut.MaxMtu = 1024;
    openBrb->ConfigOut.MinMtu = 48;
    openBrb->ConfigOut.Mtu = 1024;

    // Inbound channel configuration
    openBrb->ConfigIn.Flags = CF_FLUSHABLE;
    openBrb->ConfigIn.FlushTO = 0xFFFF;
    openBrb->ConfigIn.MaxMtu = 1024;
    openBrb->ConfigIn.MinMtu = 48;
    openBrb->ConfigIn.Mtu = 1024;

    // Register indication callback for remote disconnect notifications
    openBrb->IndicationCallback = WinPodsL2capIndicationCallback;
    openBrb->IndicationCallbackContext = Context;
    openBrb->IndicationFlags = 0;

    // Initialize output fields
    openBrb->ChannelHandle = NULL;
    openBrb->ConnectStatus = STATUS_UNSUCCESSFUL;

    // Update context state
    WdfSpinLockAcquire(Context->Lock);
    Context->ConnectionState = WinPodsConnecting;
    Context->RemoteAddress = RemoteAddress;
    Context->PSM = PSM;
    WdfSpinLockRelease(Context->Lock);

    // Submit BRB
    status = WinPodsSubmitBrbSynchronously(Context, brb, DEFAULT_CONNECT_TIMEOUT_MS);

    if (NT_SUCCESS(status)) {
        // Check connection status from BRB
        status = openBrb->ConnectStatus;

        if (NT_SUCCESS(status)) {
            // Connection successful
            WdfSpinLockAcquire(Context->Lock);
            Context->ChannelHandle = openBrb->ChannelHandle;
            Context->ConnectionState = WinPodsConnected;
            WdfSpinLockRelease(Context->Lock);

            KdPrint(("WinPodsAAP: Connected successfully, ChannelHandle: 0x%p\n",
                     Context->ChannelHandle));
        } else {
            KdPrint(("WinPodsAAP: Connection failed with status: 0x%08X\n", status));
            WdfSpinLockAcquire(Context->Lock);
            Context->ConnectionState = WinPodsDisconnected;
            Context->RemoteAddress = 0;
            Context->PSM = 0;
            WdfSpinLockRelease(Context->Lock);
        }
    } else {
        KdPrint(("WinPodsAAP: BRB submission failed: 0x%08X\n", status));
        WdfSpinLockAcquire(Context->Lock);
        Context->ConnectionState = WinPodsDisconnected;
        Context->RemoteAddress = 0;
        Context->PSM = 0;
        WdfSpinLockRelease(Context->Lock);
    }

    // Free BRB
    if (Context->BthInterface.BthFreeBrb != NULL) {
        Context->BthInterface.BthFreeBrb(brb);
    } else {
        WinPodsFreeBrb(brb);
    }

    return status;
}

NTSTATUS
WinPodsDisconnectL2CAP(
    _In_ PDEVICE_CONTEXT Context
)
{
    NTSTATUS status;
    PBRB brb;
    struct _BRB_L2CA_CLOSE_CHANNEL* closeBrb;

    KdPrint(("WinPodsAAP: Disconnecting\n"));

    if (Context->ConnectionState == WinPodsDisconnected) {
        return STATUS_SUCCESS;
    }

    // Check if we have the Bluetooth interface and a valid channel
    if (Context->BthInterface.BrbSubmit == NULL || Context->ChannelHandle == NULL) {
        // No interface or channel, just clear state
        WdfSpinLockAcquire(Context->Lock);
        Context->ConnectionState = WinPodsDisconnected;
        Context->ChannelHandle = NULL;
        Context->RemoteAddress = 0;
        Context->PSM = 0;
        WdfSpinLockRelease(Context->Lock);
        return STATUS_SUCCESS;
    }

    // Allocate BRB for close channel
    if (Context->BthInterface.BthAllocateBrb != NULL) {
        brb = Context->BthInterface.BthAllocateBrb(BRB_L2CA_CLOSE_CHANNEL, NULL);
    } else {
        status = WinPodsAllocateBrb(BRB_L2CA_CLOSE_CHANNEL, &brb);
        if (!NT_SUCCESS(status)) {
            // Failed to allocate, just clear state
            WdfSpinLockAcquire(Context->Lock);
            Context->ConnectionState = WinPodsDisconnected;
            Context->ChannelHandle = NULL;
            Context->RemoteAddress = 0;
            Context->PSM = 0;
            WdfSpinLockRelease(Context->Lock);
            return STATUS_SUCCESS;
        }
    }

    if (brb == NULL) {
        WdfSpinLockAcquire(Context->Lock);
        Context->ConnectionState = WinPodsDisconnected;
        Context->ChannelHandle = NULL;
        Context->RemoteAddress = 0;
        Context->PSM = 0;
        WdfSpinLockRelease(Context->Lock);
        return STATUS_SUCCESS;
    }

    closeBrb = (struct _BRB_L2CA_CLOSE_CHANNEL*)brb;
    closeBrb->ChannelHandle = Context->ChannelHandle;

    // Submit BRB
    status = WinPodsSubmitBrbSynchronously(Context, brb, DEFAULT_CONNECT_TIMEOUT_MS);

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: Close channel BRB failed: 0x%08X\n", status));
    }

    // Free BRB
    if (Context->BthInterface.BthFreeBrb != NULL) {
        Context->BthInterface.BthFreeBrb(brb);
    } else {
        WinPodsFreeBrb(brb);
    }

    // Clear state
    WdfSpinLockAcquire(Context->Lock);
    Context->ConnectionState = WinPodsDisconnected;
    Context->ChannelHandle = NULL;
    Context->RemoteAddress = 0;
    Context->PSM = 0;
    WdfSpinLockRelease(Context->Lock);

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
    NTSTATUS status;
    PBRB brb;
    struct _BRB_L2CA_ACL_TRANSFER* aclBrb;

    if (Context->ConnectionState != WinPodsConnected || Context->ChannelHandle == NULL) {
        return STATUS_DEVICE_NOT_CONNECTED;
    }

    if (Context->BthInterface.BrbSubmit == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (Buffer == NULL || BufferSize == 0) {
        return STATUS_INVALID_PARAMETER;
    }

    KdPrint(("WinPodsAAP: Sending %llu bytes\n", BufferSize));

    // Allocate BRB for ACL transfer
    if (Context->BthInterface.BthAllocateBrb != NULL) {
        brb = Context->BthInterface.BthAllocateBrb(BRB_L2CA_ACL_TRANSFER, NULL);
    } else {
        status = WinPodsAllocateBrb(BRB_L2CA_ACL_TRANSFER, &brb);
        if (!NT_SUCCESS(status)) {
            return status;
        }
    }

    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    aclBrb = (struct _BRB_L2CA_ACL_TRANSFER*)brb;

    // Initialize BRB for ACL transfer (outbound)
    aclBrb->ChannelHandle = Context->ChannelHandle;
    aclBrb->TransferFlags = ACL_TRANSFER_DIRECTION_OUT;
    aclBrb->Buffer = Buffer;
    aclBrb->BufferSize = (ULONG)BufferSize;
    aclBrb->Timeout = TimeoutMs > 0 ? TimeoutMs : DEFAULT_TRANSFER_TIMEOUT_MS;

    // Submit BRB
    status = WinPodsSubmitBrbSynchronously(Context, brb, aclBrb->Timeout);

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: Send failed: 0x%08X\n", status));
    }

    // Free BRB
    if (Context->BthInterface.BthFreeBrb != NULL) {
        Context->BthInterface.BthFreeBrb(brb);
    } else {
        WinPodsFreeBrb(brb);
    }

    return status;
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
    NTSTATUS status;
    PBRB brb;
    struct _BRB_L2CA_ACL_TRANSFER* aclBrb;

    *BytesReceived = 0;

    if (Context->ConnectionState != WinPodsConnected || Context->ChannelHandle == NULL) {
        return STATUS_DEVICE_NOT_CONNECTED;
    }

    if (Context->BthInterface.BrbSubmit == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (Buffer == NULL || BufferSize == 0) {
        return STATUS_INVALID_PARAMETER;
    }

    KdPrint(("WinPodsAAP: Receiving up to %llu bytes\n", BufferSize));

    // Allocate BRB for ACL transfer
    if (Context->BthInterface.BthAllocateBrb != NULL) {
        brb = Context->BthInterface.BthAllocateBrb(BRB_L2CA_ACL_TRANSFER, NULL);
    } else {
        status = WinPodsAllocateBrb(BRB_L2CA_ACL_TRANSFER, &brb);
        if (!NT_SUCCESS(status)) {
            return status;
        }
    }

    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    aclBrb = (struct _BRB_L2CA_ACL_TRANSFER*)brb;

    // Initialize BRB for ACL transfer (inbound)
    aclBrb->ChannelHandle = Context->ChannelHandle;
    aclBrb->TransferFlags = ACL_TRANSFER_DIRECTION_IN;
    aclBrb->Buffer = Buffer;
    aclBrb->BufferSize = (ULONG)BufferSize;
    aclBrb->Timeout = TimeoutMs > 0 ? TimeoutMs : DEFAULT_TRANSFER_TIMEOUT_MS;

    // Submit BRB
    status = WinPodsSubmitBrbSynchronously(Context, brb, aclBrb->Timeout);

    if (NT_SUCCESS(status)) {
        *BytesReceived = aclBrb->BufferSize;
        KdPrint(("WinPodsAAP: Received %llu bytes\n", *BytesReceived));
    } else {
        KdPrint(("WinPodsAAP: Receive failed: 0x%08X\n", status));
    }

    // Free BRB
    if (Context->BthInterface.BthFreeBrb != NULL) {
        Context->BthInterface.BthFreeBrb(brb);
    } else {
        WinPodsFreeBrb(brb);
    }

    return status;
}
