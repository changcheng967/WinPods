using WinPods.Core.AAP;

namespace WinPods.Tests.AAP;

public class MockDriverBridgeTests
{
    [Fact]
    public void Open_Succeeds()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();

        // Act
        bool result = bridge.Open();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Open_NotInstalled_Fails()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverNotInstalled();

        // Act
        bool result = bridge.Open();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Connect_Succeeds()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Open();

        // Act
        bool result = bridge.Connect(0x001122334455);

        // Assert
        Assert.True(result);
        Assert.True(bridge.IsConnected);
        Assert.True(bridge.ConnectedAddress.HasValue);
        Assert.Equal(0x001122334455UL, bridge.ConnectedAddress.Value);
    }

    [Fact]
    public void Connect_Failure_ReturnsFalse()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Open();
        bridge.SimulateConnectFailure(true);

        // Act
        bool result = bridge.Connect(0x001122334455);

        // Assert
        Assert.False(result);
        Assert.False(bridge.IsConnected);
    }

    [Fact]
    public void Connect_WithoutOpen_OpensAutomatically()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();

        // Act
        bool result = bridge.Connect(0x001122334455);

        // Assert
        Assert.True(result);
        Assert.True(bridge.IsConnected);
    }

    [Fact]
    public void Send_RecordsData()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        byte[] data = new byte[] { 0x04, 0x00, 0x03, 0x00, 0x09, 0x00, 0x0D, 0x01 };

        // Act
        bool result = bridge.Send(data);

        // Assert
        Assert.True(result);
        Assert.Single(bridge.SentData);
        Assert.Equal(data, bridge.SentData[0]);
    }

    [Fact]
    public void Send_NotConnected_ReturnsFalse()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Open();
        byte[] data = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        bool result = bridge.Send(data);

        // Assert
        Assert.False(result);
        Assert.Empty(bridge.SentData);
    }

    [Fact]
    public void Send_SimulatedFailure_ReturnsFalse()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        bridge.SimulateSendFailure(true);
        byte[] data = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        bool result = bridge.Send(data);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Receive_ReturnsQueuedData()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        byte[] expectedData = new byte[] { 0x04, 0x00, 0x03, 0x00, 0x04, 0x00, 0x0D };
        bridge.QueueReceiveData(expectedData);
        byte[] buffer = new byte[1024];

        // Act
        int received = bridge.Receive(buffer);

        // Assert
        Assert.Equal(expectedData.Length, received);
        for (int i = 0; i < expectedData.Length; i++)
        {
            Assert.Equal(expectedData[i], buffer[i]);
        }
    }

    [Fact]
    public void Receive_EmptyQueue_ReturnsNegative()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        byte[] buffer = new byte[1024];

        // Act
        int received = bridge.Receive(buffer);

        // Assert
        Assert.Equal(-1, received);
    }

    [Fact]
    public void Receive_NotConnected_ReturnsNegative()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Open();
        bridge.QueueReceiveData(new byte[] { 0x01, 0x02 });
        byte[] buffer = new byte[1024];

        // Act
        int received = bridge.Receive(buffer);

        // Assert
        Assert.Equal(-1, received);
    }

    [Fact]
    public void Receive_SimulatedFailure_ReturnsNegative()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        bridge.QueueReceiveData(new byte[] { 0x01, 0x02 });
        bridge.SimulateReceiveFailure(true);
        byte[] buffer = new byte[1024];

        // Act
        int received = bridge.Receive(buffer);

        // Assert
        Assert.Equal(-1, received);
    }

    [Fact]
    public void Disconnect_ClearsState()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        bridge.Send(new byte[] { 0x01 });

        // Act
        bridge.Disconnect();

        // Assert
        Assert.False(bridge.IsConnected);
        Assert.False(bridge.ConnectedAddress.HasValue);
    }

    [Fact]
    public void Disconnect_WhenNotConnected_DoesNotThrow()
    {
        // Arrange
        using var bridge = new MockDriverBridge();

        // Act & Assert (no exception)
        bridge.Disconnect();
    }

    [Fact]
    public void Dispose_NoException()
    {
        // Arrange
        var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        bridge.Send(new byte[] { 0x01 });
        bridge.QueueReceiveData(new byte[] { 0x02 });

        // Act
        bridge.Dispose();

        // Assert - can call dispose again without exception
        bridge.Dispose();
    }

    [Fact]
    public void Dispose_ThenUse_ThrowsObjectDisposedException()
    {
        // Arrange
        var bridge = new MockDriverBridge();
        bridge.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => bridge.Open());
    }

    [Fact]
    public void QueueAAPResponse_CreatesValidPacket()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        bridge.QueueAAPResponse(AAPCommandOpcode.Acknowledgement, AAPIdentifier.ListeningMode, new byte[] { 0x00 });
        byte[] buffer = new byte[1024];

        // Act
        int received = bridge.Receive(buffer);
        var packetData = new byte[received];
        Array.Copy(buffer, packetData, received);
        var parsed = AAPProtocol.ParsePacket(packetData);

        // Assert
        Assert.NotNull(parsed);
        Assert.Equal(AAPCommandOpcode.Acknowledgement, parsed.Value.Opcode);
        Assert.Equal(AAPIdentifier.ListeningMode, parsed.Value.Identifier);
    }

    [Fact]
    public void GetConnectionState_ReturnsCorrectState()
    {
        // Arrange
        using var bridge = new MockDriverBridge();

        // Initially not installed
        bridge.SimulateDriverNotInstalled();
        Assert.Equal(AAPConnectionState.DriverNotInstalled, bridge.GetConnectionState());

        // After installing
        bridge.SimulateDriverInstalled();
        Assert.Equal(AAPConnectionState.DriverInstalled, bridge.GetConnectionState());

        // After opening
        bridge.Open();
        Assert.Equal(AAPConnectionState.Disconnected, bridge.GetConnectionState());

        // After connecting
        bridge.Connect(0x001122334455);
        Assert.Equal(AAPConnectionState.Connected, bridge.GetConnectionState());

        // After disconnecting
        bridge.Disconnect();
        Assert.Equal(AAPConnectionState.Disconnected, bridge.GetConnectionState());
    }

    [Fact]
    public async Task SendAsync_Works()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        byte[] data = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        bool result = await bridge.SendAsync(data);

        // Assert
        Assert.True(result);
        Assert.Single(bridge.SentData);
    }

    [Fact]
    public async Task ReceiveAsync_Works()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        byte[] expectedData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        bridge.QueueReceiveData(expectedData);

        // Act
        var (bytesReceived, data) = await bridge.ReceiveAsync();

        // Assert
        Assert.Equal(expectedData.Length, bytesReceived);
        Assert.Equal(expectedData, data);
    }

    [Fact]
    public void ClearSentData_ClearsHistory()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.SimulateDriverInstalled();
        bridge.Connect(0x001122334455);
        bridge.Send(new byte[] { 0x01 });

        // Act
        bridge.ClearSentData();

        // Assert
        Assert.Empty(bridge.SentData);
    }

    [Fact]
    public void ClearReceiveQueue_ClearsQueue()
    {
        // Arrange
        using var bridge = new MockDriverBridge();
        bridge.QueueReceiveData(new byte[] { 0x01 });
        bridge.QueueReceiveData(new byte[] { 0x02 });
        Assert.Equal(2, bridge.PendingReceiveCount);

        // Act
        bridge.ClearReceiveQueue();

        // Assert
        Assert.Equal(0, bridge.PendingReceiveCount);
    }

    [Fact]
    public void IsDriverInstalled_ReflectsSimulatedStatus()
    {
        // Arrange
        using var bridge = new MockDriverBridge();

        // Act - default
        Assert.True(bridge.IsDriverInstalled);

        // Act - not installed
        bridge.SimulateDriverNotInstalled();
        Assert.False(bridge.IsDriverInstalled);

        // Act - installed
        bridge.SimulateDriverInstalled();
        Assert.True(bridge.IsDriverInstalled);
    }
}
