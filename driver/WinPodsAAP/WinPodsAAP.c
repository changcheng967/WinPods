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

#define WINPODS_POOL_TAG 'PDAW'
#define DEFAULT_TIMEOUT_MS 5000
#define RECEIVE_BUFFER_SIZE 4096

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
        KdPrint(("WinPodsAAP: Invalid BRB type: %d\n", BrbType));
        return STATUS_INVALID_PARAMETER;
    }

    // Allocate from non-paged pool
    brb = (PBRB)ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        brbSize,
        WINPODS_POOL_TAG
    );

    if (brb == NULL) {
        KdPrint(("WinPodsAAP: Failed to allocate BRB\n"));
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlZeroMemory(brb, brbSize);
    brb->BrbHeader.BrbType = BrbType;
    brb->BrbHeader.Length = (USHORT)brbSize;

    *Brb = brb;
    KdPrint(("WinPodsAAP: Allocated BRB type %d, size %llu\n", BrbType, brbSize));
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

/**
 * WinPodsSubmitBrbSynchronously
 *
 * Submits a BRB to the Bluetooth stack using the correct WDF pattern.
 * Uses WdfIoTargetSendInternalIoctlSynchronously with IOCTL_INTERNAL_BTH_SUBMIT_BRB.
 */
NTSTATUS
WinPodsSubmitBrbSynchronously(
    _In_ PDEVICE_CONTEXT Context,
    _Inout_ PBRB Brb
)
{
    NTSTATUS status;
    WDF_MEMORY_DESCRIPTOR inputDescriptor;
    ULONG bytesReturned;

    if (Context->IoTarget == NULL) {
        KdPrint(("WinPodsAAP: IoTarget is NULL - driver not properly initialized\n"));
        return STATUS_DEVICE_NOT_READY;
    }

    // Create a memory descriptor for the BRB
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(
        &inputDescriptor,
        Brb,
        Brb->BrbHeader.Length
    );

    // Submit the BRB via internal IOCTL to the Bluetooth stack
    // This is the correct pattern per Microsoft's bthecho sample
    status = WdfIoTargetSendInternalIoctlSynchronously(
        Context->IoTarget,
        NULL,                           // Request (NULL = create new)
        IOCTL_INTERNAL_BTH_SUBMIT_BRB,  // Internal IOCTL code
        &inputDescriptor,               // Input buffer (BRB)
        NULL,                           // Output buffer (none)
        NULL,                           // RequestOptions
        &bytesReturned                  // Bytes returned
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: BRB submission failed: 0x%08X\n", status));
    } else {
        KdPrint(("WinPodsAAP: BRB submitted successfully, bytes: %lu\n", bytesReturned));
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

    WDF_DRIVER_CONFIG_INIT(&config, WinPodsEvtDeviceAdd);
    config.EvtDriverContextCleanup = WinPodsEvtDriverContextCleanup;

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

    WdfDeviceInitSetExclusive(DeviceInit, TRUE);
    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_BLUETOOTH);
    WdfDeviceInitSetCharacteristics(DeviceInit, FILE_DEVICE_SECURE_OPEN, FALSE);

    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpPowerCallbacks);
    pnpPowerCallbacks.EvtDevicePrepareHardware = WinPodsEvtDevicePrepareHardware;
    pnpPowerCallbacks.EvtDeviceReleaseHardware = WinPodsEvtDeviceReleaseHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(DeviceInit, &pnpPowerCallbacks);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttrs, DEVICE_CONTEXT);
    deviceAttrs.EvtCleanupCallback = WinPodsEvtDeviceContextCleanup;

    status = WdfDeviceCreate(&DeviceInit, &deviceAttrs, &device);
    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfDeviceCreate failed: 0x%08X\n", status));
        return status;
    }

    devCtx = GetDeviceContext(device);
    RtlZeroMemory(devCtx, sizeof(DEVICE_CONTEXT));
    devCtx->ConnectionState = WinPodsDisconnected;
    devCtx->HasBthInterface = FALSE;

    // Initialize events
    KeInitializeEvent(&devCtx->ConnectCompleteEvent, NotificationEvent, FALSE);
    KeInitializeEvent(&devCtx->TransferCompleteEvent, NotificationEvent, FALSE);

    // Create spinlock
    status = WdfSpinLockCreate(WDF_NO_OBJECT_ATTRIBUTES, &devCtx->Lock);
    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: WdfSpinLockCreate failed: 0x%08X\n", status));
        return status;
    }

    // Create default IO queue
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

    // Get the I/O target for BRB submission
    // This is used with WdfIoTargetSendInternalIoctlSynchronously
    devCtx->IoTarget = WdfDeviceGetIoTarget(Device);
    if (devCtx->IoTarget == NULL) {
        KdPrint(("WinPodsAAP: Failed to get I/O target\n"));
        // Continue anyway - we may still be able to function in degraded mode
    }

    // Query the Bluetooth profile driver interface
    // This gives us BthAllocateBrb and BthFreeBrb functions
    RtlZeroMemory(&devCtx->BthInterface, sizeof(BTH_PROFILE_DRIVER_INTERFACE));
    devCtx->BthInterface.InterfaceHeader.Size = sizeof(BTH_PROFILE_DRIVER_INTERFACE);
    devCtx->BthInterface.InterfaceHeader.Version = 1;

    status = WdfFdoQueryForInterface(
        Device,
        &GUID_BTH_PROFILE_DRIVER_INTERFACE,
        (PINTERFACE)&devCtx->BthInterface,
        sizeof(BTH_PROFILE_DRIVER_INTERFACE),
        1,
        NULL
    );

    if (!NT_SUCCESS(status)) {
        //
        // IMPORTANT: This is expected when installed as Root\WinPodsAAP!
        //
        // The driver is installed as a software device under the Root enumerator,
        // not under the Bluetooth bus (BTHENUM). This means:
        // - WdfFdoQueryForInterface will fail because there's no Bluetooth parent
        // - BRB allocation via BthAllocateBrb will NOT be available
        // - We'll fall back to our own BRB allocation
        // - BRB submission via IoTarget may still work if the Bluetooth stack is present
        //
        // For production use, this driver should be enumerated under BTHENUM:
        //   BTHENUM\{E3A4B7F8-1C2D-4A5B-9E6F-0D1A2B3C4D5E}
        //
        KdPrint(("WinPodsAAP: =============================================\n"));
        KdPrint(("WinPodsAAP: BTH_PROFILE_DRIVER_INTERFACE query failed: 0x%08X\n", status));
        KdPrint(("WinPodsAAP: This is expected for Root\\WinPodsAAP installation.\n"));
        KdPrint(("WinPodsAAP: The driver will use fallback BRB allocation.\n"));
        KdPrint(("WinPodsAAP: For full functionality, enumerate under BTHENUM.\n"));
        KdPrint(("WinPodsAAP: =============================================\n"));
        devCtx->HasBthInterface = FALSE;
    } else {
        KdPrint(("WinPodsAAP: Successfully queried BTH_PROFILE_DRIVER_INTERFACE\n"));
        devCtx->HasBthInterface = TRUE;
    }

    // Allocate receive buffer
    devCtx->ReceiveBufferSize = RECEIVE_BUFFER_SIZE;
    devCtx->ReceiveBuffer = ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        devCtx->ReceiveBufferSize,
        WINPODS_POOL_TAG
    );
    if (devCtx->ReceiveBuffer == NULL) {
        KdPrint(("WinPodsAAP: Failed to allocate receive buffer\n"));
        // Non-fatal
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

    if (devCtx->ConnectionState != WinPodsDisconnected) {
        WinPodsDisconnectL2CAP(devCtx);
    }

    if (devCtx->ReceiveBuffer != NULL) {
        ExFreePoolWithTag(devCtx->ReceiveBuffer, WINPODS_POOL_TAG);
        devCtx->ReceiveBuffer = NULL;
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

    if (devCtx->ConnectionState != WinPodsDisconnected) {
        WinPodsDisconnectL2CAP(devCtx);
    }

    if (devCtx->ReceiveBuffer != NULL) {
        ExFreePoolWithTag(devCtx->ReceiveBuffer, WINPODS_POOL_TAG);
        devCtx->ReceiveBuffer = NULL;
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

        status = WinPodsConnectL2CAP(devCtx, input->BluetoothAddress, input->PSM);

        output->Success = NT_SUCCESS(status) ? 1 : 0;
        output->ChannelId = 0;  // Channel handle is internal
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
    _In_opt_ PVOID Context,
    _In_ INDICATION_CODE Indication,
    _In_ PINDICATION_PARAMETERS Parameters
)
{
    PDEVICE_CONTEXT devCtx = (PDEVICE_CONTEXT)Context;

    KdPrint(("WinPodsAAP: L2CAP Indication: %d\n", Indication));

    if (devCtx == NULL) {
        KdPrint(("WinPodsAAP: Indication callback with NULL context\n"));
        return;
    }

    if (Parameters == NULL) {
        KdPrint(("WinPodsAAP: Indication callback with NULL parameters\n"));
        return;
    }

    switch (Indication) {
    case IndicationRemoteDisconnect:
        //
        // The remote device has disconnected the L2CAP channel
        // Parameters->Disconnect contains the disconnect details
        //
        KdPrint(("WinPodsAAP: Remote device disconnected (reason: 0x%X)\n",
                 Parameters->Disconnect.Reason));

        WdfSpinLockAcquire(devCtx->Lock);
        devCtx->ConnectionState = WinPodsDisconnected;
        devCtx->ChannelHandle = NULL;
        devCtx->RemoteAddress = 0;
        devCtx->PSM = 0;
        WdfSpinLockRelease(devCtx->Lock);
        break;

    case IndicationAddReference:
    case IndicationReleaseReference:
        // Reference counting - not needed for our simple implementation
        break;

    default:
        KdPrint(("WinPodsAAP: Unhandled indication: %d\n", Indication));
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

    // Check IoTarget
    if (Context->IoTarget == NULL) {
        KdPrint(("WinPodsAAP: No I/O target available\n"));
        return STATUS_DEVICE_NOT_READY;
    }

    if (Context->ConnectionState == WinPodsConnected) {
        if (Context->RemoteAddress == RemoteAddress) {
            return STATUS_SUCCESS;
        }
        WinPodsDisconnectL2CAP(Context);
    }

    // Allocate BRB
    if (Context->HasBthInterface && Context->BthInterface.BthAllocateBrb != NULL) {
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

    // Channel configuration using the union structure
    // The BRB_L2CA_OPEN_CHANNEL has a union with Params.Client / Params.Server
    // For client connections, use Params.Client
    openBrb->Params.Client.ConfigOut.Flags = CF_FLUSHABLE;
    openBrb->Params.Client.ConfigOut.FlushTO = 0xFFFF;
    openBrb->Params.Client.ConfigOut.MaxMtu = 1024;
    openBrb->Params.Client.ConfigOut.MinMtu = 48;
    openBrb->Params.Client.ConfigOut.Mtu = 1024;

    openBrb->Params.Client.ConfigIn.Flags = CF_FLUSHABLE;
    openBrb->Params.Client.ConfigIn.FlushTO = 0xFFFF;
    openBrb->Params.Client.ConfigIn.MaxMtu = 1024;
    openBrb->Params.Client.ConfigIn.MinMtu = 48;
    openBrb->Params.Client.ConfigIn.Mtu = 1024;

    openBrb->Params.Client.ExtraOptions = NULL;
    openBrb->Params.Client.NumExtraOptions = 0;
    openBrb->Params.Client.LinkTO = 0;

    // Register indication callback
    openBrb->Params.Client.IndicationCallback = WinPodsL2capIndicationCallback;
    openBrb->Params.Client.IndicationCallbackContext = Context;
    openBrb->Params.Client.IndicationFlags = 0;

    // Initialize output fields
    openBrb->Params.Client.ChannelHandle = NULL;
    openBrb->Params.Client.ConnectStatus = STATUS_UNSUCCESSFUL;

    // Update context state
    WdfSpinLockAcquire(Context->Lock);
    Context->ConnectionState = WinPodsConnecting;
    Context->RemoteAddress = RemoteAddress;
    Context->PSM = PSM;
    WdfSpinLockRelease(Context->Lock);

    // Submit BRB using the correct WDF pattern
    status = WinPodsSubmitBrbSynchronously(Context, brb);

    if (NT_SUCCESS(status)) {
        status = openBrb->Params.Client.ConnectStatus;

        if (NT_SUCCESS(status)) {
            WdfSpinLockAcquire(Context->Lock);
            Context->ChannelHandle = openBrb->Params.Client.ChannelHandle;
            Context->ConnectionState = WinPodsConnected;
            WdfSpinLockRelease(Context->Lock);

            KdPrint(("WinPodsAAP: Connected successfully\n"));
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
    if (Context->HasBthInterface && Context->BthInterface.BthFreeBrb != NULL) {
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

    if (Context->IoTarget == NULL || Context->ChannelHandle == NULL) {
        WdfSpinLockAcquire(Context->Lock);
        Context->ConnectionState = WinPodsDisconnected;
        Context->ChannelHandle = NULL;
        Context->RemoteAddress = 0;
        Context->PSM = 0;
        WdfSpinLockRelease(Context->Lock);
        return STATUS_SUCCESS;
    }

    // Allocate BRB
    if (Context->HasBthInterface && Context->BthInterface.BthAllocateBrb != NULL) {
        brb = Context->BthInterface.BthAllocateBrb(BRB_L2CA_CLOSE_CHANNEL, NULL);
    } else {
        status = WinPodsAllocateBrb(BRB_L2CA_CLOSE_CHANNEL, &brb);
        if (!NT_SUCCESS(status)) {
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
    closeBrb->CloseStatus = STATUS_SUCCESS;

    status = WinPodsSubmitBrbSynchronously(Context, brb);

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: Close channel BRB failed: 0x%08X\n", status));
    }

    // Free BRB
    if (Context->HasBthInterface && Context->BthInterface.BthFreeBrb != NULL) {
        Context->BthInterface.BthFreeBrb(brb);
    } else {
        WinPodsFreeBrb(brb);
    }

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

    if (Context->IoTarget == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (Buffer == NULL || BufferSize == 0) {
        return STATUS_INVALID_PARAMETER;
    }

    KdPrint(("WinPodsAAP: Sending %llu bytes\n", BufferSize));

    // Allocate BRB
    if (Context->HasBthInterface && Context->BthInterface.BthAllocateBrb != NULL) {
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

    aclBrb->ChannelHandle = Context->ChannelHandle;
    aclBrb->TransferFlags = ACL_TRANSFER_DIRECTION_OUT;
    aclBrb->Buffer = Buffer;
    aclBrb->BufferSize = (ULONG)BufferSize;
    aclBrb->Timeout = TimeoutMs > 0 ? TimeoutMs : DEFAULT_TIMEOUT_MS;

    status = WinPodsSubmitBrbSynchronously(Context, brb);

    if (!NT_SUCCESS(status)) {
        KdPrint(("WinPodsAAP: Send failed: 0x%08X\n", status));
    }

    // Free BRB
    if (Context->HasBthInterface && Context->BthInterface.BthFreeBrb != NULL) {
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

    if (Context->IoTarget == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (Buffer == NULL || BufferSize == 0) {
        return STATUS_INVALID_PARAMETER;
    }

    KdPrint(("WinPodsAAP: Receiving up to %llu bytes\n", BufferSize));

    // Allocate BRB
    if (Context->HasBthInterface && Context->BthInterface.BthAllocateBrb != NULL) {
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

    aclBrb->ChannelHandle = Context->ChannelHandle;
    aclBrb->TransferFlags = ACL_TRANSFER_DIRECTION_IN;
    aclBrb->Buffer = Buffer;
    aclBrb->BufferSize = (ULONG)BufferSize;
    aclBrb->Timeout = TimeoutMs > 0 ? TimeoutMs : DEFAULT_TIMEOUT_MS;

    status = WinPodsSubmitBrbSynchronously(Context, brb);

    if (NT_SUCCESS(status)) {
        *BytesReceived = aclBrb->BufferSize;
        KdPrint(("WinPodsAAP: Received %llu bytes\n", *BytesReceived));
    } else {
        KdPrint(("WinPodsAAP: Receive failed: 0x%08X\n", status));
    }

    // Free BRB
    if (Context->HasBthInterface && Context->BthInterface.BthFreeBrb != NULL) {
        Context->BthInterface.BthFreeBrb(brb);
    } else {
        WinPodsFreeBrb(brb);
    }

    return status;
}
