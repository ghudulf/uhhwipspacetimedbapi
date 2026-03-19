#!/usr/bin/env pwsh
# Quick build script for .NET 9 projects only (excludes SpacetimeDB module)

Write-Information "Building .NET 9 projects only..." -InformationAction Continue

# Use absolute paths based on script location to work regardless of caller's working directory
$tempSln = Join-Path $PSScriptRoot "Net9Projects_$(([System.Guid]::NewGuid().ToString('N'))).sln"
$sourceSln = Join-Path $PSScriptRoot "SpacetimeDB-BRU-AVTOPARK-avtobusov.sln"
$stdbProject = Join-Path -Path $PSScriptRoot -ChildPath "server" -AdditionalChildPath "StdbModule.csproj"

try {
    # Copy original solution
    Copy-Item $sourceSln $tempSln -Force -ErrorAction Stop

    # Remove SpacetimeDB module
    $removeOutput = dotnet sln $tempSln remove $stdbProject 2>&1

    # Check if removal succeeded
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to remove server\StdbModule.csproj from solution"
        if ($removeOutput) {
            Write-Error "Error details: $removeOutput"
        }
        exit $LASTEXITCODE
    }

    # Build
    dotnet build $tempSln $args

    $exitCode = $LASTEXITCODE
    exit $exitCode
}
finally {
    # Always clean up temp solution file
    Remove-Item $tempSln -ErrorAction SilentlyContinue
}