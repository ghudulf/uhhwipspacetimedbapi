#!/usr/bin/env pwsh
# Build script that handles both .NET 9 projects and SpacetimeDB module

Write-Host "Building solution with mixed SDK versions..." -ForegroundColor Cyan
Write-Host ""

# Step 1: Build SpacetimeDB module separately (requires .NET 8 SDK with WASI workload)
Write-Host "Step 1: Building SpacetimeDB module (net8.0 + wasi-wasm)..." -ForegroundColor Yellow
Push-Location server
try {
    # Check if .NET 8 SDK is available
    $dotnet8 = dotnet --list-sdks | Select-String "8\."
    if (-not $dotnet8) {
        Write-Host "WARNING: .NET 8 SDK not found. Skipping SpacetimeDB module build." -ForegroundColor Red
        Write-Host "Install .NET 8 SDK and WASI workload with:" -ForegroundColor Yellow
        Write-Host "  dotnet workload install wasi-experimental" -ForegroundColor Yellow
    } else {
        # Try to build with .NET 8
        $env:DOTNET_ROLL_FORWARD = "Major"
        dotnet build StdbModule.csproj
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: SpacetimeDB module build failed. This may be due to missing WASI workload." -ForegroundColor Red
            Write-Host "Install with: dotnet workload install wasi-experimental" -ForegroundColor Yellow
        } else {
            Write-Host "✓ SpacetimeDB module built successfully" -ForegroundColor Green
        }
    }
} finally {
    Pop-Location
}

Write-Host ""

# Step 2: Build all .NET 9 projects (excluding SpacetimeDB module)
Write-Host "Step 2: Building .NET 9 projects..." -ForegroundColor Yellow

# Create a temporary solution without the SpacetimeDB module
$tempSln = "SpacetimeDB-BRU-AVTOPARK-avtobusov.temp.sln"
Copy-Item "SpacetimeDB-BRU-AVTOPARK-avtobusov.sln" $tempSln

# Remove SpacetimeDB module from temp solution
dotnet sln $tempSln remove server\StdbModule.csproj 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to remove SpacetimeDB module from temporary solution" -ForegroundColor Red
    Remove-Item $tempSln -ErrorAction SilentlyContinue
    exit 1
}

# Build the temp solution
dotnet build $tempSln --configuration Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ All .NET 9 projects built successfully" -ForegroundColor Green
} else {
    Write-Host "✗ Build failed for .NET 9 projects" -ForegroundColor Red
    Remove-Item $tempSln -ErrorAction SilentlyContinue
    exit 1
}

# Clean up temp solution
Remove-Item $tempSln -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Build completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Note: To build SpacetimeDB module separately, use:" -ForegroundColor Cyan
Write-Host "  cd server && dotnet build" -ForegroundColor White