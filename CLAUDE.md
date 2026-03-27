# VMS-Solution — Claude Code Guidelines

## Project Overview

Vision Management System (VMS) — .NET 8.0 WPF industrial vision inspection platform.
Uses OpenCvSharp4, HelixToolkit.Wpf.SharpDX, CommunityToolkit.Mvvm.

```
VMS-Solution/
├── VMS/                  # Main launcher application
├── VMS.AppSetup/         # System configuration wizard
├── VMS.VisionSetup/      # Vision tool workspace (primary development target)
├── NativeVision/         # C++ DLL (AVX2/FMA accelerated vision)
└── VMS.sln
```

### Build & Run

```bash
dotnet build VMS.sln
dotnet run --project VMS
```

---

## MVVM Development Guidelines

**These rules MUST be followed when generating or modifying code in this solution.**

### 1. MVVM Pattern Architecture Principles

**View (XAML & Code-behind):**
- Responsible for UI layout and animation only.
- Use `Command` binding instead of Click events. Delegate all logic to ViewModel.
- Code-behind (`.xaml.cs`) is limited to direct UI control interactions only (e.g., wiring `ImageCanvas` events).
- NEVER add business logic to `.xaml.cs` files.

**ViewModel:**
- Implement `INotifyPropertyChanged` via `ObservableObject` (CommunityToolkit.Mvvm) for data binding with View.
- Runs business logic and calls the service layer.
- MUST NOT include direct references to UI controls (e.g., `Button`, `Grid`, `TextBox`).
- All UI actions handled via `[RelayCommand]`.

**Model & Service:**
- Define data structures (`CameraInfo`, `ROIShape`, `VisionToolBase`) and pure logic (`CameraService`, `VisionService`).
- Services are stateless or singleton; models are plain data objects inheriting `ObservableObject` where binding is needed.

### 2. Camera Integration & Image Acquisition Guidelines

**CameraInfo Model:**
- Must include: `Manufacturer`, `IPAddress`, `IsConnected` properties.
- Bind appropriate icon (Path Data) to UI according to manufacturer type.

**Grab Service:**
- Invoke `GrabAsync()` through `ICameraService` interface.
- Return a common result object (e.g., `AcquisitionResult`) that can contain both 2D (`Mat`) and 3D (`PointCloudData`).

**UI Control for Camera:**
- Grab button on `MainView` binds to `GrabCommand` on `MainViewModel`.
- ViewModel property changes drive automatic `TabControl.SelectedIndex` switching based on data type (2D vs 3D).

### 3. Mandatory Code Generation Checklist

When generating or modifying code, Claude MUST:

1. **Strictly adhere to the MVVM pattern** of the current VMS project.
2. **All UI actions** are handled by `[RelayCommand]` — never use event handlers for logic.
3. **No business logic** in `MainView.xaml.cs` or any code-behind file.
4. **Camera connection status and details** (manufacturer, IP) are managed by `CameraInfo` model.
5. **Use `[ObservableProperty]`** attribute for bindable properties, not manual `OnPropertyChanged`.
6. **Use `[RelayCommand]`** attribute for commands, not manual `ICommand` implementations.
7. **Cross-ViewModel communication** uses `WeakReferenceMessenger` — never direct VM references.
8. **Service access** via interfaces (e.g., `ICameraService`, `ICameraAcquisition`) — never concrete classes in ViewModels.
9. **New vision tools** must extend `VisionToolBase` and implement `Execute(Mat) → VisionResult`.
10. **Namespace conventions** must match folder structure (e.g., `VMS.VisionSetup.VisionTools.ImageProcessing`).

### 4. Coding Conventions

| Rule | Convention |
|------|-----------|
| Private fields | `_camelCase` |
| Properties | `PascalCase` |
| Observable props | `[ObservableProperty]` on `_camelCase` field |
| Commands | `[RelayCommand]` on `PascalCase` method |
| Tool classes | `{Name}Tool.cs` in category folder under `VisionTools/` |
| Settings VMs | `{Name}ToolSettingsViewModel.cs` extending `ToolSettingsViewModelBase` |
| Namespaces | Match folder path exactly |
| Nullable | Enabled — handle nullability explicitly |
| Unsafe blocks | Allowed in VisionSetup only (native interop) |

### 5. Service Interfaces & Dependency Injection

All projects use constructor injection for services. ViewModels never access concrete service singletons directly.

**VMS.AppSetup:**
- `IConfigurationService` — app configuration persistence
- `IDialogService` — MessageBox, file dialogs

**VMS (Main App):**
- `IConfigurationService` — app configuration persistence
- `IRecipeService` — recipe CRUD operations
- `IDialogService` — MessageBox, file dialogs, save/open dialogs
- `IProcessService` — external process launching

**VMS.VisionSetup:**
- `IVisionService` — vision tool execution pipeline
- `IRecipeService` — recipe CRUD, step/tool management
- `ICameraService` — camera discovery, connection, acquisition
- `IDialogService` — MessageBox, file dialogs, camera manager dialog, recipe manager dialog, rename dialog

Service registration is done in each project's `App.xaml.cs` via manual construction (no DI container).
`MessageBox.Show`, `OpenFileDialog`, `SaveFileDialog` must only appear in `DialogService` implementations.

### 6. Key Design Patterns in Use

- **Dependency Injection:** Constructor injection via interfaces in all ViewModels
- **Singleton:** `VisionService.Instance`, `RecipeService.Instance`, `CameraService.Instance` (accessed only in `App.xaml.cs` for DI wiring)
- **Factory:** `CameraAcquisitionFactory`
- **Strategy:** `ICameraAcquisition` implementations
- **Observer:** `ObservableObject` property notifications
- **Messenger:** `WeakReferenceMessenger` for decoupled communication (ROI draw modes, tool ROI requests)
- **Template Method:** `ToolSettingsViewModelBase` for tool settings UIs

### 7. Key Libraries

| Package | Version | Purpose |
|---------|---------|---------|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM infrastructure |
| OpenCvSharp4 | 4.11.0 | Image processing |
| HelixToolkit.Wpf.SharpDX | 3.1.2 | 3D point cloud visualization |
