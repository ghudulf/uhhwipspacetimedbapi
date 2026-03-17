# Raw Assets

Place raw files here (JSON configs, data files, etc.).

The `.csproj` includes them via:
```xml
<MauiAsset Include="Resources/Raw/**" LogicalName="%(RecursiveDir)%(Filename)%(Extension)"/>
```

Access at runtime (requires UseAvaloniaEssentials):
```csharp
using var stream = await FileSystem.OpenAppPackageFileAsync("config.json");
```

Or via Avalonia's AssetLoader:
```csharp
using var stream = AssetLoader.Open(new Uri("avares://BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui/config.json"));
```
