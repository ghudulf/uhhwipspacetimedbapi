# Desktop packaging

The standalone Avalonia host supports self-contained, untrimmed publication. Keep symbols for debugging; reflection-bound XAML and WebView integrations must not be trimmed. Package identity is `com.bru.avtopark.ticketsales`. The existing styles and assets are reused.

## Linux / Flatpak

From the repository root with .NET 10:

```sh
packaging/flatpak/prepare.sh /absolute/fresh/package-directory linux-x64
cd /absolute/fresh/package-directory
flatpak-builder --user --install-deps-from=flathub --repo=repo build com.bru.avtopark.ticketsales.json
flatpak build-bundle repo BRU.Avtopark.flatpak com.bru.avtopark.ticketsales
```

Use `linux-arm64` on an aarch64 builder. Match Flatpak builder architecture to the selected .NET RID. The GNOME 49 runtime supplies the desktop/WebKit system libraries; .NET is included in the package. Dependency restore/publication happens before Flatpak's offline build. Install the runtime/SDK from a trusted Flatpak remote. The manifest grants network, display and graphics access, with no host-filesystem access. File selection/browser integration should use portals. Test the embedded OAuth WebView and the system browser with the target runtime before distributing. Linux sessions currently remain in memory; credentials are never written unprotected.

## Windows 10/11 / MSIX / App Installer

Requires PowerShell 7, .NET 10, and Windows SDK `makeappx.exe`/`signtool.exe` on PATH. Minimum supported packaging target is Windows 10 version 2004 (build 19041); Windows 11 is included. Provide your actual certificate subject and version; do not change the identity/publisher on an update.

```powershell
./packaging/windows/Build-Package.ps1 -Publisher 'CN=Your Organization' -Version 1.0.0.0 `
  -OutputDirectory C:/packages/bru-1.0.0 -Architecture x64 `
  -UpdateBaseUri https://downloads.example.org/bru/ -SigningThumbprint YOUR_CERTIFICATE_THUMBPRINT
```

Signing is optional for generating a reviewable artifact, required before distribution. The script validates the package with MakeAppx, verifies signatures when signing is requested, and generates an architecture-specific App Installer document when an HTTPS update directory is supplied. It does not install, upload or modify trust stores. Publish signed packages and the App Installer XML together; increase all matching version fields on updates. Authenticode timestamping and certificate lifecycle are distribution-pipeline responsibilities.

Both hosts put logs and WebView2 data in the per-user local application-data directory. The install directory may be read-only. Windows session storage uses CurrentUser DPAPI. WebView2 Evergreen runtime must be available on Windows; do not bundle an unofficial runtime.

## Validation gates

Native Flatpak sandbox execution, Windows MakeAppx/signing/install/update/uninstall, DPAPI, browser callbacks, accessibility and real Windows authentication require the relevant platform. Cross-compilation alone does not validate those flows. MAUI uses the shared client library but retains its own host/package targets; these desktop scripts package the Avalonia standalone host.

### Accessibility validation

Run `dotnet run --project tests/Client.Presentation` from the repository root.
`AccessibilityPolicyTests` scans every library `.axaml` file, including `Views/**`,
`Controls/**`, `App.axaml` and resources. It requires interactive names (explicit
`AutomationProperties.Name`/`LabeledBy`, or static text content/headers), named or
decorative (`AccessibilityView="Raw"`) images, live status/error announcements,
default/cancel dialog actions, and no application tab-stop suppression. Its rule
fixtures cover both accepted and rejected markup. There is no source allowlist.

`AccessibilityRuntimeTests` opens the login selector, QR display/device/approval/
devices, provider login, passkey management, Entra link, Windows link, central
view and generic modal dialog. A loopback-only synthetic API supplies capability,
profile, QR and session values; no real provider/browser/account is used. It walks
materialized visual trees with Fluent templates, checks resolved automation names
and peers, updates a bound action name, rejects empty bindings/nonfocusable actions,
and focuses enabled visible interactive controls, including populated item templates.
The only template exceptions are Fluent scrollbar repeat buttons and the
`PART_HorizontalScrollBar`/`PART_VerticalScrollBar` parts owned by a `ScrollViewer`.
A `ListBox` may delegate tab focus to its realized items; those items are still
individually named, checked and focused. No application controls/windows are exempt.

**Not verified headlessly:** contrast, visual rendering with the shipped themes,
native keyboard traversal/order, OS screen-reader announcements and mobile/native
automation integration. Before distribution, validate those on real supported
Windows/Linux/mobile hosts, including error/status updates, dialog Enter/Escape,
keyboard-only use, light/dark/high-contrast modes and scaling. Passing these tests
is not an accessibility-conformance certification.
