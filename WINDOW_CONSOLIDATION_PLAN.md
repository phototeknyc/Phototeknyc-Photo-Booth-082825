# Window Consolidation Plan

## Current Window Hierarchy Analysis

### Primary Windows (To Keep/Modify)
1. **SurfacePhotoBoothWindow** ✅ - MAIN WINDOW
   - Modern Surface-style interface
   - Should be the only entry point
   - All navigation should happen within this window

### Windows to Remove/Consolidate
1. **WelcomeWindow** ❌
   - Redundant - Surface window serves as welcome screen
   - Currently bypassed in App.xaml

2. **PhotoBoothWindow** ⚠️
   - Old main window with complex navigation
   - Should be converted to a Page within Surface window
   - Contains useful functionality that needs to be migrated

3. **MainWindow** (Template Designer) ⚠️
   - Currently standalone window
   - Should be converted to a Page within Surface window
   - Already has MainPage that can be used

### Dialog Windows (Keep as Modals)
- ModernSettingsWindow ✅
- Print dialogs ✅
- Color pickers ✅
- Event/Template browsers ✅

### Duplicate Directories to Remove
1. `/canvas/` - Old project structure
2. `/PhotoboothWPF/` - Old WPF version

## Implementation Steps

### Phase 1: Clean Up Old Windows
1. Remove WelcomeWindow.xaml and WelcomeWindow.xaml.cs
2. Remove old navigation code that opens separate windows
3. Clean up duplicate directories

### Phase 2: Convert PhotoBoothWindow to Page
1. Create PhotoBoothPage from PhotoBoothWindow content
2. Update Surface window navigation to use the page
3. Migrate menu functionality to Surface window

### Phase 3: Integrate Template Designer
1. Use existing MainPage within Surface window
2. Remove standalone MainWindow
3. Update navigation to stay within Surface window

### Phase 4: Update Navigation Model
1. All navigation through SurfacePhotoBoothWindow
2. Use Frame navigation for all pages
3. Implement consistent back navigation

## Benefits
- Single window application (except for dialogs)
- Consistent user experience
- Easier state management
- Better touch device support
- Cleaner codebase

## Navigation Flow
```
SurfacePhotoBoothWindow (Always Active)
├── Home Grid (Start Screen)
├── Frame (Content Area)
│   ├── MainPage (Templates)
│   ├── Camera Page
│   ├── PhotoboothTouchModern (Photo Booth)
│   ├── Settings Page
│   └── Other Pages
└── Modal Dialogs (as needed)
```