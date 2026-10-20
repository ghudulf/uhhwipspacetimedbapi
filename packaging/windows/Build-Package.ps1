[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64',
    [uri]$UpdateBaseUri,
    [string]$SigningThumbprint
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'MSIX packaging requires Windows and the Windows SDK.' }
if ($Publisher -notmatch '^CN=') { throw 'Publisher must match the subject of your package signing certificate.' }
$parsedVersion = [version]$Version
if (@($parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $parsedVersion.Revision | Where-Object { $_ -gt 65535 }).Count) { throw 'MSIX version components must be <= 65535.' }
if ($parsedVersion.Major -eq 0) { throw 'MSIX major version must be nonzero.' }
if ($UpdateBaseUri -and ($UpdateBaseUri.Scheme -ne 'https' -or $UpdateBaseUri.Query -or $UpdateBaseUri.Fragment -or $UpdateBaseUri.UserInfo)) { throw 'Use an HTTPS update directory URL without credentials, query or fragment.' }
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$null = New-Item -ItemType Directory -Force $OutputDirectory
$outputRoot = (Resolve-Path $OutputDirectory).Path
$stage = Join-Path $outputRoot "stage-$Architecture"
if (Test-Path $stage) { throw 'Use a fresh output directory to avoid packaging stale files.' }
$null = New-Item -ItemType Directory $stage
$makeAppx = (Get-Command makeappx.exe -ErrorAction Stop).Source
$project = Join-Path $repoRoot 'BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop'
Push-Location $repoRoot
try {
    dotnet publish $project -c Release -r "win-$Architecture" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
} finally { Pop-Location }
$assets = Join-Path $stage 'Assets'
$null = New-Item -ItemType Directory -Force $assets
# Reuse the current application asset at the sizes required by the Windows shell.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::new((Join-Path $repoRoot 'BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/Assets/avalonia-logo.ico'))
$bitmap = $icon.ToBitmap()
try {
    foreach ($size in @(44, 50, 150)) {
        $scaled = [System.Drawing.Bitmap]::new($bitmap, [System.Drawing.Size]::new($size, $size))
        try { $scaled.Save((Join-Path $assets "Logo$size.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $scaled.Dispose() }
    }
} finally { $bitmap.Dispose(); $icon.Dispose() }
$xmlPublisher = [System.Security.SecurityElement]::Escape($Publisher)
@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
 <Identity Name="com.bru.avtopark.ticketsales" Publisher="$xmlPublisher" Version="$Version" ProcessorArchitecture="$Architecture" />
 <Properties><DisplayName>BRU Avtopark</DisplayName><PublisherDisplayName>BRU Avtopark</PublisherDisplayName><Logo>Assets\Logo50.png</Logo></Properties>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Resources><Resource Language="en-us" /><Resource Language="ru-ru" /></Resources>
 <Applications><Application Id="App" Executable="BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.exe" EntryPoint="Windows.FullTrustApplication">
 <uap:VisualElements DisplayName="BRU Avtopark" Description="Fleet and ticket sales management" BackgroundColor="transparent" Square150x150Logo="Assets\Logo150.png" Square44x44Logo="Assets\Logo44.png" />
 </Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@ | Set-Content -Encoding utf8 (Join-Path $stage 'AppxManifest.xml')
$packageName = "BRU.Avtopark-$Version-$Architecture.msix"
$packagePath = Join-Path $outputRoot $packageName
& $makeAppx pack /d $stage /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx validation/pack failed.' }
if ($SigningThumbprint) {
    & signtool.exe sign /fd SHA256 /sha1 $SigningThumbprint $packagePath
    if ($LASTEXITCODE -ne 0) { throw 'Package signing failed.' }
    & signtool.exe verify /pa $packagePath
    if ($LASTEXITCODE -ne 0) { throw 'Package signature verification failed.' }
}
if ($UpdateBaseUri) {
    $baseUri = [uri]::new($UpdateBaseUri.AbsoluteUri.TrimEnd('/') + '/')
    $packageUri = [System.Security.SecurityElement]::Escape([uri]::new($baseUri, $packageName).AbsoluteUri)
    $installerUri = [System.Security.SecurityElement]::Escape([uri]::new($baseUri, "BRU.Avtopark-$Architecture.appinstaller").AbsoluteUri)
@"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Uri="$installerUri" Version="$Version">
 <MainPackage Name="com.bru.avtopark.ticketsales" Publisher="$xmlPublisher" Version="$Version" ProcessorArchitecture="$Architecture" Uri="$packageUri" />
 <UpdateSettings><OnLaunch HoursBetweenUpdateChecks="24" ShowPrompt="true" /></UpdateSettings>
</AppInstaller>
"@ | Set-Content -Encoding utf8 (Join-Path $outputRoot "BRU.Avtopark-$Architecture.appinstaller")
}
Write-Host "Package created: $packagePath (no installation performed)"
