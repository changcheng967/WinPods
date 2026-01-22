## Claude Code Prompt: iOS 26 Liquid Glass Redesign for WinPods

**Project:** WinPods - AirPods battery popup for Windows
**Location:** `C:\Users\chang\Downloads\airpod\src\WinPods.App`
**Tech Stack:** C# .NET 10, WinUI 3 (Windows App SDK), H.NotifyIcon

### Goal
Redesign the popup window to match iOS 26's new "Liquid Glass" design language. The popup currently has a window title bar and debug border - transform it into a modern, borderless, translucent floating card.

### iOS 26 Liquid Glass Design Principles
1. **Translucent material** - Background blurs and shows content behind it
2. **Edge refraction** - Light appears to bend at curved edges
3. **Specular highlights** - Glossy rim/border that catches light (brighter at top-left)
4. **Large corner radius** - Typically 40-50px for cards
5. **No window chrome** - Borderless, floating appearance
6. **Dynamic colors** - Adapts to light/dark mode

### Required Changes

#### 1. Make Window Borderless
- Remove title bar completely
- Remove the green debug border
- Set window background to transparent
- Disable resize, hide from taskbar
- Keep always-on-top behavior

#### 2. Apply Acrylic/Mica Backdrop
Use WinUI 3's `DesktopAcrylicBackdrop` or `MicaBackdrop` for the translucent blur effect:
```csharp
SystemBackdrop = new DesktopAcrylicBackdrop();
```

#### 3. Create Liquid Glass Card
- Main card with ~44px corner radius
- Semi-transparent background (use `AcrylicInAppFillColorDefaultBrush` or similar)
- Subtle border (1-1.5px) with gradient for rim lighting effect
- Top specular highlight using LinearGradientBrush (white at top fading to transparent)

#### 4. Update Content Styling
- Battery ring circles: Add subtle outer glow
- Status text: Ensure contrast on translucent background
- Noise Control buttons: Frosted glass pill-shaped buttons with highlight border
- Model name text: Slightly lighter weight

#### 5. Position and Animation
- Position popup in bottom-right corner, 16px from screen edges (above system tray)
- Slide-in animation from right or bottom
- Fade-out after 5-8 seconds of inactivity
- Click outside to dismiss

#### 6. Light/Dark Mode Support
- Detect system theme
- Adjust glass tint accordingly (lighter tint for dark mode, darker for light mode)
- Ensure text remains readable in both modes

### Reference: Current Structure
- `PopupWindow.xaml` - XAML layout
- `PopupWindow.xaml.cs` - Code-behind with ShowBattery() method
- Currently shows: AirPods image, model name, 3 battery rings (Left/Right/Case), connection status, noise control buttons

### Visual Reference
The popup should look like a floating translucent card similar to:
- iOS 26 AirPods popup
- iOS 26 Control Center widgets
- macOS Tahoe notification panels

### Don't Break
- Existing battery data binding
- Connection status updates
- Noise control button functionality (even though disabled)
- System tray integration
- Instant popup timing (~100ms)

### Test After Changes
1. Open AirPods case → popup should appear instantly as borderless glass card
2. Popup positioned bottom-right above taskbar
3. Background should blur/show desktop behind it
4. Auto-dismiss after a few seconds
5. Click outside dismisses popup
6. Works in both light and dark Windows themes