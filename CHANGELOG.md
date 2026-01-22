# Changelog

All notable changes to WinPods will be documented in this file.

## [1.1.0] - 2026-01-22

### Added
- iOS 26 Liquid Glass popup design with translucent blur effect
- Auto-connect feature - automatically connects AirPods audio when opening case near PC
- Real-time battery monitoring for left/right AirPods and charging case
- System tray integration with battery tooltip
- Media controls (Play/Pause) with global hotkey support (Ctrl+Alt+P)
- Low battery alerts via toast notifications
- Modern WinUI 3 settings window
- Bluetooth LE scanning for automatic AirPods detection

### UI Features
- Borderless floating popup with DesktopAcrylic backdrop
- Liquid glass card with 44px corner radius
- Specular highlight gradient on glass rim
- Battery ring glow effects
- Bottom-right positioning
- Smooth slide-up animations (400ms)
- Connection status indicator (Connecting/Connected/Disconnected)

### Technical
- Three-tier auto-connect system
- Race condition prevention for BLE state changes
- Synchronous connection state management
- Detailed debug logging for troubleshooting

### Known Limitations
- Noise Control (ANC/Transparency) requires kernel-level driver access
- Auto-pause on ear removal not possible due to Windows L2CAP restrictions
