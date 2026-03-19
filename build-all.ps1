#!/usr/bin/env pwsh
# Build script that handles both .NET 9 projects and SpacetimeDB module

Write-Information "Building solution with mixed SDK versions..." -InformationAction Continue
Write-Information "" -InformationAction Continue

# Step 1: Build SpacetimeDB module separately (requires .NET 8 SDK with WASI workload)
Write-Information "Step 1: Building SpacetimeDB module (net8.0 + wasi-wasm)..." -InformationAction Continue
Push-Location (Join-Path $PSScriptRoot "server")
try {
    # Check if .NET 8 SDK is available
    $dotnet8 = dotnet --list-sdks | Select-String "8\."
    if (-not $dotnet8) {
        Write-Information "WARNING: .NET 8 SDK not found. Skipping SpacetimeDB module build." -InformationAction Continue
        Write-Information "Install .NET 8 SDK and WASI workload with:" -InformationAction Continue
        Write-Information "  dotnet workload install wasi-experimental" -InformationAction Continue
        exit 1
    } else {
        # Try to build with .NET 8
        $previousRollForward = $env:DOTNET_ROLL_FORWARD
        $env:DOTNET_ROLL_FORWARD = "Major"
        dotnet build StdbModule.csproj -c Release
        $stdbExitCode = $LASTEXITCODE
        $env:DOTNET_ROLL_FORWARD = $previousRollForward
        if ($stdbExitCode -ne 0) {
            Write-Information "WARNING: SpacetimeDB module build failed. This may be due to missing WASI workload." -InformationAction Continue
            Write-Information "Install with: dotnet workload install wasi-experimental" -InformationAction Continue
            exit $stdbExitCode
        } else {
            Write-Information "✓ SpacetimeDB module built successfully" -InformationAction Continue
        }
    }
} finally {
    Pop-Location
}

Write-Information "" -InformationAction Continue

# Step 2: Build all .NET 9 projects (excluding SpacetimeDB module)
Write-Information "Step 2: Building .NET 9 projects..." -InformationAction Continue

# Create a temporary solution without the SpacetimeDB module
$tempSln = Join-Path $PSScriptRoot "SpacetimeDB-BRU-AVTOPARK-avtobusov.temp.sln"
$sourceSln = Join-Path $PSScriptRoot "SpacetimeDB-BRU-AVTOPARK-avtobusov.sln"
$stdbModule = Join-Path $PSScriptRoot "server" "StdbModule.csproj"

# Remove existing temp solution if it exists
if (Test-Path $tempSln) {
    Remove-Item $tempSln -Force -ErrorAction Stop
}

# Copy with fail-fast behavior
Copy-Item $sourceSln $tempSln -Force -ErrorAction Stop

# Remove SpacetimeDB module from temp solution
dotnet sln $tempSln remove $stdbModule 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Information "✗ Failed to remove SpacetimeDB module from temporary solution" -InformationAction Continue
    Remove-Item $tempSln -ErrorAction SilentlyContinue
    exit 1
}

# Build the temp solution
dotnet build $tempSln --configuration Release

if ($LASTEXITCODE -eq 0) {
    Write-Information "✓ All .NET 9 projects built successfully" -InformationAction Continue
} else {
    Write-Information "✗ Build failed for .NET 9 projects" -InformationAction Continue
    Remove-Item $tempSln -ErrorAction SilentlyContinue
    exit 1
}

# Clean up temp solution
Remove-Item $tempSln -ErrorAction SilentlyContinue

Write-Information "" -InformationAction Continue
Write-Information "Build completed!" -InformationAction Continue
Write-Information "" -InformationAction Continue
Write-Information "Note: To build SpacetimeDB module separately, use:" -InformationAction Continue
Write-Information "  cd server && dotnet build" -InformationAction Continue