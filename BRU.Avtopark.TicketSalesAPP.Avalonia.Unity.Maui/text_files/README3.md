# Images

Place image files here (`.png`, `.svg`, `.jpg`).

The `.csproj` includes them via:

```xml
<MauiImage Include="Resources/Images/*"/>
```

MAUI auto-resizes images for each platform density and embeds them.
Reference in AXAML by filename (without extension):

```xml
<Image Source="logo"/>
```
