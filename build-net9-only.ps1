#!/usr/bin/env pwsh
# Quick build script for .NET 9 projects only (excludes SpacetimeDB module)

Write-Host "Building .NET 9 projects only..." -ForegroundColor Cyan

# Create a filtered solution
$tempSln = "Net9Projects.sln"

# Copy original solution
Copy-Item "SpacetimeDB-BRU-AVTOPARK-avtobusov.sln" $tempSln -Force

# Remove SpacetimeDB module
dotnet sln $tempSln remove server\StdbModule.csproj 2>$null | Out-Null

# Build
dotnet build $tempSln $args

$exitCode = $LASTEXITCODE

# Clean up
Remove-Item $tempSln -ErrorAction SilentlyContinue

exit $exitCode
