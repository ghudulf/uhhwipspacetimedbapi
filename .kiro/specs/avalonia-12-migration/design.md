# Design Document: Avalonia 12 Migration

## Overview

This document describes the technical design for migrating the `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity` application from Avalonia 11.2.3 (targeting `net9.0`) to Avalonia 12 (targeting `net10.0`). The migration is a breaking-change upgrade: Avalonia 12 removes several APIs, renames others, and changes default behaviors. The goal is full functional parity with the Avalonia 11 build after migration.

The migration is purely mechanical — no new features are introduced. Every change maps a removed or renamed API to its Avalonia 12 replacement. The work is organized into discrete, independently verifiable change groups that can be applied and verified one at a time.

### Key Avalonia 12 Breaking Changes Affecting This Project

| Area | Avalonia 11 API | Avalonia 12 Replacement |
|---|---|---|
| Data validation | `BindingPlugins.DataValidators` / `DataAnnotationsValidationPlugin` | Removed (disabled by default) |
| DevTools | `AttachDevTools()` | `AttachDeveloperTools()` |
| Window chrome | `ExtendClientAreaChromeHints` enum/property | `Window.WindowDecorations` property |
| Window borders | `SystemDecorations` enum/property | `WindowDecorations` enum/property |
| Drag-and-drop data | `IDataObject` / `DataObject` | `IAsyncDataTransfer` / `DataTransfer` + `DataTransferItem` |
| Drag-and-drop event | `DragEventArgs.Data` | `DragEventArgs.DataTransfer` |
| Drag-and-drop initiation | `DragDrop.DoDragDrop(...)` | `await DragDrop.DoDragDropAsync(...)` |
| Drag-and-drop formats | `DataFormats.Text` (static class) | `DataFormat.Text` (renamed type) |
| Drag-and-drop extensions | `DataObjectExtensions` | `AsyncDataTransferExtensions` |
| Placeholder text | `TextBox.Watermark` / `NumericUpDown.Watermark` | `PlaceholderText` property |
| Floating placeholder | `TextBox.UseFloatingWatermark` | `TextBox.UseFloatingPlaceholder` |
| Clipboard text read | `IClipboard.GetTextAsync()` direct call | `ClipboardExtensions.TryGetTextAsync()` |
| Clipboard text write | `IClipboard.SetTextAsync()` direct call | `ClipboardExtensions.SetTextAsync()` |
| Clipboard formats | `IClipboard.GetFormatsAsync()` | `ClipboardExtensions.GetDataFormatsAsync()` |
| Clipboard data write | `IClipboard.SetDataObjectAsync(DataObject)` | `IClipboard.SetDataAsync(DataTransfer)` |
| Binding interface | `IBinding` | `BindingBase` |
| Reflection binding | `new Binding(...)` (reflection) | `new ReflectionBinding(...)` (explicit alias) |
| Diagnostics package | `Avalonia.Diagnostics` | `AvaloniaUI.DiagnosticsSupport` |
| Compiled bindings | Off by default | On by default |
| Target framework | `net9.0` | `net10.0` |

---

## Architecture

The application follows a standard Avalonia MVVM architecture:

```
BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop  (entry point)
  └── Program.cs  (AppBuilder chain)

BRU.Avtopark.TicketSalesAPP.Avalonia.Unity  (main project)
  ├── App.axaml / App.axaml.cs              (application lifecycle)
  ├── Views/                                 (Window and UserControl code-behind)
  ├── ViewModels/                            (MVVM view models, some create controls in C#)
  └── Assets/                               (images, fonts)
```

The migration touches both projects. The main project has the bulk of the API changes; the desktop project needs package version bumps and AppBuilder chain verification.

### Migration Strategy

Changes are applied in dependency order:

1. **Package versions** — update `.csproj` files first so the compiler reports the correct errors
2. **Removed APIs** — delete `DisableAvaloniaDataAnnotationValidation()` from `App.axaml.cs`
3. **Renamed APIs** — mechanical find-and-replace across `.cs` and `.axaml` files
4. **Binding mode** — explicitly set `AvaloniaUseCompiledBindingsByDefault` and audit bindings
5. **Third-party packages** — update each package to its Avalonia 12-compatible version
6. **Build verification** — confirm zero errors and zero Avalonia-related warnings

---

## Components and Interfaces

### 1. Project Files

**`BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj`**

Changes required:
- `<TargetFramework>net9.0</TargetFramework>` → `net10.0`
- All `Avalonia.*` core packages: `11.2.3` → `12.x.x`
- Replace `Avalonia.Diagnostics` conditional reference with `AvaloniaUI.DiagnosticsSupport`
- Add `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>` (see §Compiled Bindings)
- Update all third-party Avalonia-dependent packages (see §Third-Party Packages)

**`BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj`**

Changes required:
- `<TargetFramework>net9.0</TargetFramework>` → `net10.0`
- `Avalonia.Desktop`, `Avalonia.ReactiveUI` → Avalonia 12 versions
- `WebView.Avalonia` desktop variant → Avalonia 12-compatible version

### 2. App.axaml.cs — Remove Data Validation Code

The `DisableAvaloniaDataAnnotationValidation()` method and its call site must be deleted entirely. In Avalonia 12 the `DataAnnotationsValidationPlugin` is not registered by default, so the method body would not compile (`BindingPlugins.DataValidators` and `DataAnnotationsValidationPlugin` are removed). No replacement code is needed.

```csharp
// DELETE this entire method:
private void DisableAvaloniaDataAnnotationValidation()
{
    var dataValidationPluginsToRemove =
        BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
    foreach (var plugin in dataValidationPluginsToRemove)
        BindingPlugins.DataValidators.Remove(plugin);
}
```

Also remove the `using Avalonia.Data.Core.Plugins;` and `using System.Linq;` imports if they become unused.

### 3. DevTools Attachment — 7 Files

`AttachDevTools()` is renamed to `AttachDeveloperTools()`. All occurrences are inside `#if DEBUG` guards. The grep search found the following files:

| File | Change |
|---|---|
| `Views/OAuthLoginWindow.axaml.cs` | `AttachDevTools()` → `AttachDeveloperTools()` |
| `Views/ModalDialog.axaml.cs` | `AttachDevTools()` → `AttachDeveloperTools()` |
| `Views/AuthWindow.axaml.cs` | `AttachDevTools()` → `AttachDeveloperTools()` |
| `Views/LoginMethodSelectorWindow.axaml.cs` | `AttachDevTools()` → `AttachDeveloperTools()` |
| `Views/HelpWindow.axaml.cs` | `AttachDevTools()` → `AttachDeveloperTools()` |
| `Views/BackGroundWindow.axaml.cs` | `AttachDevTools()` → `AttachDeveloperTools()` |

All 6 files must be updated.

### 4. Window Chrome — ExtendClientAreaChromeHints → WindowDecorations

**AXAML files** — replace the `ExtendClientAreaChromeHints` attribute:

| File | Old value | New value |
|---|---|---|
| `Views/MainWindow.axaml` | `ExtendClientAreaChromeHints="NoChrome"` | `WindowDecorations="None"` |
| `Views/OAuthLoginWindow.axaml` | `ExtendClientAreaChromeHints="NoChrome"` | `WindowDecorations="None"` |
| `Views/ModalDialog.axaml` | `ExtendClientAreaChromeHints="NoChrome"` | `WindowDecorations="None"` |
| `Views/LoginMethodSelectorWindow.axaml` | `ExtendClientAreaChromeHints="NoChrome"` | `WindowDecorations="None"` |
| `Views/CentralViewWindow.axaml` | `ExtendClientAreaChromeHints="NoChrome"` | `WindowDecorations="None"` |

**C# files** — replace constructor assignments and remove removed properties:

| File | Old code | New code |
|---|---|---|
| `Views/MainWindow.axaml.cs` | `ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.SystemChrome \| ExtendClientAreaChromeHints.OSXThickTitleBar;` + `ExtendClientAreaToDecorationsHint = true;` + `ExtendClientAreaTitleBarHeightHint = 22;` | `WindowDecorations = WindowDecorations.None;` (remove the other two lines) |
| `Views/CentralViewWindow.axaml.cs` | Same pattern with `ExtendClientAreaTitleBarHeightHint = 40;` | `WindowDecorations = WindowDecorations.None;` (remove the other two lines) |

Note: `ExtendClientAreaToDecorationsHint` and `ExtendClientAreaTitleBarHeightHint` are removed in Avalonia 12. These properties must be removed from both AXAML attributes and C# constructor assignments in all affected files.

### 5. Window Borders — SystemDecorations → WindowDecorations

**AXAML files:**

| File | Old | New |
|---|---|---|
| `Views/SplashScreen.axaml` | `SystemDecorations="None"` | `WindowDecorations="None"` |
| `Views/BackGroundWindow.axaml` | `SystemDecorations="None"` | `WindowDecorations="None"` |
| `Views/AuthWindow.axaml` | `SystemDecorations="BorderOnly"` | `WindowDecorations="BorderOnly"` |
| `Views/OAuthLoginWindow.axaml` | `SystemDecorations="None"` | `WindowDecorations="None"` |

**C# files:**

| File | Old | New |
|---|---|---|
| `Views/BackGroundWindow.axaml.cs` | `this.SystemDecorations = SystemDecorations.None;` | `this.WindowDecorations = WindowDecorations.None;` |
| `Views/AuthWindow.axaml.cs` (`ApplyClassicWindowStyle`) | `SystemDecorations = SystemDecorations.BorderOnly;` | `WindowDecorations = WindowDecorations.BorderOnly;` |

### 6. Drag-and-Drop — Full API Migration

Avalonia 12 replaces the entire drag-and-drop data model. Three distinct changes must be applied:

**6a. `DataFormats` → `DataFormat` (type rename)**

Both `MainWindow.axaml.cs` and `CentralViewWindow.axaml.cs` use `DataFormats.Text` in `DragOver` and `Drop` handlers. Replace the static class reference:

```csharp
// Before:
if (e.Data.Contains(DataFormats.Text))

// After:
if (e.DataTransfer.Contains(DataFormat.Text))
```

**6b. `DragEventArgs.Data` → `DragEventArgs.DataTransfer`**

The `Data` property on `DragEventArgs` is renamed to `DataTransfer` and now returns `IAsyncDataTransfer` instead of `IDataObject`. All access to drag event data must use the new property:

```csharp
// Before:
private void OnDrop(object? sender, DragEventArgs e)
{
    if (e.Data.Contains(DataFormats.Text))
    {
        var text = e.Data.Get(DataFormats.Text) as string;
    }
}

// After:
private async void OnDrop(object? sender, DragEventArgs e)
{
    if (e.DataTransfer.Contains(DataFormat.Text))
    {
        var text = await e.DataTransfer.GetTextAsync();
    }
}
```

**6c. `DragDrop.DoDragDrop` → `DragDrop.DoDragDropAsync`**

Any call site that initiates a drag operation must be made async:

```csharp
// Before:
DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Move);

// After:
var dataTransfer = new DataTransfer();
dataTransfer.Items.Add(new DataTransferItem(DataFormat.Text, "payload"));
await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
```

**6d. `DataObject` → `DataTransfer` + `DataTransferItem`**

Anywhere a `DataObject` is constructed and populated, replace it with `DataTransfer` containing `DataTransferItem` instances. `DataObjectExtensions` is replaced by `AsyncDataTransferExtensions`.

### 7. Placeholder Text — Watermark → PlaceholderText

This is the most widespread change. The `Watermark` property is renamed to `PlaceholderText` on both `TextBox` and `NumericUpDown`.

**AXAML files** (search-and-replace `Watermark=` → `PlaceholderText=`):

| File | Occurrences |
|---|---|
| `Views/WebSocketDebugWindow.axaml` | 2 (ServerUrl, AccessToken TextBoxes) |
| `Views/HelpWindow.axaml` | 1 (search box) |
| `Views/ManagementToolWindowsViews/BusManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/MaintenanceManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/UserManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/TicketManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/RouteManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/JobManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/EmployeeManagementToolWindow.axaml` | 1 (search TextBox) |
| `Views/ManagementToolWindowsViews/RouteSchedulesManagementToolWindow.axaml` | 1 (CalendarDatePicker — verify if renamed in Avalonia 12) |

Note: `SalesManagementToolWindow.axaml`, `IncomeReportToolWindow.axaml`, and `SalesStatisticsToolWindow.axaml` contain no `Watermark` attributes and require no AXAML changes.

**C# ViewModel and View files** (replace `.Watermark =` with `.PlaceholderText =`):

| File | Occurrences | Controls |
|---|---|---|
| `Views/AuthWindow.axaml.cs` | 3 | username, password, TOTP code TextBoxes |
| `Views/OAuthLoginWindow.axaml.cs` | 1 | redirect URI TextBox |
| `ViewModels/UserManagementViewModel.cs` | 3 | login, password, leave-empty hint TextBoxes |
| `ViewModels/TicketManagementViewModel.cs` | 4 | 2× NumericUpDown (price, seat) in create dialog + 2× in edit dialog |
| `ViewModels/SalesManagementViewModel.cs` | 2 | buyer name, buyer phone TextBoxes |
| `ViewModels/RouteManagementViewModel.cs` | ~14 | 8 in create dialog (TextBox + NumericUpDown), 8 in edit dialog |
| `ViewModels/MaintenanceManagementViewModel.cs` | 6 | 3 in add dialog, 3 in edit dialog |
| `ViewModels/JobManagementViewModel.cs` | 4 | 2 in add dialog, 2 in edit dialog |
| `ViewModels/EmployeeManagementViewModel.cs` | 3 | surname, name, patronym TextBoxes |
| `ViewModels/BusManagementViewModel.cs` | ~14 | 7 in create dialog (TextBox + NumericUpDown), 7 in edit dialog |

### 8. Clipboard API — Full Migration

The clipboard API in Avalonia 12 moves away from direct `IClipboard` method calls toward extension methods and the new `DataTransfer`/`DataTransferItem` model.

**8a. Text read — `GetTextAsync` → `TryGetTextAsync`**

```csharp
// Before:
var text = await clipboard.GetTextAsync();

// After:
var text = await clipboard.TryGetTextAsync();
```

**8b. Text write — `SetTextAsync` via extension method**

`IClipboard.SetTextAsync` is no longer a direct interface method; it is now provided by `ClipboardExtensions`:

```csharp
// Before:
var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
if (clipboard != null)
    await clipboard.SetTextAsync(info.ToString());

// After (extension method form):
var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
if (clipboard != null)
    await ClipboardExtensions.SetTextAsync(clipboard, info.ToString());
```

This applies to `AboutWindow.axaml.cs` and any other file that calls `SetTextAsync` on an `IClipboard` reference.

**8c. Format enumeration — `GetFormatsAsync` → `GetDataFormatsAsync`**

```csharp
// Before:
var formats = await clipboard.GetFormatsAsync();

// After:
var formats = await ClipboardExtensions.GetDataFormatsAsync(clipboard);
```

**8d. Data object write — `SetDataObjectAsync(DataObject)` → `SetDataAsync(DataTransfer)`**

```csharp
// Before:
var dataObject = new DataObject();
dataObject.Set(DataFormats.Text, "payload");
await clipboard.SetDataObjectAsync(dataObject);

// After:
var dataTransfer = new DataTransfer();
dataTransfer.Items.Add(new DataTransferItem(DataFormat.Text, "payload"));
await clipboard.SetDataAsync(dataTransfer);
```

### 9. Compiled Bindings Default Change

In Avalonia 12, `AvaloniaUseCompiledBindingsByDefault` defaults to `true`. This project uses many `{Binding}` expressions without `x:DataType`, which would break under compiled bindings.

**Decision: Set `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>` in the main `.csproj`.**

Rationale: The project has a large number of reflection bindings (especially in dynamically-constructed controls in ViewModels). Enabling compiled bindings would require adding `x:DataType` to every AXAML file and converting all dynamic bindings to `ReflectionBinding`. This is a separate, larger refactoring effort that should not be bundled with the version migration.

**`IBinding` removal and `BindingBase` replacement**

In Avalonia 12, the `IBinding` interface is removed. The `Binding` class is kept as an alias for `ReflectionBinding`. Any code that declares a variable or parameter as `IBinding` must be updated to `BindingBase`:

```csharp
// Before:
IBinding binding = new Binding("Username", BindingMode.TwoWay);

// After:
BindingBase binding = new Binding("Username", BindingMode.TwoWay);
// or, for clarity:
BindingBase binding = new ReflectionBinding("Username") { Mode = BindingMode.TwoWay };
```

The `AuthWindow.axaml.cs` constructs `Binding` objects directly in C# (e.g., `[!TextBox.TextProperty] = new Binding("Username", BindingMode.TwoWay)`). With compiled bindings off, these continue to work as `ReflectionBinding` aliases. The grep scan of the codebase found no `IBinding`-typed variable declarations — all binding variables are declared inline — so no `IBinding` → `BindingBase` substitutions are required in this project. If any are found during compilation, they must be changed to `BindingBase`.

### 10. Third-Party Package Compatibility

The following packages need Avalonia 12-compatible versions. Exact version numbers must be confirmed against each package's NuGet release history at migration time.

| Package | Current Version | Migration Action |
|---|---|---|
| `Semi.Avalonia` | 11.0.7 | Upgrade to Avalonia 12-compatible release |
| `Semi.Avalonia.DataGrid` | 11.0.7 | Upgrade alongside Semi.Avalonia |
| `FluentAvaloniaUI` | 2.0.5 | Upgrade to Avalonia 12-compatible release |
| `Material.Icons.Avalonia` | 2.1.0 | Upgrade to Avalonia 12-compatible release |
| `SukiUI` | 6.0.0 | Upgrade to Avalonia 12-compatible release |
| `Avalonia.Labs.Controls` | 11.0.5 | Upgrade to Avalonia 12-compatible release |
| `Classic.Avalonia.Theme` | 11.2.0.7 | Upgrade or assess compatibility |
| `Classic.CommonControls.Avalonia` | 11.2.0.7 | Upgrade or assess compatibility |
| `ReDocking.Avalonia` | 1.0.3 | Upgrade or assess compatibility |
| `Dock.Model.Mvvm` | 11.0.0.5 | Upgrade to Avalonia 12-compatible release |
| `MessageBox.Avalonia` | 3.2.0 | Upgrade to Avalonia 12-compatible release |
| `WebView.Avalonia` | 11.0.0.1 | Upgrade to Avalonia 12-compatible release |
| `LiveChartsCore.SkiaSharpView.Avalonia` | 2.0.0-rc2 | Upgrade to stable Avalonia 12-compatible release |
| `FluentAvalonia.ProgressRing` | 1.69.2 | Upgrade or assess compatibility |

If a package has no Avalonia 12-compatible release at migration time, the options are:
1. Pin the package and test for runtime compatibility (some packages work across versions)
2. Remove the package and replace its usage with Avalonia 12 built-in controls
3. Fork/patch the package locally

Each incompatible package must be documented with its status and chosen resolution strategy.

### 11. Desktop Project — Program.cs

`Program.cs` in the Desktop project previously used `UseDesktopWebView()` from `WebView.Avalonia.Desktop`, which is now deprecated. In Avalonia 12, replace this with the official `Avalonia.Controls.WebView` package (version 12.0.0-preview2 or later). Remove `UseDesktopWebView()` and `AvaloniaWebViewBuilder.Initialize()` calls. Instead, add the `Avalonia.Controls.WebView` NuGet package and use the `NativeWebView` control in XAML (with `.Source` property for navigation, and `NavigationRequested`/`NavigationCompleted` event handlers for lifecycle hooks). The `UsePlatformDetect()` call remains valid in Avalonia 12 (HarfBuzz is included by default). No other changes to the AppBuilder chain are expected.

### 12. MAUI Integration (Optional)

MAUI integration requires creating a **brand new, separate MAUI host project** that references the existing Unity library as a project dependency. The existing `Desktop.csproj` and `Program.cs` are not touched. The Unity library itself (`BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj`) requires targeted changes to `App.axaml.cs` to support non-desktop lifetimes.

`Avalonia.Controls.Maui` replaces the .NET MAUI native rendering pipeline with Avalonia in full hosting mode. Controls are drawn by Avalonia's renderer (Skia) instead of platform-native widgets. The .NET MAUI binding system, XAML, styles, navigation, and Shell architecture are not replaced — only the rendering layer is swapped.

#### 12a. New Project Structure

```
BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui/
  ├── BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.csproj
  ├── MauiProgram.cs          (MAUI builder configuration)
  ├── App.cs                  (MAUI Application class — distinct from Avalonia App.axaml.cs)
  ├── Resources/
  │     ├── Fonts/            (MauiFont build action items)
  │     ├── Images/           (MauiImage build action items)
  │     └── Raw/              (MauiAsset build action items)
  └── Platforms/
        ├── Android/MainApplication.cs
        ├── iOS/AppDelegate.cs
        └── ...
```

The `.csproj` must include:
- MAUI workload target framework monikers (e.g., `net10.0-android`, `net10.0-ios`, `net10.0`)
- `<ProjectReference Include="../BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.csproj" />`
- Core package: `<PackageReference Include="Avalonia.Controls.Maui" Version="12.x.x" />`
- Optional packages as needed: `Avalonia.Controls.Maui.Essentials`, `Avalonia.Controls.Maui.Compatibility`, `Avalonia.Controls.Maui.SkiaSharp.Views`

#### 12b. Integration Mode Selection

`Avalonia.Controls.Maui` supports two mutually exclusive setup modes — use exactly one:

- **Full hosting** (`UseAvaloniaApp()`): MAUI handlers are replaced with Avalonia handlers. Avalonia controls are rendered through the MAUI shell via Skia. Use this for a fully Avalonia-driven UI.
- **Embedding** (`UseAvaloniaEmbedding<TApp>()`): Only `AvaloniaView` is registered inside a MAUI-native app. Use this for incremental adoption — hosting Avalonia content inside an existing MAUI-native layout.

#### 12c. MauiProgram.cs

All MAUI builder configuration lives in the new project's `MauiProgram.cs`. `UseMauiApp<T>` takes the MAUI `App.cs` class; `UseAvaloniaApp()` wires in the Avalonia `App` from the Unity library. `UseAvaloniaApp` must be called before any optional extension methods.

```csharp
using Avalonia.Controls.Maui;
using Avalonia.Controls.Maui.Essentials;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<MauiShellApp>()   // MAUI App.cs in this project
            .UseAvaloniaApp()             // full hosting — must come first
            .UseAvaloniaEssentials()      // optional: Essentials API support
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
            });
        return builder.Build();
    }
}
```

For Browser/WASM, pass `useSingleViewLifetime: true`:
```csharp
.UseAvaloniaApp(useSingleViewLifetime: true)
```

For embedding mode instead of full hosting:
```csharp
.UseAvaloniaEmbedding<App>()  // App = Avalonia App class from Unity library
```

#### 12d. Optional Extension Methods

| Method | Package | Purpose |
|---|---|---|
| `UseAvaloniaCompatibility()` | `Avalonia.Controls.Maui.Compatibility` | Handlers for deprecated MAUI controls (Frame→Border, ListView→CollectionView, etc.) |
| `UseAvaloniaEssentials()` | `Avalonia.Controls.Maui.Essentials` | Avalonia implementations of Essentials APIs (FilePicker, Screenshot, FileSystem, Preferences, etc.) |
| `UseAvaloniaSkiaSharp()` | `Avalonia.Controls.Maui.SkiaSharp.Views` | Handlers for `SKCanvasView`/`SKGLView`; pass `forceSoftwareRendering: true` for Browser/WASM |

#### 12e. Font, Image, and Asset Registration

**Fonts** — place in `Resources/Fonts/` with `MauiFont` build action. At build time they are converted to Avalonia embedded resources under `Assets/Fonts/`. Register aliases in `ConfigureFonts`:

```xml
<ItemGroup>
  <MauiFont Include="Resources/Fonts/*.ttf" />
</ItemGroup>
```

```csharp
.ConfigureFonts(fonts => fonts.AddFont("Inter-Regular.ttf", "InterRegular"))
```

**Images** — `MauiImage` build action → auto-resized, embedded under `Images/`, referenced by filename in AXAML.

**Raw assets** — `MauiAsset` build action → embedded as Avalonia resources, accessible via `FileSystem.OpenAppPackageFileAsync("file.json")` (requires `UseAvaloniaEssentials`) or `AssetLoader.Open(new Uri("avares://AssemblyName/file.json"))`.

#### 12f. MAUI App.cs vs Avalonia App.axaml.cs

The new project contains a MAUI `App.cs` (deriving from `Microsoft.Maui.Controls.Application`) that is the MAUI shell entry point. This is entirely separate from the Avalonia `App.axaml.cs` in the Unity library, which handles Avalonia's `OnFrameworkInitializationCompleted`. The two `App` classes coexist — MAUI's `App.cs` is the outer shell; Avalonia's `App.axaml.cs` handles the Avalonia lifecycle inside it.

#### 12g. App.axaml.cs Changes Required in Unity Library

Two changes are needed in `App.axaml.cs` when MAUI integration is adopted:

**1. `RegisterServices()` — guard the desktop WebView initializer**

`AvaloniaWebViewBuilder.Initialize(default)` is a desktop-only call. It must be wrapped so it does not execute on MAUI targets:

```csharp
public override void RegisterServices()
{
    base.RegisterServices();
#if DESKTOP
    AvaloniaWebViewBuilder.Initialize(default);
    Log.Information("WebView.Avalonia initialized");
#endif
}
```

**2. `OnFrameworkInitializationCompleted()` — add Android lifetime branch**

The `IClassicDesktopStyleApplicationLifetime` branch contains the full desktop startup flow (`ShowSplashScreenAndInitialize`, `BackGroundWindow`, `LoginMethodSelectorWindow`, `MainWindow`, `UnderConstructionWindow`) — this is desktop-only and must remain entirely inside that branch. The `ISingleViewApplicationLifetime` branch already sets `MainView`, which is the correct hook for MAUI single-view targets (Browser/WASM, iOS). Add `IActivityApplicationLifetime` for Android:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Desktop-only: splash screen, multi-window management, etc.
        // (existing ShowSplashScreenAndInitialize flow — unchanged)
    }
    else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
    {
        // MAUI single-view (Browser/WASM, iOS, etc.)
        singleView.MainView = new MainView { DataContext = new MainViewModel() };
    }
    else if (ApplicationLifetime is IActivityApplicationLifetime activity)
    {
        // Android
        activity.MainActivity.SetContentView(new MainView { DataContext = new MainViewModel() });
    }
    base.OnFrameworkInitializationCompleted();
}
```

Note: `DisableAvaloniaDataAnnotationValidation()` is still present in `App.axaml.cs` and must be removed as part of Task 2 (Req 2) — this is independent of MAUI integration.

#### 12h. Custom Handler Registration

Custom handlers or overrides for built-in MAUI controls are registered via `ConfigureMauiHandlers` after `UseAvaloniaApp`. Registrations made after `UseAvaloniaApp` take precedence over the defaults:

```csharp
builder
    .UseAvaloniaApp()
    .ConfigureMauiHandlers(handlers =>
    {
        handlers.AddHandler<MyControl, MyControlHandler>();
    });
```

#### 12i. Lifecycle Events

Avalonia-specific lifecycle events are available via `ConfigureLifecycleEvents` with `AvaloniaLifecycle`. Currently raised events: `OnLaunching`, `OnLaunched`, `OnWindowCreated` (desktop only — not raised for single-view lifetimes):

```csharp
builder.ConfigureLifecycleEvents(events =>
{
    events.AddWindows(avalonia =>
    {
        avalonia.OnLaunched((app, args) => Log.Information("MAUI app launched"));
    });
});
```

#### 12j. Desktop-Specific API Incompatibilities

The following APIs used in the Unity library are desktop-only and incompatible with MAUI mobile/browser targets:

| API / Pattern | Location | Desktop | Mobile/Browser | Strategy |
|---|---|---|---|---|
| `ShowSplashScreenAndInitialize` | `App.axaml.cs` | Used | Not applicable | Already inside `IClassicDesktopStyleApplicationLifetime` branch — no change needed |
| `new BackGroundWindow()`, `new LoginMethodSelectorWindow()`, `new MainWindow()` | `App.axaml.cs` | Used | Not supported (multi-window) | Already inside desktop branch |
| `AvaloniaWebViewBuilder.Initialize(default)` | `App.axaml.cs` `RegisterServices` | Used | Not available | Wrap in `#if DESKTOP` |
| `Window.WindowDecorations` | Various Views | Supported | Not applicable | Already desktop-only views |
| `DragDrop.DoDragDropAsync` | `MainWindow.axaml.cs`, `CentralViewWindow.axaml.cs` | Supported | Not supported | Wrap in `#if DESKTOP` if targeting mobile |
| `BeginMoveDrag` | Various Views | Supported | Not supported | Wrap in `#if DESKTOP` if targeting mobile |

#### 12k. Solution File Update

The new MAUI project must be added to `SpacetimeDB-BRU-AVTOPARK-avtobusov.sln` so it participates in solution-level builds and is visible in the IDE.

#### 12l. Windows Note

The `net*-windows` WinUI path is not production-ready in the current `Avalonia.Controls.Maui` source. For Windows, continue using the base `net10.0` TFM with the existing `Avalonia.Desktop` entry point. The MAUI project should target `net10.0-android`, `net10.0-ios`, `net10.0` (generic desktop/browser) but not `net10.0-windows`.

---

## Data Models

This migration does not introduce new data models. The changes are purely at the API call level. The relevant "data" is the project file structure:

**Package reference model (conceptual):**
```
ProjectFile
  ├── TargetFramework: "net10.0"
  ├── AvaloniaUseCompiledBindingsByDefault: false
  └── PackageReferences[]
        ├── { Id: "Avalonia", Version: "12.x.x" }
        ├── { Id: "AvaloniaUI.DiagnosticsSupport", Version: "12.x.x", DebugOnly: true }
        └── ... (all third-party packages at Avalonia 12-compatible versions)
```

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: No Removed API References Remain

*For any* `.cs` or `.axaml` file in the Avalonia project, the file SHALL NOT contain any of the following removed or renamed API identifiers: `AttachDevTools`, `BindingPlugins.DataValidators`, `DataAnnotationsValidationPlugin`, `ExtendClientAreaChromeHints`, `SystemDecorations`, `DataFormats.`, `.Watermark =`, `Watermark="`, `UseFloatingWatermark`, `DragEventArgs.Data`, `DoDragDrop(`, `DataObject(`, `IBinding `.

This is a universal property over all project source files. A single occurrence of any forbidden identifier in any file constitutes a violation.

**Validates: Requirements 2.1, 3.1, 3.2, 3.3, 4.1–4.6, 5.1–5.5, 6.1, 6.2, 7.1–7.9**

### Property 2: No Direct IClipboard Method Calls Remain

*For any* `.cs` file in the Avalonia project, if the file obtains an `IClipboard` reference, it SHALL NOT call `SetTextAsync` or `GetTextAsync` directly on that reference — it SHALL use the `ClipboardExtensions` pattern instead.

**Validates: Requirements 8.1, 8.2**

### Property 3: All Package References Are Avalonia 12-Compatible

*For any* `PackageReference` in the project file whose `Include` attribute names an Avalonia-dependent package, the `Version` attribute SHALL specify a version that is compatible with Avalonia 12 (i.e., the package's major version number aligns with Avalonia 12, or the package has been verified to work with Avalonia 12).

**Validates: Requirements 10.1–10.13**

### Property 4: Compiled Bindings Setting Is Explicit

*For any* build of the project, the `AvaloniaUseCompiledBindingsByDefault` MSBuild property SHALL be explicitly set to either `true` or `false` in the project file, so the binding mode is never implicitly determined by the framework default.

**Validates: Requirement 9.1**

---

## Error Handling

### Package Incompatibility

If a third-party package has no Avalonia 12-compatible release, the migration must not block. The strategy is:

1. Attempt to build with the existing version — many packages are binary-compatible across minor Avalonia versions
2. If build fails, check the package's GitHub issues/releases for a pre-release Avalonia 12 build
3. If no compatible version exists, remove the package reference and stub out its usage with a `// TODO: replace after Avalonia 12 compatible version is released` comment
4. Document each such package in a migration notes section of the tasks file

### Binding Errors at Runtime

With `AvaloniaUseCompiledBindingsByDefault=false`, binding errors are runtime warnings rather than compile errors. The existing Serilog logging infrastructure will capture these. After migration, run the app and check the debug log for `[Binding]` error entries.

### WebView Compatibility

`WebView.Avalonia` wraps a native WebView component. If the Avalonia 12-compatible version changes the `AvaloniaWebViewBuilder.Initialize` API or the `UseDesktopWebView()` extension, `App.axaml.cs` and `Program.cs` must be updated accordingly. The `RegisterServices` override in `App.axaml.cs` is the correct place for `AvaloniaWebViewBuilder.Initialize`.

---

## Testing Strategy

This migration has no new business logic — correctness is defined entirely by the absence of old APIs and the presence of correct new ones. The testing strategy reflects this.

### Unit Tests (Example-Based)

These verify specific, concrete post-conditions:

- **Build test**: `dotnet build -c Release` produces exit code 0 with zero errors
- **Warning test**: Build output contains no warnings matching `CS0618` (obsolete) or `AVL*` (Avalonia-specific) warning codes related to migrated APIs
- **Project file test**: `.csproj` contains `<TargetFramework>net10.0</TargetFramework>` and `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>`
- **Diagnostics package test**: `.csproj` contains `AvaloniaUI.DiagnosticsSupport` and does NOT contain `Avalonia.Diagnostics`

### Property-Based Tests

Property-based tests validate universal properties across all files in the project. The recommended library for C# is **CsCheck** or **FsCheck**. Each test should run with a minimum of 100 iterations (though for file-scanning properties, the "iterations" are over all project files).

**Property Test 1: No Removed API References**
```
// Feature: avalonia-12-migration, Property 1: No removed API references remain
// For all source files in the project, none contain forbidden API identifiers
[Property]
void NoRemovedApiReferences(string[] projectFiles)
{
    var forbidden = new[] {
        "AttachDevTools(", "BindingPlugins.DataValidators",
        "DataAnnotationsValidationPlugin", "ExtendClientAreaChromeHints",
        "SystemDecorations", "DataFormats.", ".Watermark =", "Watermark=\"",
        "UseFloatingWatermark", "DragEventArgs.Data", "DoDragDrop(",
        "DataObject(", "IBinding "
    };
    foreach (var file in projectFiles)
    {
        var content = File.ReadAllText(file);
        foreach (var api in forbidden)
            Assert.DoesNotContain(api, content);
    }
}
```

**Property Test 2: No Direct IClipboard Calls**
```
// Feature: avalonia-12-migration, Property 2: No direct IClipboard method calls remain
// For all .cs files, IClipboard references use ClipboardExtensions pattern
[Property]
void NoDirectClipboardCalls(string[] csFiles)
{
    foreach (var file in csFiles)
    {
        var content = File.ReadAllText(file);
        if (content.Contains("IClipboard") || content.Contains(".Clipboard"))
            Assert.DoesNotMatch(
                @"\.SetTextAsync\(|\.GetTextAsync\(|\.GetFormatsAsync\(|\.SetDataObjectAsync\(",
                content);
    }
}
```

**Property Test 3: All Avalonia Packages Are Version 12**
```
// Feature: avalonia-12-migration, Property 3: All package references are Avalonia 12-compatible
// For all PackageReference elements with Avalonia-related package IDs, version starts with "12."
[Property]
void AllAvaloniaPackagesAreVersion12(XDocument csproj)
{
    var avaloniaPackages = csproj.Descendants("PackageReference")
        .Where(r => r.Attribute("Include")?.Value.StartsWith("Avalonia") == true);
    foreach (var pkg in avaloniaPackages)
    {
        var version = pkg.Attribute("Version")?.Value ?? "";
        Assert.StartsWith("12.", version);
    }
}
```

**Property Test 4: Compiled Bindings Setting Is Explicit**
```
// Feature: avalonia-12-migration, Property 4: Compiled bindings setting is explicit
// The project file explicitly declares AvaloniaUseCompiledBindingsByDefault
[Property]
void CompiledBindingsSettingIsExplicit(XDocument csproj)
{
    var setting = csproj.Descendants("AvaloniaUseCompiledBindingsByDefault").FirstOrDefault();
    Assert.NotNull(setting);
    Assert.True(setting.Value == "true" || setting.Value == "false");
}
```

### Manual Verification Checklist

After all automated tests pass, perform these manual checks:

1. Launch the app in Debug mode — verify DevTools opens with F12
2. Navigate through the login flow (both OAuth and traditional)
3. Open the main window and verify window chrome renders correctly (no OS title bar overlap)
4. Open CentralViewWindow and verify its custom chrome
5. Open AboutWindow and use the "Copy Info" button — verify clipboard write succeeds
6. Open a management tool window and verify placeholder text appears in search boxes
7. Attempt a drag-and-drop operation in MainWindow or CentralViewWindow
8. Verify SplashScreen, BackGroundWindow, and AuthWindow render without OS decorations