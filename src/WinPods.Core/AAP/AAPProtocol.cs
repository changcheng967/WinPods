namespace WinPods.Core.AAP;

/// <summary>
/// Handles AAP (Apple Accessory Protocol) packet framing and serialization.
/// Reference: https://github.com/tyalie/AAP-Protocol-Defintion
/// </summary>
public static class AAPProtocol
{
    // AAP packet header (4 bytes)
    // Byte 0-1: Marker (0x04 0x00)
    // Byte 2-3: Length of payload (little endian)

    // Payload structure:
    // Byte 4-5: Opcode (little endian)
    // Byte 6: Identifier
    // Byte 7+: Data (variable length)

    private const byte AAP_MARKER_0 = 0x04;
    private const byte AAP_MARKER_1 = 0x00;
    private const int HEADER_SIZE = 7; // 4 byte header + 2 byte opcode + 1 byte identifier

    /// <summary>
    /// Builds a complete AAP command packet.
    /// </summary>
    /// <param name="opcode">The command opcode.</param>
    /// <param name="identifier">The command identifier.</param>
    /// <param name="data">Optional data payload.</param>
    /// <returns>The complete packet bytes.</returns>
    public static byte[] BuildPacket(AAPCommandOpcode opcode, AAPIdentifier identifier, byte[]? data = null)
    {
        int dataLength = data?.Length ?? 0;
        int totalLength = HEADER_SIZE + dataLength;

        var packet = new byte[totalLength];
        int pos = 0;

        // Header marker
        packet[pos++] = AAP_MARKER_0;
        packet[pos++] = AAP_MARKER_1;

        // Payload length (opcode + identifier + data)
        ushort payloadLength = (ushort)(2 + 1 + dataLength);
        packet[pos++] = (byte)(payloadLength & 0xFF);
        packet[pos++] = (byte)((payloadLength >> 8) & 0xFF);

        // Opcode (little endian)
        packet[pos++] = (byte)((ushort)opcode & 0xFF);
        packet[pos++] = (byte)(((ushort)opcode >> 8) & 0xFF);

        // Identifier
        packet[pos++] = (byte)identifier;

        // Data
        if (data != null && dataLength > 0)
        {
            Buffer.BlockCopy(data, 0, packet, pos, dataLength);
        }

        return packet;
    }

    /// <summary>
    /// Builds an acknowledgement packet for a received message.
    /// </summary>
    public static byte[] BuildAcknowledgement(ushort originalOpcode, AAPIdentifier identifier)
    {
        return BuildPacket(AAPCommandOpcode.Acknowledgement, identifier,
            new byte[] { (byte)(originalOpcode & 0xFF), (byte)((originalOpcode >> 8) & 0xFF) });
    }

    /// <summary>
    /// Builds a response packet (for mock testing).
    /// </summary>
    public static byte[] BuildResponse(AAPCommandOpcode opcode, AAPIdentifier identifier, byte[]? data = null)
    {
        return BuildPacket(opcode, identifier, data);
    }

    /// <summary>
    /// Parses an AAP packet from raw bytes.
    /// </summary>
    /// <param name="packet">The raw packet bytes.</param>
    /// <returns>Parsed packet info, or null if invalid.</returns>
    public static (AAPCommandOpcode Opcode, AAPIdentifier Identifier, byte[] Data)? ParsePacket(byte[] packet)
    {
        if (packet == null || packet.Length < HEADER_SIZE)
            return null;

        // Validate header marker
        if (packet[0] != AAP_MARKER_0 || packet[1] != AAP_MARKER_1)
            return null;

        // Read payload length
        ushort payloadLength = (ushort)(packet[2] | (packet[3] << 8));

        // Validate packet length
        if (packet.Length < 4 + payloadLength)
            return null;

        // Read opcode
        ushort opcodeValue = (ushort)(packet[4] | (packet[5] << 8));
        if (!Enum.IsDefined(typeof(AAPCommandOpcode), opcodeValue))
            return null;

        var opcode = (AAPCommandOpcode)opcodeValue;

        // Read identifier
        byte identifierValue = packet[6];
        if (!Enum.IsDefined(typeof(AAPIdentifier), identifierValue))
        {
            // Unknown identifier - still parse but with caution
        }
        var identifier = (AAPIdentifier)identifierValue;

        // Read data
        int dataLength = payloadLength - 3; // Subtract opcode (2) + identifier (1)
        byte[] data = Array.Empty<byte>();

        if (dataLength > 0)
        {
            data = new byte[dataLength];
            Buffer.BlockCopy(packet, 7, data, 0, dataLength);
        }

        return (opcode, identifier, data);
    }

    /// <summary>
    /// Validates if a packet is properly formatted.
    /// </summary>
    public static bool IsValidPacket(byte[] packet)
    {
        return ParsePacket(packet) != null;
    }

    /// <summary>
    /// Extracts the opcode from a packet without full parsing.
    /// </summary>
    public static AAPCommandOpcode? GetOpcode(byte[] packet)
    {
        if (packet == null || packet.Length < 6)
            return null;

        if (packet[0] != AAP_MARKER_0 || packet[1] != AAP_MARKER_1)
            return null;

        ushort opcodeValue = (ushort)(packet[4] | (packet[5] << 8));
        if (Enum.IsDefined(typeof(AAPCommandOpcode), opcodeValue))
            return (AAPCommandOpcode)opcodeValue;

        return null;
    }

    /// <summary>
    /// Extracts the identifier from a packet without full parsing.
    /// </summary>
    public static AAPIdentifier? GetIdentifier(byte[] packet)
    {
        if (packet == null || packet.Length < 7)
            return null;

        if (packet[0] != AAP_MARKER_0 || packet[1] != AAP_MARKER_1)
            return null;

        byte identifierValue = packet[6];
        if (Enum.IsDefined(typeof(AAPIdentifier), identifierValue))
            return (AAPIdentifier)identifierValue;

        return null;
    }

    /// <summary>
    /// Formats a packet as a hex string for debugging.
    /// </summary>
    public static string FormatPacket(byte[] packet)
    {
        if (packet == null)
            return "<null>";

        return BitConverter.ToString(packet).Replace("-", " ");
    }

    /// <summary>
    /// Parses battery level data from an AAP notification.
    /// Battery data format varies by device model.
    /// </summary>
    public static AAPBatteryInfo? ParseBatteryData(byte[] data)
    {
        if (data == null || data.Length < 4)
            return null;

        // Standard format:
        // Byte 0: Left battery (0-10 scale, 0xFF = unknown)
        // Byte 1: Right battery (0-10 scale, 0xFF = unknown)
        // Byte 2: Case battery (0-10 scale, 0xFF = unknown)
        // Byte 3: Charging flags (bit 0: left, bit 1: right, bit 2: case)

        byte leftRaw = data[0];
        byte rightRaw = data[1];
        byte caseRaw = data[2];
        byte chargingFlags = data.Length > 3 ? data[3] : (byte)0;

        return new AAPBatteryInfo
        {
            LeftPercentage = leftRaw == 0xFF ? (byte)0 : (byte)(leftRaw * 10),
            RightPercentage = rightRaw == 0xFF ? (byte)0 : (byte)(rightRaw * 10),
            CasePercentage = caseRaw == 0xFF ? (byte)0 : (byte)(caseRaw * 10),
            LeftCharging = (chargingFlags & 0x01) != 0,
            RightCharging = (chargingFlags & 0x02) != 0,
            CaseCharging = (chargingFlags & 0x04) != 0,
            LastUpdate = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Parses ear detection data from an AAP notification.
    /// </summary>
    public static AAPEarDetectionInfo? ParseEarDetectionData(byte[] data)
    {
        if (data == null || data.Length < 1)
            return null;

        // Ear detection byte:
        // Bit 0: Left in ear
        // Bit 1: Right in ear

        byte status = data[0];

        return new AAPEarDetectionInfo
        {
            LeftInEar = (status & 0x01) != 0,
            RightInEar = (status & 0x02) != 0,
            LastUpdate = DateTime.UtcNow
        };
    }
}
