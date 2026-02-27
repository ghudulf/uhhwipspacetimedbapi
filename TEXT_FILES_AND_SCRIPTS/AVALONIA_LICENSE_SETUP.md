# Avalonia Accelerate License Setup

## Issue

The project uses `Avalonia.Controls.WebView` which requires an **Avalonia Accelerate** subscription license key.

Error message:
```
AvaloniaUI.Licensing error AVLIC0001: No valid AvaloniaUI subscription keys found. 
Please ensure the <AvaloniaUILicenseKey /> item contains a valid license key from the Avalonia Portal.
```

## Solution

You already have an active Avalonia Accelerate subscription (as shown in your portal screenshot). You just need to add your license key to the project.

### Steps to Add License Key:

1. **Get Your License Key:**
   - Go to https://portal.avaloniaui.net
   - Log in with your account
   - Navigate to "Account" section
   - Look for "License Keys" or "API Keys"
   - Copy your Avalonia Accelerate license key

2. **Add License Key to Project:**
   
   Open the file:
   ```
   BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop/BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj
   ```

   Find this section:
   ```xml
   <!-- Avalonia Accelerate License Key -->
   <!-- Get your license key from: https://portal.avaloniaui.net -->
   <ItemGroup>
     <AvaloniaUILicenseKey Include="YOUR_LICENSE_KEY_HERE" />
   </ItemGroup>
   ```

   Replace `YOUR_LICENSE_KEY_HERE` with your actual license key:
   ```xml
   <ItemGroup>
     <AvaloniaUILicenseKey Include="your-actual-license-key-from-portal" />
   </ItemGroup>
   ```

3. **Rebuild the Project:**
   ```bash
   dotnet clean
   dotnet build
   ```

## Alternative: Environment Variable

You can also set the license key as an environment variable instead of hardcoding it in the project file:

**Windows (PowerShell):**
```powershell
$env:AVALONIA_LICENSE_KEY = "your-license-key-here"
```

**Windows (CMD):**
```cmd
set AVALONIA_LICENSE_KEY=your-license-key-here
```

**Linux/macOS:**
```bash
export AVALONIA_LICENSE_KEY="your-license-key-here"
```

Then modify the project file to use the environment variable:
```xml
<ItemGroup>
  <AvaloniaUILicenseKey Include="$(AVALONIA_LICENSE_KEY)" />
</ItemGroup>
```

## Security Note

⚠️ **Important:** Do NOT commit your license key to version control!

Add this to your `.gitignore` if you're storing the key in a separate file:
```
**/avalonia.license
*.license.key
```

## Troubleshooting

If you still get the error after adding the license key:

1. **Verify the key is correct** - Copy it again from the portal
2. **Clean and rebuild:**
   ```bash
   dotnet clean
   dotnet restore
   dotnet build
   ```
3. **Check for spaces** - Make sure there are no extra spaces before/after the key
4. **Restart Visual Studio** - Sometimes the IDE needs to be restarted

## What is Avalonia Accelerate?

Avalonia Accelerate provides:
- **WebView** - Native web browser control (what we're using for OAuth)
- **Dev Tools** - Advanced debugging and inspection capabilities  
- **Visual Studio Extension** - Enhanced XAML editing experience

Your subscription is active until **Nov 8, 2026** (as shown in the portal).

## Need Help?

If you can't find your license key in the portal:
1. Check the "Subscriptions" page
2. Look for "Manage Subscription" button
3. Contact Avalonia support at https://avaloniaui.net/support

## References

- Avalonia Portal: https://portal.avaloniaui.net
- Avalonia Licensing Docs: https://docs.avaloniaui.net/docs/accelerate/licensing
- WebView Documentation: https://docs.avaloniaui.net/docs/controls/webview
