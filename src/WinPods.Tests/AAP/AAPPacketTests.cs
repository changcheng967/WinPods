using WinPods.Core.AAP;

namespace WinPods.Tests.AAP;

public class AAPPacketTests
{
    [Fact]
    public void BuildPacket_HasCorrectMarker()
    {
        // Arrange & Act
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, new byte[] { 0x01 });

        // Assert
        Assert.Equal(0x04, packet[0]);
        Assert.Equal(0x00, packet[1]);
    }

    [Fact]
    public void BuildPacket_PayloadLengthCorrect()
    {
        // Arrange
        byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        int dataLength = data.Length;

        // Act
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, data);

        // Assert
        // Payload = opcode (2) + identifier (1) + data (4) = 7
        ushort payloadLength = (ushort)(packet[2] | (packet[3] << 8));
        Assert.Equal(2 + 1 + dataLength, payloadLength);
    }

    [Fact]
    public void BuildPacket_NoData_HasMinimalLength()
    {
        // Arrange & Act
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, null);

        // Assert
        // Total length = 4 (header) + 2 (opcode) + 1 (identifier) = 7
        Assert.Equal(7, packet.Length);
    }

    [Fact]
    public void ParsePacket_RoundTrip()
    {
        // Arrange
        var opcode = AAPCommandOpcode.ControlCommand;
        var identifier = AAPIdentifier.ListeningMode;
        byte[] data = new byte[] { 0x01, 0x02, 0x03 };

        var packet = AAPProtocol.BuildPacket(opcode, identifier, data);

        // Act
        var result = AAPProtocol.ParsePacket(packet);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(opcode, result.Value.Opcode);
        Assert.Equal(identifier, result.Value.Identifier);
        Assert.Equal(data, result.Value.Data);
    }

    [Fact]
    public void ParsePacket_Garbage_ReturnsNull()
    {
        // Arrange
        byte[] garbage = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

        // Act
        var result = AAPProtocol.ParsePacket(garbage);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePacket_Empty_ReturnsNull()
    {
        // Arrange
        byte[] empty = Array.Empty<byte>();

        // Act
        var result = AAPProtocol.ParsePacket(empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePacket_TruncatedHeader_ReturnsNull()
    {
        // Arrange - only 2 bytes (marker), not enough for full header
        byte[] truncated = new byte[] { 0x04, 0x00 };

        // Act
        var result = AAPProtocol.ParsePacket(truncated);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePacket_Null_ReturnsNull()
    {
        // Arrange & Act
        var result = AAPProtocol.ParsePacket(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void IsValidPacket_Valid_ReturnsTrue()
    {
        // Arrange
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, new byte[] { 0x01 });

        // Act & Assert
        Assert.True(AAPProtocol.IsValidPacket(packet));
    }

    [Fact]
    public void IsValidPacket_Invalid_ReturnsFalse()
    {
        // Arrange
        byte[] invalid = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        // Act & Assert
        Assert.False(AAPProtocol.IsValidPacket(invalid));
    }

    [Fact]
    public void FormatPacket_ReturnsNonEmptyString()
    {
        // Arrange
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, new byte[] { 0x01 });

        // Act
        string formatted = AAPProtocol.FormatPacket(packet);

        // Assert
        Assert.False(string.IsNullOrEmpty(formatted));
        Assert.Contains("04", formatted);
        Assert.Contains("00", formatted);
    }

    [Fact]
    public void FormatPacket_Null_ReturnsNullString()
    {
        // Act
        string formatted = AAPProtocol.FormatPacket(null!);

        // Assert
        Assert.Equal("<null>", formatted);
    }

    [Theory]
    [InlineData(AAPNoiseMode.Off)]
    [InlineData(AAPNoiseMode.NoiseCancellation)]
    [InlineData(AAPNoiseMode.Transparency)]
    [InlineData(AAPNoiseMode.Adaptive)]
    public void AllNoiseModes_ProduceValidPackets(AAPNoiseMode mode)
    {
        // Arrange
        byte[] data = new byte[] { (byte)mode, 0x00, 0x00, 0x00 };

        // Act
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, data);
        var result = AAPProtocol.ParsePacket(packet);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AAPCommandOpcode.ControlCommand, result.Value.Opcode);
        Assert.Equal(AAPIdentifier.ListeningMode, result.Value.Identifier);
        Assert.Equal((byte)mode, result.Value.Data[0]);
    }

    [Fact]
    public void BuildAcknowledgement_CreatesCorrectPacket()
    {
        // Arrange
        ushort originalOpcode = (ushort)AAPCommandOpcode.ControlCommand;
        var identifier = AAPIdentifier.ListeningMode;

        // Act
        var ack = AAPProtocol.BuildAcknowledgement(originalOpcode, identifier);
        var parsed = AAPProtocol.ParsePacket(ack);

        // Assert
        Assert.NotNull(parsed);
        Assert.Equal(AAPCommandOpcode.Acknowledgement, parsed.Value.Opcode);
        Assert.Equal(identifier, parsed.Value.Identifier);
        Assert.Equal(2, parsed.Value.Data.Length);
    }

    [Fact]
    public void GetOpcode_ValidPacket_ReturnsCorrectOpcode()
    {
        // Arrange
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, null);

        // Act
        var opcode = AAPProtocol.GetOpcode(packet);

        // Assert
        Assert.Equal(AAPCommandOpcode.ControlCommand, opcode);
    }

    [Fact]
    public void GetIdentifier_ValidPacket_ReturnsCorrectIdentifier()
    {
        // Arrange
        var packet = AAPProtocol.BuildPacket(AAPCommandOpcode.ControlCommand, AAPIdentifier.ListeningMode, null);

        // Act
        var identifier = AAPProtocol.GetIdentifier(packet);

        // Assert
        Assert.Equal(AAPIdentifier.ListeningMode, identifier);
    }
}
