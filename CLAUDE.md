# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview
This is a WPF photobooth application written in C# that provides a visual designer canvas for creating photo layouts with camera integration. The project consists of multiple sub-projects including camera control libraries, a custom designer canvas, and portable device libraries for various camera brands.

## Build Commands
- **Build entire solution**: `msbuild Photobooth.sln` (from root directory)
- **Build specific project**: `msbuild Photobooth.csproj` (for main project)
- **Build for Release**: `msbuild Photobooth.sln /p:Configuration=Release`
- **Clean solution**: `msbuild Photobooth.sln /t:Clean`

## Project Structure

### Main Projects
- **Photobooth.csproj** - Main WPF application (.NET Framework 4.8)
- **PhotoboothWPF** - Newer .NET 7 version of the main application
- **CameraControl.Devices** - Camera control and device management library
- **DesignerCanvas** - Custom visual designer canvas control
- **Canon.Eos.Framework** - Canon camera SDK integration
- **PortableDeviceLib** - Portable device communication library

### Key Architecture Components

#### Visual Designer System
- **DesignerCanvas** - Core canvas control for drag-and-drop layout editing
- **CanvasItems** - Various canvas items (ImageCanvasItem, PlaceholderCanvasItem, etc.)
- **Adorners** - Visual editing handles for resize, rotate, and move operations
- **CanvasImageExporter** - Handles exporting canvas layouts to image files

#### Camera Integration
- **CameraDeviceManager** - Central manager for all camera devices
- **BaseCameraDevice** - Base class for camera implementations
- **Brand-specific implementations** - Canon, Nikon, Sony, GoPro, etc.
- **Transfer Protocols** - MTP, PTP/IP, DD Server protocols for device communication

#### Main UI Pages
- **MainPage.xaml/.cs** - Primary interface with canvas and tools
- **Camera.xaml/.cs** - Camera control and live view interface  
- **Setting.xaml/.cs** - Application settings and configuration
- **Properties.xaml/.cs** - Item properties panel
- **Sidebar.xaml/.cs** - Tool palette and navigation

## Development Workflow

### Working with Canvas Items
- Canvas items inherit from `CanvasItem` base class
- Items support properties: position, size, rotation, aspect ratio locking
- Use `dcvs_container.dcvs.Items.Add(item)` to add items to canvas
- Items can be locked for position, size, or aspect ratio

### Camera Device Development
- All camera devices inherit from `BaseCameraDevice` or `ICameraDevice`
- Device discovery is handled by provider classes (e.g., `SonyProvider`, `CanonBase`)
- Live view data is handled through `LiveViewData` class
- Camera events use `PhotoCapturedEventArgs` for photo capture notifications

### Adding New Camera Support
1. Create device class inheriting from `BaseCameraDevice`
2. Implement required abstract methods (Connect, Disconnect, etc.)
3. Add provider class for device discovery
4. Register provider in `CameraDeviceManager`

### UI Theme and Styling
- Custom styles defined in `Styles/` directory (ButtonStyle.xaml, etc.)
- Images and icons stored in `images/` directory
- Generic themes in `Themes/Generic.xaml`

## Key Dependencies
- **WPF** - Windows Presentation Foundation UI framework
- **Canon EDSDK** - Canon camera SDK (EDSDK.dll, EdsImage.dll)
- **WIA (Windows Image Acquisition)** - For scanner/camera support
- **PortableDevice APIs** - For MTP device communication
- **AForge** - Computer vision and imaging library (referenced in PhotoboothWPF)

## Camera SDK Files
- Canon SDK files: `EDSDK.dll`, `EdsImage.dll` (must be in output directory)
- Various camera-specific DLLs in `refs/` directory
- Ensure SDK files are copied to build output for camera functionality

## Testing
No specific test framework detected. Manual testing through the application interface is the current approach.

## Important Notes
- The project has dual implementations (Framework 4.8 and .NET 7 versions)
- Camera functionality requires proper SDK files and driver installation
- Canvas export resolution is configurable (default 300 DPI for print quality)
- Print functionality supports 4x6 photo booth standard dimensions