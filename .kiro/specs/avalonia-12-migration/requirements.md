# Requirements Document

## Introduction

This document defines the requirements for migrating the BRU Avtopark Ticket Sales Avalonia client application from Avalonia 11.2.3 to Avalonia 12. The migration involves updating the core framework package, resolving all breaking API changes in C# code and AXAML markup, updating the target framework, and ensuring all third-party Avalonia-dependent packages are upgraded to Avalonia 12-compatible versions. The application must retain full functional parity with the current Avalonia 11 build after migration.

## Glossary

- **Migration_Tool**: The automated or manual process responsible for applying code and markup changes to the project
- **Project_File**: The `.csproj` file `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj`
- **Desktop_Project_File**: The `.csproj` file `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj`
- **App**: The Avalonia desktop application `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity`
- **AXAML_File**: Any `.axaml` markup file in the Avalonia project
- **Code_File**: Any `.cs` source file in the Avalonia project
- **Compiled_Binding**: An Avalonia binding resolved at compile time using `x:DataType`
- **Reflection_Binding**: An Avalonia binding resolved at runtime without `x:DataType`
- **DevTools**: The Avalonia developer diagnostics overlay
- **DataAnnotationsValidationPlugin**: The removed Avalonia 11 class used to disable data annotation validation
- **WindowDecorations**: The Avalonia 12 replacement for `SystemDecorations` and `ExtendClientAreaChromeHints`
- **DataFormat**: The Avalonia 12 replacement for `DataFormats` in drag-and-drop operations
- **ClipboardExtensions**: The Avalonia 12 extension methods replacing direct `IClipboard` method calls
- **Third_Party_Package**: Any NuGet package that depends on Avalonia (Semi.Avalonia, FluentAvaloniaUI, SukiUI, etc.)
- **AppBuilder**: The Avalonia application builder chain configured in `Program.cs`
- **MAUI_Integration**: The optional `Avalonia.Controls.Maui` backend enabling cross-platform hosting of the App inside a .NET MAUI shell
- **IAsyncDataTransfer**: The Avalonia 12 interface replacing `IDataObject` for drag-and-drop and clipboard data access
- **DataTransfer**: The Avalonia 12 concrete class replacing `DataObject`, containing a collection of `DataTransferItem` instances
- **DataTransferItem**: The Avalonia 12 class representing a single item in a `DataTransfer`, pairing a `DataFormat` with its value
- **BindingBase**: The Avalonia 12 base class replacing the removed `IBinding` interface; `Binding` is kept as an alias for `ReflectionBinding`

---

## Requirements

### Requirement 1: Core Framework Package Upgrade

**User Story:** As a developer, I want the project to reference Avalonia 12 packages, so that the application can use the latest framework features and receive ongoing support.

#### Acceptance Criteria

1. THE Project_File SHALL reference Avalonia version 12.x.x for all `Avalonia.*` core packages.
2. THE Project_File SHALL target `net10.0` as the TargetFramework.
3. WHEN the project is built after the upgrade, THE App SHALL compile without errors related to missing or incompatible Avalonia core package versions.
4. THE Project_File SHALL reference `AvaloniaUI.DiagnosticsSupport` in place of `Avalonia.Diagnostics` for the conditional Debug-only diagnostics package.

---

### Requirement 2: Remove Obsolete Data Validation Disabling Code

**User Story:** As a developer, I want the removed `BindingPlugins.DataValidators` API usage eliminated, so that the project compiles cleanly against Avalonia 12.

#### Acceptance Criteria

1. THE App SHALL NOT contain any reference to `BindingPlugins.DataValidators` or `DataAnnotationsValidationPlugin` after migration.
2. WHEN `App.axaml.cs` is compiled against Avalonia 12, THE App SHALL compile without errors or warnings related to the removed data annotation validation API.
3. THE App SHALL retain equivalent runtime behavior, given that the data annotations validation plugin is disabled by default in Avalonia 12.

---

### Requirement 3: Replace DevTools Attachment API

**User Story:** As a developer, I want the DevTools attachment calls updated to the Avalonia 12 API, so that the diagnostics overlay works correctly in Debug builds.

#### Acceptance Criteria

1. THE Code_File `OAuthLoginWindow.axaml.cs` SHALL call `AttachDeveloperTools()` instead of `AttachDevTools()` within `#if DEBUG` blocks.
2. THE Code_File `ModalDialog.axaml.cs` SHALL call `AttachDeveloperTools()` instead of `AttachDevTools()` within `#if DEBUG` blocks.
3. THE Code_File `AuthWindow.axaml.cs` SHALL call `AttachDeveloperTools()` instead of `AttachDevTools()` within `#if DEBUG` blocks.
4. THE Code_File `LoginMethodSelectorWindow.axaml.cs` SHALL call `AttachDeveloperTools()` instead of `AttachDevTools()` within `#if DEBUG` blocks.
5. THE Code_File `HelpWindow.axaml.cs` SHALL call `AttachDeveloperTools()` instead of `AttachDevTools()` within `#if DEBUG` blocks.
6. THE Code_File `BackGroundWindow.axaml.cs` SHALL call `AttachDeveloperTools()` instead of `AttachDevTools()` within `#if DEBUG` blocks.
7. WHEN the App is built in Debug configuration, THE DevTools SHALL attach successfully using the updated API.
8. WHEN the App is built in Release configuration, THE App SHALL NOT include any DevTools attachment code.

---

### Requirement 4: Replace ExtendClientAreaChromeHints with WindowDecorations

**User Story:** As a developer, I want all `ExtendClientAreaChromeHints` usages replaced with the Avalonia 12 `Window.WindowDecorations` API, so that custom window chrome continues to function correctly.

#### Acceptance Criteria

1. THE Code_File `MainWindow.axaml.cs` SHALL use `Window.WindowDecorations` instead of `ExtendClientAreaChromeHints`.
2. THE AXAML_File `MainWindow.axaml` SHALL use the `WindowDecorations` property instead of `ExtendClientAreaChromeHints`.
3. THE AXAML_File `OAuthLoginWindow.axaml` SHALL use the `WindowDecorations` property instead of `ExtendClientAreaChromeHints`.
4. THE AXAML_File `ModalDialog.axaml` SHALL use the `WindowDecorations` property instead of `ExtendClientAreaChromeHints`.
5. THE AXAML_File `LoginMethodSelectorWindow.axaml` SHALL use the `WindowDecorations` property instead of `ExtendClientAreaChromeHints`.
6. THE AXAML_File `CentralViewWindow.axaml` SHALL use the `WindowDecorations` property instead of `ExtendClientAreaChromeHints`.
7. WHEN the App is launched, THE App SHALL render window chrome with the same visual appearance as the Avalonia 11 build.

---

### Requirement 5: Replace SystemDecorations Enum with WindowDecorations

**User Story:** As a developer, I want all `SystemDecorations` enum usages updated to the Avalonia 12 `WindowDecorations` enum, so that borderless and border-only windows render correctly.

#### Acceptance Criteria

1. THE AXAML_File `SplashScreen.axaml` SHALL use `WindowDecorations="None"` in place of `SystemDecorations="None"`.
2. THE AXAML_File `BackGroundWindow.axaml` SHALL use `WindowDecorations="None"` in place of `SystemDecorations="None"`.
3. THE AXAML_File `AuthWindow.axaml` SHALL use `WindowDecorations="BorderOnly"` in place of `SystemDecorations="BorderOnly"`.
4. THE Code_File `BackGroundWindow.axaml.cs` SHALL assign `WindowDecorations.None` to `this.WindowDecorations` in place of `SystemDecorations.None` assigned to `this.SystemDecorations`.
5. THE Code_File `AuthWindow.axaml.cs` SHALL assign `WindowDecorations.BorderOnly` to `WindowDecorations` in the `ApplyClassicWindowStyle()` method in place of `SystemDecorations.BorderOnly` assigned to `SystemDecorations`.
6. WHEN the App renders splash, background, and auth windows, THE App SHALL display the correct window decoration style matching the Avalonia 11 behavior.

---

### Requirement 6: Update Drag-and-Drop API

**User Story:** As a developer, I want all drag-and-drop API usages updated to the Avalonia 12 `DataFormat`/`IAsyncDataTransfer` model, so that drag-and-drop operations continue to function correctly.

#### Acceptance Criteria

1. THE Code_File `MainWindow.axaml.cs` SHALL reference `DataFormat` members instead of `DataFormats` members in `DragOver` and `Drop` event handlers.
2. THE Code_File `CentralViewWindow.axaml.cs` SHALL reference `DataFormat` members instead of `DataFormats` members in `DragOver` and `Drop` event handlers.
3. WHERE `DragEventArgs.Data` is accessed in any Code_File, it SHALL be replaced with `DragEventArgs.DataTransfer` (type `IAsyncDataTransfer`).
4. WHERE `DragDrop.DoDragDrop(...)` is called in any Code_File, it SHALL be replaced with `await DragDrop.DoDragDropAsync(...)`.
5. WHERE a `DataObject` is constructed and passed to drag-and-drop or clipboard APIs, it SHALL be replaced with `DataTransfer` containing `DataTransferItem` instances.
6. WHEN a drag-and-drop operation is performed in the App, THE App SHALL correctly identify and handle the dragged data format.

---

### Requirement 7: Replace TextBox.Watermark and NumericUpDown.Watermark with PlaceholderText

**User Story:** As a developer, I want all `Watermark` and `UseFloatingWatermark` property usages on `TextBox` and `NumericUpDown` controls replaced with their Avalonia 12 equivalents, so that placeholder text renders correctly across both AXAML markup and C# code.

#### Acceptance Criteria

1. THE Migration_Tool SHALL replace all occurrences of the `Watermark` attribute on `TextBox` elements across all AXAML_Files with `PlaceholderText`. Affected files: `WebSocketDebugWindow.axaml`, `HelpWindow.axaml`, `BusManagementToolWindow.axaml`, `MaintenanceManagementToolWindow.axaml`, `UserManagementToolWindow.axaml`, `TicketManagementToolWindow.axaml`, `RouteManagementToolWindow.axaml`, `JobManagementToolWindow.axaml`, `EmployeeManagementToolWindow.axaml`.
2. THE Migration_Tool SHALL replace all occurrences of the `Watermark` attribute on `NumericUpDown` elements across all AXAML_Files with `PlaceholderText`.
3. THE Migration_Tool SHALL replace all occurrences of the `UseFloatingWatermark` attribute on `TextBox` elements across all AXAML_Files with `UseFloatingPlaceholder`.
4. THE AXAML_File `RouteSchedulesManagementToolWindow.axaml` SHALL have its `CalendarDatePicker.Watermark` attribute evaluated — if `CalendarDatePicker` renames the property in Avalonia 12, it SHALL be replaced with `PlaceholderText`; otherwise the finding SHALL be documented.
5. THE Code_File `AuthWindow.axaml.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all three TextBox instances created in C# (username, password, and TOTP code fields).
6. THE Code_File `OAuthLoginWindow.axaml.cs` SHALL assign `PlaceholderText` instead of `Watermark` on the redirect URI TextBox created in C#.
7. THE Code_File `UserManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox controls (login, password, and leave-empty hint fields).
8. THE Code_File `TicketManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox and NumericUpDown controls (ticket price and seat number fields — 4 occurrences across create and edit dialogs).
9. THE Code_File `SalesManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox controls (buyer name and buyer phone fields).
10. THE Code_File `RouteManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox and NumericUpDown controls across both the create and edit dialogs (~14 occurrences).
11. THE Code_File `MaintenanceManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox controls across both the add and edit dialogs (6 occurrences).
12. THE Code_File `JobManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox controls across both the add and edit dialogs (4 occurrences).
13. THE Code_File `EmployeeManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox controls (surname, name, patronym fields).
14. THE Code_File `BusManagementViewModel.cs` SHALL assign `PlaceholderText` instead of `Watermark` on all programmatically created TextBox and NumericUpDown controls across both the create and edit dialogs (~14 occurrences).
15. WHEN the App is built, THE App SHALL compile without obsolescence warnings related to `TextBox.Watermark`, `NumericUpDown.Watermark`, or `TextBox.UseFloatingWatermark`.
16. WHEN a TextBox or NumericUpDown with placeholder text is rendered, THE App SHALL display the placeholder text identically to the Avalonia 11 behavior.

---

### Requirement 8: Update Clipboard API Usage

**User Story:** As a developer, I want the clipboard API calls updated to the Avalonia 12 `ClipboardExtensions` and `IAsyncDataTransfer` model, so that clipboard operations work correctly.

#### Acceptance Criteria

1. THE Code_File `AboutWindow.axaml.cs` SHALL use `ClipboardExtensions.SetTextAsync` (extension method on `IClipboard`) instead of calling `IClipboard.SetTextAsync` directly.
2. WHERE any Code_File calls `IClipboard.GetTextAsync`, it SHALL be replaced with `ClipboardExtensions.TryGetTextAsync`.
3. WHERE any Code_File calls `IClipboard.GetFormatsAsync`, it SHALL be replaced with `ClipboardExtensions.GetDataFormatsAsync`.
4. WHERE any Code_File constructs a `DataObject` and passes it to `IClipboard.SetDataObjectAsync`, it SHALL be replaced with a `DataTransfer` containing `DataTransferItem` instances passed to `IClipboard.SetDataAsync`.
5. WHEN the user triggers a clipboard copy action in the App, THE App SHALL successfully write text to the system clipboard.

---

### Requirement 9: Resolve Compiled Bindings Default Change

**User Story:** As a developer, I want all AXAML bindings to be explicitly typed or opted out of compiled bindings, so that the application builds successfully under Avalonia 12's compiled-bindings-by-default behavior.

#### Acceptance Criteria

1. THE Project_File SHALL explicitly set `<AvaloniaUseCompiledBindingsByDefault>` to either `true` or `false` to document the intended binding mode.
2. WHERE `<AvaloniaUseCompiledBindingsByDefault>` is set to `true`, THEN every `{Binding}` expression in AXAML_Files that requires compiled binding SHALL have a corresponding `x:DataType` attribute on the containing element or ancestor.
3. WHERE `<AvaloniaUseCompiledBindingsByDefault>` is set to `true`, THEN any `{Binding}` expression that cannot be statically typed SHALL be replaced with `{ReflectionBinding}`.
4. THE Code_File `AuthWindow.axaml.cs` SHALL be verified to compile correctly with any `Binding` instances constructed directly in C# code. In Avalonia 12, `IBinding` is removed and replaced by `BindingBase`; the `Binding` class is kept as an alias for `ReflectionBinding`. Any code that declares a variable as `IBinding` SHALL be updated to `BindingBase`. Any code that creates `new Binding(...)` for reflection-based bindings MAY be updated to `new ReflectionBinding(...)` for clarity.
5. WHEN the App is built with compiled bindings enabled, THE App SHALL compile without binding-related build errors.
6. WHEN the App is run, THE App SHALL resolve all bindings correctly and display data as expected.

---

### Requirement 10: Third-Party Package Compatibility

**User Story:** As a developer, I want all third-party Avalonia-dependent packages updated to Avalonia 12-compatible versions, so that the application builds and runs without package compatibility errors.

#### Acceptance Criteria

1. THE Project_File SHALL reference Avalonia 12-compatible versions of Semi.Avalonia and Semi.Avalonia.DataGrid.
2. THE Project_File SHALL reference an Avalonia 12-compatible version of FluentAvaloniaUI.
3. THE Project_File SHALL reference an Avalonia 12-compatible version of Material.Icons.Avalonia.
4. THE Project_File SHALL reference an Avalonia 12-compatible version of SukiUI.
5. THE Project_File SHALL reference an Avalonia 12-compatible version of Avalonia.Labs.Controls.
6. THE Project_File SHALL reference Avalonia 12-compatible versions of Classic.Avalonia.Theme and Classic.CommonControls.Avalonia.
7. THE Project_File SHALL reference an Avalonia 12-compatible version of ReDocking.Avalonia.
8. THE Project_File SHALL reference an Avalonia 12-compatible version of Dock.Model.Mvvm.
9. THE Project_File SHALL reference an Avalonia 12-compatible version of MessageBox.Avalonia.
10. THE Project_File SHALL reference Avalonia.Controls.WebView (version 12.0.0-preview2 or later) and use the NativeWebView/NativeWebDialog control pattern instead of the deprecated WebView.Avalonia package and UseDesktopWebView() extension method.
11. THE Project_File SHALL reference an Avalonia 12-compatible version of LiveChartsCore.SkiaSharpView.Avalonia.
12. THE Project_File SHALL reference an Avalonia 12-compatible version of FluentAvalonia.ProgressRing.
13. IF an Avalonia 12-compatible version of a Third_Party_Package does not exist at migration time, THEN THE Migration_Tool SHALL document the incompatible package and propose a replacement or removal strategy.
14. WHEN the App is built after all package updates, THE App SHALL compile without NuGet dependency resolution errors.

---

### Requirement 11: Build and Runtime Verification

**User Story:** As a developer, I want the migrated application to build cleanly and run with full functional parity, so that end users experience no regressions after the migration.

#### Acceptance Criteria

1. WHEN the App is built in Release configuration after migration, THE App SHALL produce zero compiler errors.
2. WHEN the App is built in Release configuration after migration, THE App SHALL produce zero compiler warnings related to Avalonia 12 obsolete or removed APIs.
3. WHEN the App is launched after migration, THE App SHALL reach the main window without runtime exceptions.
4. WHEN the user interacts with all major UI surfaces (login, main window, management tool windows, OAuth login, modal dialogs), THE App SHALL behave identically to the Avalonia 11 build.
5. WHEN the App is built in Debug configuration after migration, THE DevTools overlay SHALL be accessible via the standard keyboard shortcut.

---

### Requirement 12: Desktop Project Package Alignment

**User Story:** As a developer, I want the `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop` project to have its packages and AppBuilder chain updated to Avalonia 12, so that the desktop entry point compiles and launches correctly alongside the main Unity project.

#### Acceptance Criteria

1. THE Desktop_Project_File SHALL reference Avalonia 12-compatible versions of `Avalonia.Desktop`, `Avalonia.ReactiveUI`, and `WebView.Avalonia` (desktop variant).
2. THE Code_File `Program.cs` in the Desktop project SHALL retain the `UsePlatformDetect()` call, which remains valid in Avalonia 12 and includes HarfBuzz by default.
3. THE Code_File `Program.cs` in the Desktop project SHALL use an Avalonia 12-compatible `UseDesktopWebView()` extension from the updated `Avalonia.WebView.Desktop` package.
4. WHEN the Desktop project is built after package updates, THE Desktop_Project_File SHALL compile without errors related to missing or incompatible Avalonia package versions.
5. WHEN the App is launched via the Desktop entry point after migration, THE App SHALL start successfully and reach the main window without runtime exceptions.

---

### Requirement 13: Avalonia.Controls.Maui Integration (Optional)

**User Story:** As a developer, I want to optionally create a new MAUI host project that references the existing Unity library, so that the App can target Browser/WASM and future mobile platforms beyond the current Windows/macOS/Linux desktop path, without modifying the existing desktop entry point.

#### Acceptance Criteria

1. WHERE MAUI_Integration is adopted, a new project `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui` SHALL be created as a separate `.csproj` file. This project SHALL reference `Avalonia.Controls.Maui` at an Avalonia 12-compatible version and SHALL include a `<ProjectReference>` to `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj`. The project SHALL NOT include `net10.0-windows` as a target framework moniker, as the WinUI path is not production-ready.
2. WHERE MAUI_Integration is adopted, the new MAUI project SHALL contain a `MauiProgram.cs` that calls `UseMauiApp<T>()` with the MAUI `App.cs` class from the new project, then chains `UseAvaloniaApp()` for full hosting mode (or `UseAvaloniaEmbedding<App>()` for embedding mode). `UseAvaloniaApp` SHALL be called before any optional extension methods (`UseAvaloniaEssentials`, `UseAvaloniaCompatibility`, `UseAvaloniaSkiaSharp`).
3. WHERE MAUI_Integration is adopted, the new MAUI project SHALL contain a MAUI `App.cs` (a class deriving from `Microsoft.Maui.Controls.Application`) that is distinct from the Avalonia `App.axaml.cs` in the Unity library. The MAUI `App.cs` is the class passed to `UseMauiApp<T>()`; the Avalonia `App.axaml.cs` handles Avalonia's `OnFrameworkInitializationCompleted` lifecycle.
4. WHERE MAUI_Integration is adopted and Android is a target platform, THE Code_File `App.axaml.cs` `OnFrameworkInitializationCompleted` method SHALL be extended to handle `IActivityApplicationLifetime` alongside the existing `IClassicDesktopStyleApplicationLifetime` and `ISingleViewApplicationLifetime` branches.
5. WHERE MAUI_Integration is adopted, the `AvaloniaWebViewBuilder.Initialize(default)` call in `App.axaml.cs` `RegisterServices()` SHALL be wrapped in a `#if DESKTOP` guard so that it is not invoked on MAUI targets where the desktop WebView is unavailable.
6. WHERE MAUI_Integration is adopted, the desktop-specific startup flow in `App.axaml.cs` `OnFrameworkInitializationCompleted` — including `ShowSplashScreenAndInitialize`, multi-window management (`BackGroundWindow`, `LoginMethodSelectorWindow`, `MainWindow`), and `UnderConstructionWindow` — SHALL remain entirely inside the `IClassicDesktopStyleApplicationLifetime` branch and SHALL NOT execute on MAUI single-view or mobile targets.
7. THE existing `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj` and its `Program.cs` SHALL NOT be modified as part of MAUI integration. The desktop entry point SHALL remain the primary build target for Windows/macOS/Linux.
8. WHERE MAUI_Integration is adopted, the new MAUI project SHALL be added to the solution file so it participates in solution-level builds.
9. WHERE MAUI_Integration is NOT adopted, THE App SHALL remain buildable and runnable using only the existing `Avalonia.Desktop` entry point with no changes required to `App.axaml.cs` or `Desktop.csproj`.
10. IF MAUI_Integration is evaluated and desktop-specific APIs (multi-window management, `Window.WindowDecorations`, `DragDrop.DoDragDropAsync`, `BeginMoveDrag`) are found to be incompatible with a mobile or browser target, THEN THE Migration_Tool SHALL document each incompatibility and the platform-conditional strategy applied.