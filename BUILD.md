# Build Instructions

This solution contains projects targeting different .NET versions:
- **SpacetimeDB Module** (`server/`): .NET 8.0 with WASI-WASM runtime
- **All other projects**: .NET 9.0

## Quick Start

### Option 1: Build Everything (Recommended)
```powershell
.\build-all.ps1
```
This script builds the SpacetimeDB module first (if .NET 8 SDK is available), then builds all .NET 9 projects.

### Option 2: Build .NET 9 Projects Only
```powershell
.\build-net9-only.ps1
```
Use this when you only need to build the API, services, and Avalonia client.

### Option 3: Build from Visual Studio
Open `SpacetimeDB-BRU-AVTOPARK-avtobusov.sln` in Visual Studio. The SpacetimeDB module may show build errors if you don't have the WASI workload installed - you can exclude it from the build configuration.

## Prerequisites

### For .NET 9 Projects (Required)
- .NET 9.0 SDK or later

### For SpacetimeDB Module (Optional)
- .NET 8.0 SDK
- WASI workload: `dotnet workload install wasi-experimental`

## Manual Build Commands

### Build SpacetimeDB Module Only
```powershell
cd server
dotnet build StdbModule.csproj
```

### Build Specific .NET 9 Project
```powershell
cd BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService
dotnet build
```

### Build Entire Solution (May fail if WASI workload not installed)
```powershell
dotnet build SpacetimeDB-BRU-AVTOPARK-avtobusov.sln
```

## Troubleshooting

### Error: "The 'wasi-experimental' workload is not supported in .NET 9"
This means you're trying to build the SpacetimeDB module with .NET 9 SDK. Solutions:
1. Use `build-net9-only.ps1` to skip the SpacetimeDB module
2. Install .NET 8 SDK and WASI workload
3. Remove the SpacetimeDB module from the solution temporarily

### Error: "Project targets a different framework"
Make sure you're using the correct SDK version:
- SpacetimeDB module requires .NET 8 SDK
- Other projects require .NET 9 SDK

The `global.json` file ensures .NET 9 SDK is used by default, but the SpacetimeDB module explicitly requires .NET 8.
