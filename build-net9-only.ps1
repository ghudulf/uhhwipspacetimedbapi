#!/usr/bin/env pwsh
# Quick build script for .NET 9 projects only (excludes SpacetimeDB module)

Write-Information "Building .NET 9 projects only..." -InformationAction Continue

# Use absolute paths based on script location to work regardless of caller's working directory
$tempSln = Join-Path $PSScriptRoot "Net9Projects.sln"
$sourceSln = Join-Path $PSScriptRoot "SpacetimeDB-BRU-AVTOPARK-avtobusov.sln"
$stdbProject = Join-Path $PSScriptRoot "server\StdbModule.csproj"

# Copy original solution
Copy-Item $sourceSln $tempSln -Force

# Remove SpacetimeDB module
dotnet sln $tempSln remove $stdbProject 2>$null | Out-Null

# Check if removal succeeded
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to remove server\StdbModule.csproj from solution"
    Remove-Item $tempSln -ErrorAction SilentlyContinue
    exit $LASTEXITCODE
}

# Build
dotnet build $tempSln $args

$exitCode = $LASTEXITCODE

# Clean up
Remove-Item $tempSln -ErrorAction SilentlyContinue

exit $exitCode