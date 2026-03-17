# Implementation Tasks: Avalonia 12 Migration

## Tasks

- [-] 1. Update project files to Avalonia 12
  - [x] 1.1 Update `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj`: change `TargetFramework` from `net9.0` to `net10.0`, bump all `Avalonia.*` core package versions to `12.x.x`, replace `Avalonia.Diagnostics` conditional reference with `AvaloniaUI.DiagnosticsSupport`, and add `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>`
  - [x] 1.2 Update `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj`: change `TargetFramework` to `net10.0`, bump `Avalonia.Desktop`, `Avalonia.ReactiveUI`, and `WebView.Avalonia` desktop variant to Avalonia 12-compatible versions

- [x] 2. Remove obsolete data validation code
  - [x] 2.1 Delete the `DisableAvaloniaDataAnnotationValidation()` method and its call site from `App.axaml.cs`
  - [x] 2.2 Remove the `using Avalonia.Data.Core.Plugins;` import from `App.axaml.cs` if it becomes unused after the deletion

- [x] 3. Replace DevTools attachment API in all six window files
  - [x] 3.1 Replace `AttachDevTools()` with `AttachDeveloperTools()` in `Views/OAuthLoginWindow.axaml.cs`
  - [x] 3.2 Replace `AttachDevTools()` with `AttachDeveloperTools()` in `Views/ModalDialog.axaml.cs`
  - [x] 3.3 Replace `AttachDevTools()` with `AttachDeveloperTools()` in `Views/AuthWindow.axaml.cs`
  - [x] 3.4 Replace `AttachDevTools()` with `AttachDeveloperTools()` in `Views/LoginMethodSelectorWindow.axaml.cs`
  - [x] 3.5 Replace `AttachDevTools()` with `AttachDeveloperTools()` in `Views/HelpWindow.axaml.cs`
  - [x] 3.6 Replace `AttachDevTools()` with `AttachDeveloperTools()` in `Views/BackGroundWindow.axaml.cs`

- [x] 4. Replace ExtendClientAreaChromeHints with WindowDecorations
  - [x] 4.1 In `Views/MainWindow.axaml`, replace `ExtendClientAreaChromeHints="NoChrome"` with `WindowDecorations="None"` and remove `ExtendClientAreaToDecorationsHint` and `ExtendClientAreaTitleBarHeightHint` attributes
  - [x] 4.2 In `Views/OAuthLoginWindow.axaml`, replace `ExtendClientAreaChromeHints="NoChrome"` with `WindowDecorations="None"` and remove related extend-area attributes
  - [x] 4.3 In `Views/ModalDialog.axaml`, replace `ExtendClientAreaChromeHints="NoChrome"` with `WindowDecorations="None"` and remove related extend-area attributes
  - [x] 4.4 In `Views/LoginMethodSelectorWindow.axaml`, replace `ExtendClientAreaChromeHints="NoChrome"` with `WindowDecorations="None"` and remove related extend-area attributes
  - [x] 4.5 In `Views/CentralViewWindow.axaml`, replace `ExtendClientAreaChromeHints="NoChrome"` with `WindowDecorations="None"` and remove related extend-area attributes
  - [x] 4.6 In `Views/MainWindow.axaml.cs`, replace the `ExtendClientAreaChromeHints` assignment with `WindowDecorations = WindowDecorations.None` and remove the `ExtendClientAreaToDecorationsHint = true` and `ExtendClientAreaTitleBarHeightHint = 22` lines
  - [x] 4.7 In `Views/CentralViewWindow.axaml.cs`, replace the `ExtendClientAreaChromeHints` assignment with `WindowDecorations = WindowDecorations.None` and remove the `ExtendClientAreaToDecorationsHint = true` and `ExtendClientAreaTitleBarHeightHint = 40` lines

- [x] 5. Replace SystemDecorations with WindowDecorations
  - [x] 5.1 In `Views/SplashScreen.axaml`, replace `SystemDecorations="None"` with `WindowDecorations="None"`
  - [x] 5.2 In `Views/BackGroundWindow.axaml`, replace `SystemDecorations="None"` with `WindowDecorations="None"`
  - [x] 5.3 In `Views/AuthWindow.axaml`, replace `SystemDecorations="BorderOnly"` with `WindowDecorations="BorderOnly"`
  - [x] 5.4 In `Views/OAuthLoginWindow.axaml`, replace `SystemDecorations="None"` with `WindowDecorations="None"`
  - [x] 5.5 In `Views/BackGroundWindow.axaml.cs`, replace `this.SystemDecorations = SystemDecorations.None` with `this.WindowDecorations = WindowDecorations.None`
  - [x] 5.6 In `Views/AuthWindow.axaml.cs` (`ApplyClassicWindowStyle`), replace `SystemDecorations = SystemDecorations.BorderOnly` with `WindowDecorations = WindowDecorations.BorderOnly`

- [x] 6. Update drag-and-drop API
  - [x] 6.1 In `Views/MainWindow.axaml.cs`, replace all `e.Data` accesses with `e.DataTransfer`, replace `DataFormats.Text` with `DataFormat.Text`, and make `Drop` handler async using `await e.DataTransfer.GetTextAsync()`
  - [x] 6.2 In `Views/CentralViewWindow.axaml.cs`, apply the same `DragEventArgs.Data` → `DragEventArgs.DataTransfer`, `DataFormats` → `DataFormat`, and async data access changes
  - [x] 6.3 In any file that calls `DragDrop.DoDragDrop(...)`, replace with `await DragDrop.DoDragDropAsync(...)` and replace any `DataObject` construction with `DataTransfer` + `DataTransferItem`

- [x] 7. Replace Watermark and UseFloatingWatermark with PlaceholderText and UseFloatingPlaceholder
  - [x] 7.1 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/WebSocketDebugWindow.axaml` (2 occurrences: ServerUrl, AccessToken TextBoxes)
  - [x] 7.2 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/HelpWindow.axaml` (1 occurrence: search box)
  - [x] 7.3 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/BusManagementToolWindow.axaml`
  - [x] 7.4 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/MaintenanceManagementToolWindow.axaml`
  - [x] 7.5 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/UserManagementToolWindow.axaml`
  - [x] 7.6 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/TicketManagementToolWindow.axaml`
  - [x] 7.7 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/RouteManagementToolWindow.axaml`
  - [x] 7.8 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/JobManagementToolWindow.axaml`
  - [x] 7.9 Search-and-replace `Watermark=` → `PlaceholderText=` in `Views/ManagementToolWindowsViews/EmployeeManagementToolWindow.axaml`
  - [x] 7.10 In `Views/ManagementToolWindowsViews/RouteSchedulesManagementToolWindow.axaml`, verify whether `CalendarDatePicker.Watermark` is renamed in Avalonia 12 — if yes, replace with `PlaceholderText`; if no, document the finding
  - [x] 7.11 Search-and-replace `UseFloatingWatermark=` → `UseFloatingPlaceholder=` across all AXAML files
  - [x] 7.12 In `Views/AuthWindow.axaml.cs`, replace `.Watermark =` with `.PlaceholderText =` on all three TextBox instances (username, password, TOTP code — 3 occurrences)
  - [x] 7.13 In `Views/OAuthLoginWindow.axaml.cs`, replace `.Watermark =` with `.PlaceholderText =` on the redirect URI TextBox (1 occurrence)
  - [x] 7.14 In `ViewModels/UserManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (3 occurrences: login, password, leave-empty hint)
  - [x] 7.15 In `ViewModels/TicketManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (4 occurrences: price and seat NumericUpDown in both create and edit dialogs)
  - [x] 7.16 In `ViewModels/SalesManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (2 occurrences: buyer name, buyer phone)
  - [x] 7.17 In `ViewModels/RouteManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (~14 occurrences across create and edit dialogs)
  - [x] 7.18 In `ViewModels/MaintenanceManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (6 occurrences across add and edit dialogs)
  - [x] 7.19 In `ViewModels/JobManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (4 occurrences across add and edit dialogs)
  - [x] 7.20 In `ViewModels/EmployeeManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (3 occurrences: surname, name, patronym)
  - [x] 7.21 In `ViewModels/BusManagementViewModel.cs`, replace all `.Watermark =` assignments with `.PlaceholderText =` (~14 occurrences across create and edit dialogs)

- [x] 8. Update clipboard API
  - [x] 8.1 In `Views/AboutWindow.axaml.cs`, replace the direct `clipboard.SetTextAsync(...)` call with `ClipboardExtensions.SetTextAsync(clipboard, ...)`
  - [x] 8.2 In any file that calls `clipboard.GetTextAsync()`, replace with `ClipboardExtensions.TryGetTextAsync(clipboard)`
  - [x] 8.3 In any file that calls `clipboard.GetFormatsAsync()`, replace with `ClipboardExtensions.GetDataFormatsAsync(clipboard)`
  - [x] 8.4 In any file that constructs a `DataObject` and passes it to `clipboard.SetDataObjectAsync(...)`, replace with a `DataTransfer` containing `DataTransferItem` instances passed to `clipboard.SetDataAsync(...)`

- [x] 9. Resolve compiled bindings and IBinding removal
  - [x] 9.1 Confirm `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>` is present in the main `.csproj` (added in task 1.1)
  - [x] 9.2 Verify no `IBinding`-typed variable declarations exist in the codebase (grep scan found none — all `new Binding(...)` calls in `AuthWindow.axaml.cs` are inline without `IBinding` type declarations); if any are found during compilation, replace with `BindingBase`
  - [x] 9.3 Verify `Views/AuthWindow.axaml.cs` compiles correctly — `new Binding(...)` calls remain valid as `ReflectionBinding` aliases when compiled bindings are off

- [ ] 10. Update third-party Avalonia-dependent packages
  - [ ] 10.1 Research and update `Semi.Avalonia` and `Semi.Avalonia.DataGrid` to Avalonia 12-compatible versions in the main `.csproj`
  - [ ] 10.2 Research and update `FluentAvaloniaUI` to an Avalonia 12-compatible version
  - [ ] 10.3 Research and update `Material.Icons.Avalonia` to an Avalonia 12-compatible version
  - [ ] 10.4 Research and update `SukiUI` to an Avalonia 12-compatible version
  - [ ] 10.5 Research and update `Avalonia.Labs.Controls` to an Avalonia 12-compatible version
  - [ ] 10.6 Research and update `Classic.Avalonia.Theme` and `Classic.CommonControls.Avalonia` to Avalonia 12-compatible versions, or document incompatibility and propose removal/replacement
  - [ ] 10.7 Research and update `ReDocking.Avalonia` to an Avalonia 12-compatible version, or document incompatibility
  - [ ] 10.8 Research and update `Dock.Model.Mvvm` to an Avalonia 12-compatible version
  - [ ] 10.9 Research and update `MessageBox.Avalonia` to an Avalonia 12-compatible version
  - [ ] 10.10 Research and update `WebView.Avalonia` to an Avalonia 12-compatible version
  - [ ] 10.11 Research and update `LiveChartsCore.SkiaSharpView.Avalonia` to a stable Avalonia 12-compatible version
  - [ ] 10.12 Research and update `FluentAvalonia.ProgressRing` to an Avalonia 12-compatible version, or document incompatibility
  - [ ] 10.13 For any package with no Avalonia 12-compatible release, document the package name, current version, incompatibility status, and chosen resolution strategy (pin-and-test, remove, or fork)

- [x] 11. Verify Desktop project AppBuilder chain
  - [x] 11.1 Confirm `Program.cs` in the Desktop project retains `UsePlatformDetect()` (valid in Avalonia 12)
  - [x] 11.2 Confirm the project uses `Avalonia.Controls.WebView` (version 12.0.0-preview2 or later) and the `NativeWebView` control pattern instead of the deprecated `UseDesktopWebView()` extension method from `WebView.Avalonia`

- [x] 12. Build and runtime verification
  - [x] 12.1 Run `dotnet build -c Release` on the solution and confirm zero compiler errors
  - [x] 12.2 Confirm build output contains no `CS0618` (obsolete) or `AVL*` Avalonia-specific warnings related to migrated APIs
  - [x] 12.3 Confirm `.csproj` contains `<TargetFramework>net10.0</TargetFramework>` and `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>`
  - [x] 12.4 Confirm `.csproj` contains `AvaloniaUI.DiagnosticsSupport` and does NOT contain `Avalonia.Diagnostics`
  - [x] 12.5 Launch the app in Debug mode and verify DevTools opens with F12
  - [x] 12.6 Navigate through the login flow (OAuth and traditional) and verify no runtime exceptions
  - [x] 12.7 Open the main window and verify window chrome renders correctly (no OS title bar overlap)
  - [x] 12.8 Open AboutWindow and use the "Copy Info" button — verify clipboard write succeeds
  - [x] 12.9 Open a management tool window and verify placeholder text appears in search/input fields
  - [x] 12.10 Attempt a drag-and-drop operation in MainWindow or CentralViewWindow and verify it completes without exceptions
  - [x] 12.11 Verify SplashScreen, BackGroundWindow, and AuthWindow render without OS decorations

- [x] 13. Avalonia.Controls.Maui integration (optional)
  - [x] 13.1 Create the new project directory `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui/` and its `.csproj` with MAUI workload TFMs (`net10.0-android`, `net10.0-ios`, `net10.0` — do NOT include `net10.0-windows`, WinUI path is not production-ready), a `<ProjectReference>` to `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj`, and package references to `Avalonia.Controls.Maui` plus optional `Avalonia.Controls.Maui.Essentials`, `Avalonia.Controls.Maui.Compatibility`, `Avalonia.Controls.Maui.SkiaSharp.Views` as needed
  - [x] 13.2 Create `MauiProgram.cs` in the new project: call `UseMauiApp<MauiShellApp>()` (the MAUI App.cs in this project), then `UseAvaloniaApp()` for full hosting mode (or `UseAvaloniaEmbedding<App>()` for embedding mode); `UseAvaloniaApp` must come before any optional extensions; for Browser/WASM pass `useSingleViewLifetime: true`; chain `UseAvaloniaEssentials()` and `ConfigureFonts(...)` as needed
  - [x] 13.3 Create a MAUI `App.cs` in the new project (deriving from `Microsoft.Maui.Controls.Application`) as the MAUI shell entry point — this is the class passed to `UseMauiApp<T>()` and is entirely separate from the Avalonia `App.axaml.cs` in the Unity library
  - [x] 13.4 Create platform-specific entry point files under `Platforms/` as required by the MAUI workload (e.g., `Android/MainApplication.cs`, `iOS/AppDelegate.cs`)
  - [x] 13.5 Register fonts via `ConfigureFonts` in `MauiProgram.cs`; place font files in `Resources/Fonts/` with `MauiFont` build action — at build time they are converted to Avalonia embedded resources under `Assets/Fonts/`; register images with `MauiImage` and raw assets with `MauiAsset` build actions
  - [x] 13.6 In `App.axaml.cs` `RegisterServices()`, wrap `AvaloniaWebViewBuilder.Initialize(default)` in a `#if DESKTOP` guard so it is not invoked on MAUI targets where the desktop WebView is unavailable
  - [x] 13.7 In `App.axaml.cs` `OnFrameworkInitializationCompleted()`, add an `IActivityApplicationLifetime` branch for Android alongside the existing `IClassicDesktopStyleApplicationLifetime` and `ISingleViewApplicationLifetime` branches; confirm the full desktop startup flow (`ShowSplashScreenAndInitialize`, `BackGroundWindow`, `LoginMethodSelectorWindow`, `MainWindow`, `UnderConstructionWindow`) remains entirely inside the `IClassicDesktopStyleApplicationLifetime` branch
  - [x] 13.8 Register any custom control handlers via `ConfigureMauiHandlers` after `UseAvaloniaApp` in `MauiProgram.cs` — registrations after `UseAvaloniaApp` take precedence over the defaults
  - [x] 13.9 Audit desktop-specific APIs for platform incompatibility — `DragDrop.DoDragDropAsync`, `BeginMoveDrag`, `Window.WindowDecorations` — and wrap each in `#if DESKTOP` guards where needed for mobile/browser targets
  - [x] 13.10 When running under MAUI, disable custom titlebars and restore OS window decorations. AXAML is compiled at build time so `#if` guards cannot be used in `.axaml` files — visibility must be controlled at runtime from code-behind:
      - `MainWindow.axaml`: add `x:Name="TitleBarGrid"` to the custom titlebar `<Grid Grid.Row="0" Background="{DynamicResource TitleBarBackground}" Height="35">` so it can be referenced from code-behind
      - `MainWindow.axaml.cs`: in the constructor, wrap `WindowDecorations = WindowDecorations.None` and the `SetupTitleBar()` call in `#if DESKTOP`; add a `#if !DESKTOP` block that sets `this.FindControl<Grid>("TitleBarGrid")!.IsVisible = false` and updates the root grid's first `RowDefinition.Height` to `new GridLength(0)` so the collapsed row leaves no dead space
      - `CentralViewWindow.axaml`: add `x:Name="TitleBarGrid"` to the titlebar `<Grid Grid.Row="0" Background="#111116" ...>` (the 36px row with `MinimizeBtn`, `MaximizeBtn`, `CloseBtn`)
      - `CentralViewWindow.axaml.cs`: in the constructor, add a `#if !DESKTOP` block that sets `this.FindControl<Grid>("TitleBarGrid")!.IsVisible = false` and sets the root grid's first `RowDefinition.Height` to `new GridLength(0)`; the `WindowDecorations` assignment is already absent from this constructor so no guard is needed there
      - For both windows, wrap `BeginMoveDrag(e)` inside `TitleBarDragArea_PointerPressed` in `#if DESKTOP` since the method does not exist on MAUI targets
      - `AuthWindow.axaml.cs`: wrap `BeginMoveDrag(e)` in its pointer-pressed handler in `#if DESKTOP`; also guard the `WindowDecorations` assignments inside `ApplyClassicWindowStyle` and `ApplyModernWindowStyle` with `#if DESKTOP`
  - [x] 13.11 Add the new MAUI project to `SpacetimeDB-BRU-AVTOPARK-avtobusov.sln` so it participates in solution-level builds
  - [x] 13.12 Verify the existing `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj` and `Program.cs` are unchanged and the desktop build still produces a working executable