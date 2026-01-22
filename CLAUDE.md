# CLAUDE.md - AirPods Experience for Windows

> **Project Codename**: WinPods  
> **Goal**: Bring the native Apple/iOS AirPods experience to Windows with beautiful UI, seamless functionality, and open-source transparency.

---

## 🎯 Project Vision

Create a Windows application that replicates the magical AirPods experience from iOS/macOS:
- Instant popup when opening AirPods case showing battery levels
- Beautiful animations matching Apple's design language
- Automatic ear detection with play/pause
- System tray integration with battery monitoring
- Noise control mode switching

---

## 📋 Feature Specification

### Phase 1: Core Features (MVP)

| Feature | Priority | Complexity | Description |
|---------|----------|------------|-------------|
| BLE Scanner | P0 | High | Scan for AirPods via BLE advertisements |
| Protocol Parser | P0 | High | Decode Apple Proximity Pairing messages |
| Battery Display | P0 | Medium | Show L/R/Case battery in system tray |
| Popup Animation | P0 | Medium | iOS-style slide-up card on case open |
| System Tray | P0 | Low | Background app with tray icon |

### Phase 2: Enhanced Features

| Feature | Priority | Complexity | Description |
|---------|----------|------------|-------------|
| Ear Detection | P1 | Medium | Pause/resume media on earbud removal |
| Audio Switching | P1 | High | Route audio to speakers when AirPods removed |
| Low Battery Alert | P1 | Low | Toast notification at configurable threshold |
| Auto-Connect | P2 | Medium | Connect AirPods when case opened |
| Noise Control | P2 | High | Switch ANC/Transparency/Off modes |

### Phase 3: Advanced Features

| Feature | Priority | Complexity | Description |
|---------|----------|------------|-------------|
| Spatial Audio | P3 | Very High | Head tracking simulation |
| Multi-device | P3 | Medium | Support multiple AirPods pairs |
| Widgets | P3 | Medium | Windows 11 widget support |
| Hotkeys | P3 | Low | Global shortcuts for controls |

---

## 🔧 Technical Architecture

### Recommended Stack

```
┌─────────────────────────────────────────────────────────────┐
│                        UI Layer                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │   WinUI 3       │  │  H.NotifyIcon   │  │   Windows    │ │
│  │   Popup Window  │  │  System Tray    │  │   Widgets    │ │
│  └────────┬────────┘  └────────┬────────┘  └──────┬───────┘ │
├───────────┼────────────────────┼──────────────────┼─────────┤
│           │         Service Layer                 │         │
│  ┌────────┴────────────────────┴──────────────────┴───────┐ │
│  │              DeviceManager (Singleton)                  │ │
│  │  - Track connected AirPods                              │ │
│  │  - Emit state change events                             │ │
│  │  - Handle reconnection logic                            │ │
│  └────────┬────────────────────┬──────────────────────────┘ │
├───────────┼────────────────────┼────────────────────────────┤
│           │         Core Layer                   │          │
│  ┌────────┴────────┐  ┌────────┴────────┐  ┌────┴────────┐ │
│  │   BleScanner    │  │  ProtocolParser │  │AudioManager │ │
│  │   (WinRT BLE)   │  │  (Apple Proto)  │  │  (WASAPI)   │ │
│  └─────────────────┘  └─────────────────┘  └─────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Technology Choices

| Component | Technology | Rationale |
|-----------|------------|-----------|
| Language | C# (.NET 8+) | Fast development, great WinRT interop |
| UI Framework | WinUI 3 | Modern Windows 11 look, Fluent Design |
| System Tray | H.NotifyIcon.WinUI | Best WinUI 3 tray icon library |
| BLE | Windows.Devices.Bluetooth.Advertisement | Native WinRT API |
| Audio | NAudio + Windows Audio Session API | Robust audio endpoint control |
| Animations | WinUI Composition API | Smooth 60fps animations |
| Packaging | MSIX | Microsoft Store distribution |

### Alternative: Native C++ Stack

```
Language: C++20
UI: Win32 + Direct2D (or Qt 6)
BLE: WinRT/C++ or WinBLE
Audio: WASAPI direct
Build: CMake + vcpkg
```

---

## 📡 Apple BLE Protocol Specification

### Overview

AirPods broadcast **Proximity Pairing** messages via BLE advertisements. These contain unencrypted device info, battery levels, and state.

### BLE Advertisement Structure

```
┌──────────────────────────────────────────────────────────────┐
│                    BLE Advertisement Packet                   │
├──────────────────────────────────────────────────────────────┤
│ Access Address: 0x8E89BED6 (fixed for advertising)           │
│ PDU Header: ADV_NONCONN_IND or ADV_IND                       │
│ Advertiser Address: XX:XX:XX:XX:XX:XX (randomized)           │
├──────────────────────────────────────────────────────────────┤
│                    Manufacturer Specific Data                 │
│ ┌──────────────┬────────────┬─────────────────────────────┐ │
│ │ Length       │ Type: 0xFF │ Company ID: 0x004C (Apple)  │ │
│ ├──────────────┴────────────┴─────────────────────────────┤ │
│ │              Apple Continuity Message                    │ │
│ │ ┌────────────┬────────────┬───────────────────────────┐ │ │
│ │ │ Type: 0x07 │ Length     │ Proximity Pairing Data    │ │ │
│ │ └────────────┴────────────┴───────────────────────────┘ │ │
│ └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### Proximity Pairing Message Format (Type 0x07)

```
Byte:  0     1      2-3       4        5         6           7        8       9
     ┌─────┬──────┬────────┬────────┬─────────┬───────────┬────────┬───────┬────────┐
     │0x01 │Device│ Status │Battery │Charging │ Lid Open  │ Color  │ 0x00  │Encrypt │
     │Prefix│Model │        │ L | R  │ Status  │  Count    │        │Suffix │Payload │
     └─────┴──────┴────────┴────────┴─────────┴───────────┴────────┴───────┴────────┘
           │      │        │        │         │           │
           │      │        │        │         │           └─ Device color code
           │      │        │        │         └─ Increments when lid opened
           │      │        │        └─ Charging flags + Case battery
           │      │        └─ High nibble: Right, Low nibble: Left (×10%)
           │      └─ Position status (in ear, in case, etc.)
           └─ 2-byte device model identifier
```

### Device Model Codes (2 bytes, little-endian)

```cpp
enum class AirPodsModel : uint16_t {
    // AirPods
    AirPods1            = 0x0220,
    AirPods2            = 0x0F20,
    AirPods3            = 0x1320,
    AirPods4            = 0x1720,   // Unconfirmed
    AirPods4_ANC        = 0x1920,   // Unconfirmed
    
    // AirPods Pro
    AirPodsPro          = 0x0E20,
    AirPodsPro2         = 0x1420,
    AirPodsPro2_USBC    = 0x1520,   // USB-C variant
    
    // AirPods Max
    AirPodsMax          = 0x0A20,
    AirPodsMax2024      = 0x1620,   // Unconfirmed
    
    // Beats (same protocol)
    PowerbeatsPro       = 0x0B20,
    PowerbeatsPro2      = 0x1120,
    BeatsFitPro         = 0x1220,
    BeatsStudioBuds     = 0x1020,
    BeatsStudioBudsPlus = 0x1620,
    BeatsStudioPro      = 0x1820,
    BeatsSolo3          = 0x0620,
    BeatsSoloP          = 0x0C20,
    BeatsX              = 0x0520,
    BeatsFlex           = 0x1020,
};
```

### Status Byte Decoding

```cpp
// Byte 4: Status flags indicating position
enum class PodStatus : uint8_t {
    BothInCase         = 0x55,  // Both pods in case
    BothInEar          = 0x33,  // Both pods in ear  
    LeftInEar          = 0x31,  // Left in ear, right in case
    RightInEar         = 0x13,  // Right in ear, left in case
    BothOutOfCase      = 0x11,  // Both out, not in ear
    // ... varies by model
};

// Practical approach: check individual nibbles
bool isLeftInEar = (status & 0x0F) == 0x03 || (status & 0x0F) == 0x01;
bool isRightInEar = (status >> 4) == 0x03 || (status >> 4) == 0x01;
```

### Battery Level Decoding

```cpp
struct BatteryInfo {
    uint8_t left;       // 0-10 (multiply by 10 for percentage)
    uint8_t right;      // 0-10
    uint8_t case_;      // 0-10
    bool leftCharging;
    bool rightCharging;
    bool caseCharging;
};

BatteryInfo parseBattery(const uint8_t* data) {
    BatteryInfo info;
    
    // Byte 5: Battery levels (each nibble is 0-10, representing 0-100%)
    info.right = ((data[5] >> 4) & 0x0F) * 10;  // High nibble
    info.left  = (data[5] & 0x0F) * 10;         // Low nibble
    
    // Byte 6: Charging status (bits) + Case battery (high nibble)
    info.case_ = ((data[6] >> 4) & 0x0F) * 10;
    info.leftCharging  = (data[6] & 0x01) != 0;
    info.rightCharging = (data[6] & 0x02) != 0;
    info.caseCharging  = (data[6] & 0x04) != 0;
    
    // Handle special values
    // 0x0F (15) sometimes means "disconnected" or "unknown"
    if ((data[5] & 0x0F) == 0x0F) info.left = -1;   // Unknown
    if ((data[5] >> 4) == 0x0F) info.right = -1;
    if ((data[6] >> 4) == 0x0F) info.case_ = -1;
    
    return info;
}
```

### Lid Open Counter

```cpp
// Byte 7: Lid Open Count
// Increments each time the case lid is opened
// Used to detect "case just opened" event for popup
uint8_t lidOpenCount = data[7];

// Trigger popup when:
// 1. New AirPods detected, OR
// 2. lidOpenCount changed from previous value
```

### Device Color Codes

```cpp
enum class DeviceColor : uint8_t {
    White       = 0x00,
    Black       = 0x01,
    Red         = 0x02,
    Blue        = 0x03,
    Pink        = 0x04,
    Gray        = 0x05,
    Silver      = 0x06,
    Gold        = 0x07,
    RoseGold    = 0x08,
    SpaceGray   = 0x09,
    DarkCherry  = 0x0A,
    Green       = 0x0B,
    // ... varies by device
};
```

---

## 💻 Implementation Guide

### Step 1: BLE Scanner Setup (C#)

```csharp
using Windows.Devices.Bluetooth.Advertisement;

public class AirPodsScanner : IDisposable
{
    private const ushort AppleCompanyId = 0x004C;
    private const byte ProximityPairingType = 0x07;
    
    private BluetoothLEAdvertisementWatcher _watcher;
    
    public event EventHandler<AirPodsData>? AirPodsDetected;
    
    public void Start()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            SignalStrengthFilter = new BluetoothSignalStrengthFilter
            {
                InRangeThresholdInDBm = -70,
                OutOfRangeThresholdInDBm = -80,
                OutOfRangeTimeout = TimeSpan.FromSeconds(5)
            }
        };
        
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Start();
    }
    
    private void OnAdvertisementReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var mfgData in args.Advertisement.ManufacturerData)
        {
            if (mfgData.CompanyId != AppleCompanyId) continue;
            
            var data = new byte[mfgData.Data.Length];
            DataReader.FromBuffer(mfgData.Data).ReadBytes(data);
            
            // Look for Proximity Pairing message (type 0x07)
            int offset = 0;
            while (offset < data.Length - 2)
            {
                byte type = data[offset];
                byte length = data[offset + 1];
                
                if (type == ProximityPairingType && length >= 25)
                {
                    var airpods = ParseProximityPairing(
                        data.AsSpan(offset + 2, length),
                        args.RawSignalStrengthInDBm,
                        args.BluetoothAddress
                    );
                    AirPodsDetected?.Invoke(this, airpods);
                }
                
                offset += 2 + length;
            }
        }
    }
    
    private AirPodsData ParseProximityPairing(
        ReadOnlySpan<byte> data, short rssi, ulong address)
    {
        return new AirPodsData
        {
            Model = (AirPodsModel)BitConverter.ToUInt16(data.Slice(1, 2)),
            Status = data[3],
            LeftBattery = (data[4] & 0x0F) * 10,
            RightBattery = ((data[4] >> 4) & 0x0F) * 10,
            CaseBattery = ((data[5] >> 4) & 0x0F) * 10,
            IsLeftCharging = (data[5] & 0x01) != 0,
            IsRightCharging = (data[5] & 0x02) != 0,
            IsCaseCharging = (data[5] & 0x04) != 0,
            LidOpenCount = data[6],
            Color = (DeviceColor)data[7],
            Rssi = rssi,
            BluetoothAddress = address,
            LastSeen = DateTime.UtcNow
        };
    }
    
    public void Dispose() => _watcher?.Stop();
}
```

### Step 2: Popup Window (WinUI 3 XAML)

```xml
<!-- PopupWindow.xaml -->
<Window x:Class="WinPods.PopupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AirPods" Width="340" Height="420">
    
    <Grid Background="{ThemeResource AcrylicBackgroundFillColorDefaultBrush}"
          CornerRadius="16" Padding="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- AirPods Image -->
        <Image Grid.Row="0" 
               Source="{x:Bind ViewModel.DeviceImage}" 
               Height="180" Margin="0,20,0,20"/>
        
        <!-- Device Name -->
        <TextBlock Grid.Row="1" 
                   Text="{x:Bind ViewModel.DeviceName}"
                   Style="{StaticResource SubtitleTextBlockStyle}"
                   HorizontalAlignment="Center"/>
        
        <!-- Battery Indicators -->
        <Grid Grid.Row="2" Margin="0,24,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            
            <!-- Left Pod -->
            <StackPanel Grid.Column="0" HorizontalAlignment="Center">
                <local:BatteryIndicator 
                    Level="{x:Bind ViewModel.LeftBattery}"
                    IsCharging="{x:Bind ViewModel.IsLeftCharging}"/>
                <TextBlock Text="Left" Opacity="0.6" 
                           HorizontalAlignment="Center"/>
            </StackPanel>
            
            <!-- Right Pod -->
            <StackPanel Grid.Column="1" HorizontalAlignment="Center">
                <local:BatteryIndicator 
                    Level="{x:Bind ViewModel.RightBattery}"
                    IsCharging="{x:Bind ViewModel.IsRightCharging}"/>
                <TextBlock Text="Right" Opacity="0.6"
                           HorizontalAlignment="Center"/>
            </StackPanel>
            
            <!-- Case -->
            <StackPanel Grid.Column="2" HorizontalAlignment="Center">
                <local:BatteryIndicator 
                    Level="{x:Bind ViewModel.CaseBattery}"
                    IsCharging="{x:Bind ViewModel.IsCaseCharging}"/>
                <TextBlock Text="Case" Opacity="0.6"
                           HorizontalAlignment="Center"/>
            </StackPanel>
        </Grid>
        
        <!-- Connect Button -->
        <Button Grid.Row="3" 
                Content="Connect" 
                Style="{StaticResource AccentButtonStyle}"
                HorizontalAlignment="Stretch"
                Margin="0,24,0,0"
                Command="{x:Bind ViewModel.ConnectCommand}"/>
    </Grid>
</Window>
```

### Step 3: Slide-Up Animation

```csharp
public async Task ShowPopupWithAnimation()
{
    // Position window at bottom-center of screen
    var display = DisplayArea.Primary;
    var workArea = display.WorkArea;
    
    AppWindow.MoveAndResize(new RectInt32(
        (workArea.Width - 340) / 2,
        workArea.Height,  // Start below screen
        340,
        420
    ));
    
    Activate();
    
    // Animate slide up
    var compositor = this.Compositor;
    var visual = ElementCompositionPreview.GetElementVisual(Content);
    
    var animation = compositor.CreateVector3KeyFrameAnimation();
    animation.InsertKeyFrame(0f, new Vector3(0, 420, 0));
    animation.InsertKeyFrame(1f, new Vector3(0, 0, 0));
    animation.Duration = TimeSpan.FromMilliseconds(350);
    
    var easing = compositor.CreateCubicBezierEasingFunction(
        new Vector2(0.1f, 0.9f), 
        new Vector2(0.2f, 1f)  // iOS-like spring
    );
    animation.SetScalarParameter("Progress", 0);
    
    visual.StartAnimation("Offset", animation);
    
    // Move window up simultaneously
    for (int i = 0; i <= 420; i += 14)
    {
        AppWindow.Move(new PointInt32(
            (workArea.Width - 340) / 2,
            workArea.Height - 40 - i
        ));
        await Task.Delay(10);
    }
    
    // Auto-dismiss after 5 seconds
    await Task.Delay(5000);
    await HidePopupWithAnimation();
}
```

### Step 4: System Tray Integration

```csharp
// Using H.NotifyIcon.WinUI NuGet package
using H.NotifyIcon;

public class TrayIconManager
{
    private TaskbarIcon _trayIcon;
    
    public void Initialize(XamlRoot xamlRoot)
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(
                new Uri("ms-appx:///Assets/airpods_tray.ico")),
            ToolTipText = "WinPods - AirPods not connected"
        };
        
        // Context menu
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Connect AirPods",
            Command = ConnectCommand 
        });
        menu.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Settings",
            Command = SettingsCommand 
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Exit",
            Command = ExitCommand 
        });
        
        _trayIcon.ContextFlyout = menu;
        _trayIcon.LeftClickCommand = ShowPopupCommand;
    }
    
    public void UpdateBattery(int left, int right, int case_)
    {
        int avg = (left + right) / 2;
        _trayIcon.ToolTipText = $"AirPods - L:{left}% R:{right}% Case:{case_}%";
        _trayIcon.IconSource = GetBatteryIcon(avg);
    }
}
```

### Step 5: Audio Endpoint Switching

```csharp
using NAudio.CoreAudioApi;

public class AudioManager
{
    private MMDeviceEnumerator _enumerator;
    private string _previousDeviceId;
    
    public AudioManager()
    {
        _enumerator = new MMDeviceEnumerator();
    }
    
    public void OnAirPodsRemoved()
    {
        // Store current AirPods device ID
        var current = _enumerator.GetDefaultAudioEndpoint(
            DataFlow.Render, Role.Multimedia);
        
        if (IsAirPods(current))
        {
            _previousDeviceId = current.ID;
            
            // Switch to speakers
            var speakers = FindSpeakers();
            if (speakers != null)
            {
                SetDefaultAudioDevice(speakers.ID);
            }
        }
    }
    
    public void OnAirPodsInserted()
    {
        if (_previousDeviceId != null)
        {
            SetDefaultAudioDevice(_previousDeviceId);
        }
    }
    
    // Uses PolicyConfig COM interface (undocumented but widely used)
    private void SetDefaultAudioDevice(string deviceId)
    {
        var policyConfig = new PolicyConfigClient();
        policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
        policyConfig.SetDefaultEndpoint(deviceId, Role.Communications);
    }
}
```

---

## 📁 Project Structure

```
WinPods/
├── src/
│   ├── WinPods.Core/                    # Core library (netstandard2.1)
│   │   ├── Bluetooth/
│   │   │   ├── AirPodsScanner.cs        # BLE advertisement scanner
│   │   │   ├── ProtocolParser.cs        # Apple protocol decoder
│   │   │   ├── DeviceModels.cs          # Model enums and lookups
│   │   │   └── BatteryInfo.cs           # Battery data structures
│   │   ├── Audio/
│   │   │   ├── AudioManager.cs          # WASAPI endpoint control
│   │   │   ├── MediaController.cs       # Play/pause control
│   │   │   └── PolicyConfigClient.cs    # COM interop for default device
│   │   ├── Services/
│   │   │   ├── DeviceManager.cs         # Central device state manager
│   │   │   ├── SettingsService.cs       # User preferences
│   │   │   └── NotificationService.cs   # Toast notifications
│   │   └── Models/
│   │       ├── AirPodsData.cs           # Device state model
│   │       └── AppSettings.cs           # Settings model
│   │
│   ├── WinPods.App/                     # WinUI 3 application
│   │   ├── Views/
│   │   │   ├── PopupWindow.xaml         # Battery popup
│   │   │   ├── SettingsPage.xaml        # Settings UI
│   │   │   └── MainWindow.xaml          # Main window (hidden)
│   │   ├── ViewModels/
│   │   │   ├── PopupViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   ├── Controls/
│   │   │   ├── BatteryIndicator.xaml    # Battery bar control
│   │   │   └── AirPodsImage.xaml        # Device image control
│   │   ├── Services/
│   │   │   └── TrayIconManager.cs       # System tray
│   │   ├── Helpers/
│   │   │   ├── AnimationHelper.cs       # Composition animations
│   │   │   └── WindowHelper.cs          # Window positioning
│   │   ├── Assets/
│   │   │   ├── Images/
│   │   │   │   ├── airpods_1.png
│   │   │   │   ├── airpods_2.png
│   │   │   │   ├── airpods_3.png
│   │   │   │   ├── airpods_pro.png
│   │   │   │   ├── airpods_pro_2.png
│   │   │   │   └── airpods_max.png
│   │   │   └── Icons/
│   │   │       ├── tray_full.ico
│   │   │       ├── tray_medium.ico
│   │   │       ├── tray_low.ico
│   │   │       └── tray_empty.ico
│   │   ├── Strings/
│   │   │   ├── en-US/
│   │   │   └── Resources.resw
│   │   ├── App.xaml
│   │   └── Program.cs
│   │
│   └── WinPods.Tests/                   # Unit tests
│       ├── ProtocolParserTests.cs
│       └── BatteryParsingTests.cs
│
├── docs/
│   ├── PROTOCOL.md                      # Detailed protocol docs
│   └── CONTRIBUTING.md
│
├── .github/
│   └── workflows/
│       └── build.yml                    # CI/CD
│
├── WinPods.sln
├── README.md
├── LICENSE                              # MIT or GPL-3.0
└── CLAUDE.md                            # This file
```

---

## 🎨 UI/UX Design Guidelines

### Popup Card Design

```
╭──────────────────────────────────────╮
│                                      │
│         ╭──────────────────╮         │
│         │                  │         │
│         │   [AirPods Pro   │         │
│         │      Image]      │         │
│         │                  │         │
│         ╰──────────────────╯         │
│                                      │
│           Your AirPods Pro           │
│                                      │
│   ████████░░  ██████░░░░  ████░░░░░░ │
│     Left         Right      Case     │
│     80%           60%        40%     │
│                                      │
│   ╭──────────────────────────────╮   │
│   │          Connect             │   │
│   ╰──────────────────────────────╯   │
│                                      │
╰──────────────────────────────────────╯
```

### Color Scheme

```css
/* Light Mode */
--background: rgba(255, 255, 255, 0.85);   /* Acrylic */
--text-primary: #1D1D1F;
--text-secondary: rgba(0, 0, 0, 0.55);
--accent: #007AFF;                          /* Apple Blue */
--battery-green: #34C759;
--battery-yellow: #FFCC00;
--battery-red: #FF3B30;

/* Dark Mode */
--background: rgba(30, 30, 30, 0.85);
--text-primary: #F5F5F7;
--text-secondary: rgba(255, 255, 255, 0.55);
--accent: #0A84FF;
--battery-green: #30D158;
--battery-yellow: #FFD60A;
--battery-red: #FF453A;
```

### Animation Specs

| Animation | Duration | Easing | Notes |
|-----------|----------|--------|-------|
| Popup slide up | 350ms | cubic-bezier(0.1, 0.9, 0.2, 1) | iOS spring feel |
| Popup slide down | 250ms | ease-in | Faster dismiss |
| Battery fill | 500ms | ease-out | On data update |
| Fade in | 200ms | ease-out | Elements appearing |

---

## 🧪 Testing Strategy

### Unit Tests

```csharp
[TestClass]
public class ProtocolParserTests
{
    [TestMethod]
    public void ParseAirPodsPro_ValidData_ReturnsCorrectModel()
    {
        // Real captured data from AirPods Pro
        byte[] data = { 
            0x07, 0x19, 0x01, 0x0E, 0x20, 0x55, 0x98, 0x8F, 
            0x39, 0x00, 0x00, /* ... encrypted payload */ 
        };
        
        var result = ProtocolParser.Parse(data);
        
        Assert.AreEqual(AirPodsModel.AirPodsPro, result.Model);
        Assert.AreEqual(80, result.LeftBattery);   // 0x8 * 10
        Assert.AreEqual(90, result.RightBattery);  // 0x9 * 10
    }
    
    [TestMethod]
    public void ParseBattery_ChargingFlags_CorrectlyDetected()
    {
        byte chargingByte = 0x87;  // Case charging, case at 80%
        
        var info = BatteryInfo.ParseChargingByte(chargingByte);
        
        Assert.IsTrue(info.IsCaseCharging);
        Assert.IsFalse(info.IsLeftCharging);
        Assert.AreEqual(80, info.CaseBattery);
    }
}
```

### Integration Tests

- Test with real AirPods hardware
- Verify battery accuracy (compare with iPhone)
- Test popup timing on case open
- Test ear detection with media playback
- Test reconnection after Bluetooth toggle

### Manual Test Cases

1. **First Launch**: Install → App starts in tray → No errors
2. **Detection**: Open AirPods case near PC → Popup appears within 2s
3. **Battery Accuracy**: Compare displayed % with iPhone → Within 10%
4. **Ear Detection**: Remove earbud → Music pauses → Reinsert → Resumes
5. **Reconnect**: Disconnect Bluetooth → Reconnect → App recovers

---

## 📚 Reference Resources

### Open Source Projects to Study

| Project | Language | Key Learnings |
|---------|----------|---------------|
| [AirPodsDesktop](https://github.com/SpriteOvO/AirPodsDesktop) | C++ | BLE scanning, animation |
| [MagicPods-Windows](https://github.com/steam3d/MagicPods-Windows) | C# | Full feature set, UI |
| [MagicPodsCore](https://github.com/steam3d/MagicPodsCore) | C# | Cross-platform core |
| [OpenPods](https://github.com/adolfintel/OpenPods) | Java | Android implementation |
| [librepods](https://github.com/kavishdevar/librepods) | Kotlin | Protocol documentation |
| [furiousMAC/continuity](https://github.com/furiousMAC/continuity) | Research | Protocol specs |

### Research Papers

- [Handoff All Your Privacy](https://petsymposium.org/popets/2019/popets-2019-0057.pdf) - Apple Continuity protocol reverse engineering
- [Discontinued Privacy](https://petsymposium.org/2020/files/papers/issue1/popets-2020-0003.pdf) - Personal data leaks in Apple BLE

### Documentation

- [Windows.Devices.Bluetooth.Advertisement](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement)
- [WinUI 3 Gallery](https://github.com/microsoft/WinUI-Gallery)
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)
- [NAudio Documentation](https://github.com/naudio/NAudio/wiki)

---

## ⚙️ Development Commands

```bash
# Clone and setup
git clone https://github.com/yourusername/WinPods.git
cd WinPods

# Restore packages
dotnet restore

# Build
dotnet build -c Release

# Run tests
dotnet test

# Create MSIX package
dotnet publish -c Release -p:PublishProfile=MSIX

# Run app
dotnet run --project src/WinPods.App
```

---

## 🚀 Deployment Checklist

- [ ] Code signing certificate obtained
- [ ] MSIX package tested on clean Windows install
- [ ] Microsoft Store assets prepared (icons, screenshots)
- [ ] Privacy policy written
- [ ] Auto-update mechanism tested
- [ ] Crash reporting integrated (e.g., Sentry)
- [ ] Telemetry opt-in implemented
- [ ] README.md completed with GIFs

---

## 🤝 Contribution Guidelines

1. Fork the repository
2. Create feature branch: `git checkout -b feature/amazing-feature`
3. Follow C# coding conventions
4. Write unit tests for new code
5. Test with real AirPods hardware
6. Submit PR with detailed description

---

## 📄 License

Choose one:
- **MIT License** - Maximum adoption, commercial use OK
- **GPL-3.0** - Copyleft, derivatives must be open source