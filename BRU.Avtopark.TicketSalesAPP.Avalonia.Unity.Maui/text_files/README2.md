# Fonts

Place `.ttf` or `.otf` font files here.

The `.csproj` includes them via:

```xml
<MauiFont Include="Resources/Fonts/*"/>
```

At build time, MAUI converts them to Avalonia embedded resources under `Assets/Fonts/`.
Register aliases in `MauiProgram.cs` → `ConfigureFonts`:

```csharp
fonts.AddFont("Inter-Regular.ttf", "InterRegular");
```

Then reference in AXAML:

```xml
<TextBlock FontFamily="{StaticResource InterRegular}" .../>
```
