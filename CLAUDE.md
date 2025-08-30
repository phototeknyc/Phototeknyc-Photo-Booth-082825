# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.


## Project Directory
**Full Path**: `C:\Users\rakes\OneDrive\Desktop\photeteknycbooth_clone`
**WSL Path**: `/mnt/c/Users/rakes/OneDrive/Desktop/photeteknycbooth_clone`

## Tool Notes
**Read/Edit Tool Path Issues**: 
- The Read and Edit tools fail with absolute WSL paths (`/mnt/c/...`)
- **Solution**: Always use RELATIVE paths from the project root for Read/Edit tools
- **Alternative**: Use bash commands (`cat`, `grep`, `sed`) with absolute paths
- **Working Directory**: Already set to `/mnt/c/Users/rakes/OneDrive/Desktop/photeteknycbooth_clone`
- **Search Tools Work Fine**: Glob and Grep tools work correctly with both relative and absolute paths
## Project Overview
This is a WPF photobooth application written in C# that provides a visual designer canvas for creating photo layouts with camera integration and advanced printing capabilities. The solution is built on .NET Framework 4.8 and includes extensive touch support for touchscreen kiosks.

## Build Commands
- **Build entire solution**: `msbuild Photobooth.sln`
- **Build specific project**: `msbuild Photobooth.csproj`
- **Build for Release**: `msbuild Photobooth.sln /p:Configuration=Release`
- **Clean solution**: `msbuild Photobooth.sln /t:Clean`
- **Build and restore NuGet packages**: `nuget restore && msbuild Photobooth.sln`

## Key Architecture

### Solution Projects
- **Photobooth.csproj** - Main WPF application (.NET Framework 4.8)
- **CameraControl.Devices** - Camera control and device management library
- **DesignerCanvas** - Custom visual designer canvas control with touch support
- **Canon.Eos.Framework** - Canon camera SDK integration
- **PortableDeviceLib** - Windows Portable Device API wrapper for MTP/PTP communication

### Camera System Architecture
The camera system uses a provider-based architecture:
1. **CameraDeviceManager** (`CameraControl.Devices/CameraDeviceManager.cs`) - Central manager that discovers and manages all camera devices
2. **Provider Classes** - Device discovery and instantiation (e.g., `SonyUSBProvider`, `CanonBase`, `GoProProvider`)
3. **Device Classes** - Specific camera implementations inheriting from `BaseCameraDevice`
4. **Transfer Protocols** - MTP, PTP/IP, and DD Server protocols for device communication
5. **Live View Handling** - `LiveViewData` class manages streaming preview data

### Canvas Designer System
The designer system provides drag-and-drop layout creation:
- **DesignerCanvas** (`DesignerCanvas/Controls/DesignerCanvas.cs`) - Main canvas control
- **TouchEnabledCanvas** (`DesignerCanvas/Controls/TouchEnabledCanvas.cs`) - Touch gesture support with pinch/zoom/rotate
- **Canvas Items** - `CanvasItem` base class with specialized items (ImageCanvasItem, PlaceholderCanvasItem, TextCanvasItem, ShapeCanvasItem)
- **Adorners** - Visual handles for resize, rotate, and move operations
- **CanvasImageExporter** - High-resolution export functionality (configurable DPI)

### Touch Interface Support
- Multi-touch gestures: pinch-to-zoom, rotate, pan, drag
- Touch-friendly UI controls with minimum 44x44px targets
- Touch-optimized styles in `Styles/TouchFriendlyStyle.xaml`
- Touch mode pages: `PhotoboothTouch.xaml` and `PhotoboothTouchModern.xaml`

### Printing System
Advanced printing with DNP printer support:
- Printer profile management with full DEVMODE capture
- DNP-specific settings (2" cut for 2x6 prints, color correction, overcoat)
- Print pooling for multiple printer management
- Custom print layouts and templates

## Development Patterns

### Adding New Camera Support
1. Create device class in `CameraControl.Devices/[Brand]/` inheriting from `BaseCameraDevice`
2. Implement required abstract methods: `Connect()`, `Disconnect()`, `CapturePhoto()`, `GetLiveViewImage()`
3. Create provider class implementing device discovery
4. Register provider in `CameraDeviceManager.ConnectToCamera()`

### Working with Canvas Items
```csharp
// Add item to canvas
var item = new ImageCanvasItem { Width = 200, Height = 150 };
dcvs_container.dcvs.Items.Add(item);

// Lock aspect ratio
item.IsAspectRatioLocked = true;

// Export canvas
CanvasImageExporter.Export(canvas, outputPath, dpi: 300);
```

### Implementing Touch Features
- Inherit from `TouchEnabledCanvas` for automatic gesture support
- Use `TouchDragThumb` for touch-friendly manipulation handles
- Apply touch styles from `TouchFriendlyStyle.xaml` to controls

## External Dependencies & SDK Files

### Required DLLs in `refs/` directory:
- **AForge.dll**, **AForge.Imaging.dll** - Image processing
- **Interop.WIA.dll** - Windows Image Acquisition
- **Interop.PortableDeviceApiLib.dll** - MTP/PTP support

### Camera SDK Files (must be in output directory):
- Canon: `EDSDK.dll`, `EdsImage.dll`
- Sony: `SonySDKHelper.dll`
- Place SDK files in `bin/Debug/` or `bin/Release/` after building

### NuGet Packages
Key packages referenced in `.csproj` files:
- `Magick.NET-Q16-AnyCPU` - Advanced image processing
- `Newtonsoft.Json` - JSON serialization
- `System.Data.SQLite` - Local database support
- `PixiEditor.ColorPicker` - Color selection UI

## Important Implementation Notes
- The application uses WPF with .NET Framework 4.8 (not .NET Core/5+)
- Camera functionality requires proper driver installation and SDK files
- Touch features require Windows 10/11 with touch support enabled
- Print profiles are saved as binary DEVMODE structures for complete driver state preservation
- Canvas export default is 300 DPI for print quality
- Live view requires continuous polling on most camera models

# Clean Architecture & Refactoring Guidelines

## 🏗️ CRITICAL: Clean Service-Oriented Architecture Pattern

**ALL PAGES** (especially PhotoboothTouchModernRefactored and similar UI pages) must follow a **CLEAN SERVICE-ORIENTED ARCHITECTURE** to maintain code quality and prevent technical debt.

## 🎯 Architecture Principles

### Pages (UI Layer) - ROUTING ONLY
Pages should **ONLY** handle:
- UI event routing (button clicks, touch events)
- Service method calls (delegation)
- Service event subscriptions
- Simple UI property updates
- Dispatcher invocations for UI thread operations

### Services (Business Logic Layer)
Services should handle:
- All business logic
- State management
- Timer management
- File operations
- Database operations
- Complex calculations
- Session management
- External API calls

## ❌ NEVER Add to Pages

**These belong in services, NOT in pages:**
- Business logic (photo processing, session management, etc.)
- Timer management (use service timers instead)
- File operations (copying, saving, deleting)
- Database operations (beyond simple service calls)
- Complex image processing
- Direct camera operations
- State management beyond UI state
- Complex conditionals or loops (>10 lines)

## ✅ Service Architecture

### Services Directory Structure
All services are located in the `/Services` directory. When adding new functionality:
1. Check if an existing service can handle it
2. If not, create a new service in `/Services`
3. Follow the single responsibility principle
4. Ensure the service is stateless or manages its own state properly

### Core Services and Their Responsibilities:

- **PhotoboothSessionService**: 
  - Complete session lifecycle management
  - Auto-clear timer functionality
  - Session state tracking
  - GIF generation coordination

- **PhotoboothWorkflowService**: 
  - Camera operations
  - Countdown management
  - Photo capture orchestration
  - Live view coordination

- **PhotoboothUIService**: 
  - All UI updates and notifications
  - UI element visibility management
  - Status message updates
  - Control state management

- **PhotoCaptureService**: 
  - Photo capture operations
  - File management
  - Photo storage

- **PhotoProcessingOperations**: 
  - Image processing
  - Template composition
  - Filter applications

- **PrintingService**: 
  - Print queue management
  - Printer communication

- **SharingOperations**: 
  - Cloud uploads
  - QR code generation
  - Gallery management

## 🏗️ Refactoring Rules

### When to Refactor
1. **Method Length Rule**: If a method in a page exceeds 15 lines
2. **Logic Complexity Rule**: If a method contains business logic
3. **Timer Rule**: If a page manages timers directly
4. **State Rule**: If a page manages non-UI state

### How to Refactor
1. **Identify the concern** - What is this code doing?
2. **Find or create appropriate service** - Which service should handle this?
3. **Move logic to service** - Extract method to service
4. **Create events if needed** - Service should fire events for UI updates
5. **Page subscribes to events** - Page updates UI based on service events
6. **Page calls service methods** - Page becomes a thin routing layer

## 📝 Adding New Features

### Correct Process:
1. **Determine if it's UI or Business Logic**
2. **For Business Logic:**
   - Add to existing service OR create new service
   - Service handles all logic and state
   - Service fires events for state changes
3. **For UI Updates:**
   - Page subscribes to service events
   - Page updates UI in event handlers
   - Keep handlers under 10 lines
4. **Never add business logic to pages**

### Example Pattern:
```csharp
// ❌ WRONG - Business logic in page
private void StartButton_Click(object sender, EventArgs e)
{
    if (Properties.Settings.Default.RequireEventSelection && _currentEvent == null)
    {
        ShowEventSelection();
        return;
    }
    
    var sessionId = Guid.NewGuid().ToString();
    _database.CreateSession(sessionId);
    _captureService.StartCapture();
    _timer.Start();
    // ... more logic
}

// ✅ CORRECT - Page routes to service
private void StartButton_Click(object sender, EventArgs e)
{
    _sessionService.StartNewSession();
}
```

## 🔄 Event-Driven Architecture

### Service Events Pattern:
1. Services expose events for state changes
2. Pages subscribe to relevant events
3. Pages update UI in event handlers
4. Event handlers use Dispatcher for thread safety

### Example:
```csharp
// In Service
public event EventHandler<SessionCompletedEventArgs> SessionCompleted;

// In Page
_sessionService.SessionCompleted += OnSessionCompleted;

private void OnSessionCompleted(object sender, SessionCompletedEventArgs e)
{
    Dispatcher.Invoke(() => {
        _uiService.ShowCompletionControls();
    });
}
```

## 🚨 Code Review Checklist

Before accepting changes, verify:
- [ ] Pages contain NO business logic
- [ ] All timers are managed by services
- [ ] Page methods are under 15 lines
- [ ] Complex operations are in services
- [ ] Services handle their own state
- [ ] Pages only route and update UI
- [ ] Event subscriptions are properly managed
- [ ] No direct file/database operations in pages

## 💡 Benefits of This Architecture

1. **Maintainability**: Easy to understand and modify
2. **Testability**: Services can be unit tested
3. **Reusability**: Services can be used by multiple pages
4. **Separation of Concerns**: Clear boundaries
5. **Scalability**: Easy to add new features
6. **Debugging**: Issues are isolated to specific layers

## 🔧 Maintenance Notes

- Regularly review pages for logic creep
- Refactor immediately when patterns are violated
- Document service responsibilities
- Keep services focused on single responsibility
- Use dependency injection where appropriate
- Follow the business logic when fixing issues too
---

# PhotoboothTouchModernRefactored Clean Architecture Guidelines

This page follows the architecture patterns above and serves as the reference implementation for clean architecture in this application.
