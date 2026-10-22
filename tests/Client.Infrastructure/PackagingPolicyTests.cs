using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class PackagingPolicyTests
{
    private const string AppId = "com.bru.avtopark.ticketsales";
    private const string DesktopProject = "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop/BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.csproj";
    private const string MauiProject = "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui/BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.csproj";
    private static readonly string[] BroadGrants = ["--filesystem=host", "--filesystem=home", "--device=all", "--socket=session-bus", "--socket=system-bus", "--share=all"];
    // No exceptions. A future exception must specify the exact argument AND a deployment
    // justification here and in packaging/README.md. No file-wide or category-wide exemptions.
    private static readonly IReadOnlyDictionary<string, string> GrantAllowlist = new Dictionary<string, string>();
    private static readonly (string Rule, Regex Pattern)[] SecretPatterns =
    [
        ("private key", new(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----", RegexOptions.CultureInvariant)),
        ("JWT", new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant)),
        ("provider credential", new(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|xox[baprs]-[A-Za-z0-9-]{16,})\b", RegexOptions.CultureInvariant)),
        ("credential-bearing URL", new(@"https?://[^\s/<>""']+:[^\s/<>""']+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("literal bearer", new(@"\bBearer\s+[A-Za-z0-9._~-]{16,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("literal credential assignment", new(@"\b(?:access[_-]?token|refresh[_-]?token|client[_-]?secret|password|private[_-]?key|api[_-]?key|signing[_-]?password|token|secret)\b[""']?\s*[:=]\s*[""'](?<value>[^""'\r\n]+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("XML credential", new(@"<(?:AccessToken|RefreshToken|ClientSecret|Password|PrivateKey|ApiKey)\b[^>]*>(?<value>[^<\s][^<]*)</", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
    ];

    public static void Run()
    {
        var root = FindRoot();
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
        var desktop = XDocument.Parse(Read(DesktopProject));
        var maui = XDocument.Parse(Read(MauiProject));
        var meta = XDocument.Parse(Read($"packaging/flatpak/{AppId}.metainfo.xml"));
        var manifestTemplate = Read("packaging/windows/AppxManifest.xml.in");
        var installerTemplate = Read("packaging/windows/BRU.Avtopark.appinstaller.in");
        var manifest = XDocument.Parse(manifestTemplate);
        var installer = XDocument.Parse(installerTemplate);
        var version = Value(desktop, "ApplicationDisplayVersion");
        Check(Regex.IsMatch(version, @"^[1-9]\d*\.\d+\.\d+$"), "desktop display version must have three numeric components");
        Check(Value(desktop, "ApplicationId") == AppId && Value(maui, "ApplicationId") == AppId, "desktop/MAUI application identity drift");
        Check(Value(maui, "ApplicationDisplayVersion") == version, "desktop/MAUI release version drift");
        Check(Value(meta, "id") == AppId, "AppStream identity drift");
        Check(meta.Descendants("release").First().Attribute("version")?.Value == version, "AppStream latest release version drift");
        Check(Value(meta, "launchable") == AppId + ".desktop", "AppStream desktop-id drift");
        Check(meta.Descendants("launchable").Single().Attribute("type")?.Value == "desktop-id", "AppStream launchable type drift");
        Check(Value(meta, "project_license") == "EPL-2.0" && Read("LICENSE").StartsWith("Eclipse Public License - v 2.0", StringComparison.Ordinal), "project license must match repository EPL-2.0");
        Check(Value(meta, "metadata_license") == "CC0-1.0", "AppStream metadata license drift (separate from project license)");

        var desktopEntry = DesktopEntry(Read($"packaging/flatpak/{AppId}.desktop"));
        var prepare = Read("packaging/flatpak/prepare.sh");
        var flatpak = EmbeddedManifest(prepare);
        Check(flatpak["app-id"]?.GetValue<string>() == AppId, "Flatpak app-id drift");
        Check(desktopEntry["Type"] == "Application" && desktopEntry["Icon"] == AppId, "desktop entry type/icon identity drift");
        Check(desktopEntry["Exec"] == "bru-avtopark" && flatpak["command"]?.GetValue<string>() == desktopEntry["Exec"], "desktop Exec/Flatpak command drift");
        var buildCommands = flatpak["modules"]!.AsArray().SelectMany(m => m!["build-commands"]!.AsArray()).Select(c => c!.GetValue<string>()).ToArray();
        foreach (var installed in new[] { $"/app/share/applications/{AppId}.desktop", $"/app/share/metainfo/{AppId}.metainfo.xml", $"/app/share/icons/hicolor/scalable/apps/{AppId}.svg", "/app/bin/bru-avtopark" })
            Check(buildCommands.Any(c => c.EndsWith(installed, StringComparison.Ordinal)), "Flatpak install identity/launcher drift: " + installed);
        Check(prepare.Contains("exec /app/lib/bru-avtopark/BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop \"$@\"", StringComparison.Ordinal), "Flatpak launcher target drift");
        var grantViolations = GrantViolations(flatpak["finish-args"]!.AsArray().Select(a => a!.GetValue<string>())).ToArray();
        Check(grantViolations.Length == 0, string.Join("; ", grantViolations));

        var identity = manifest.Descendants().Single(e => e.Name.LocalName == "Identity");
        var main = installer.Descendants().Single(e => e.Name.LocalName == "MainPackage");
        foreach (var node in new[] { identity, main })
        {
            Check(node.Attribute("Name")?.Value == AppId, "Windows package identity drift");
            foreach (var (attribute, placeholder) in new[] { ("Version", "@VERSION@"), ("Publisher", "@PUBLISHER@"), ("ProcessorArchitecture", "@ARCHITECTURE@") })
                Check(node.Attribute(attribute)?.Value == placeholder, "Windows identity placeholder drift: " + attribute);
        }
        Check(installer.Root!.Attribute("Version")?.Value == "@VERSION@" && installer.Root.Attribute("Uri")?.Value == "@INSTALLER_URI@" && main.Attribute("Uri")?.Value == "@PACKAGE_URI@", "App Installer root/package placeholder drift");
        Check(manifest.Descendants().Single(e => e.Name.LocalName == "Application").Attribute("Executable")?.Value == "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop.exe", "MSIX executable drift");
        Check(Placeholders(manifestTemplate).SetEquals(["@PUBLISHER@", "@VERSION@", "@ARCHITECTURE@"]), "unexpected MSIX template placeholders");
        Check(Placeholders(installerTemplate).SetEquals(["@PUBLISHER@", "@VERSION@", "@ARCHITECTURE@", "@INSTALLER_URI@", "@PACKAGE_URI@"]), "unexpected App Installer placeholders");
        foreach (var architecture in new[] { "x64", "arm64" })
        {
            var renderedManifest = XDocument.Parse(Render(manifestTemplate, version, architecture));
            var renderedInstaller = XDocument.Parse(Render(installerTemplate, version, architecture));
            var renderedIdentity = renderedManifest.Descendants().Single(e => e.Name.LocalName == "Identity");
            var renderedMain = renderedInstaller.Descendants().Single(e => e.Name.LocalName == "MainPackage");
            Check(renderedIdentity.Attributes().All(a => renderedMain.Attribute(a.Name)?.Value == a.Value), "rendered MSIX/App Installer identity/version mismatch");
            Check(renderedInstaller.Root!.Attribute("Version")?.Value == version + ".0", "rendered Windows/display version mismatch");
        }

        var packagingFiles = Directory.GetFiles(Path.Combine(root, "packaging"), "*", SearchOption.AllDirectories);
        foreach (var path in packagingFiles)
        {
            Check(!new[] { ".pfx", ".p12", ".key", ".pem", ".snk" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase), "private-key/certificate container must not be packaged: " + Path.GetRelativePath(root, path));
            var rules = SecretViolations(Encoding.UTF8.GetString(File.ReadAllBytes(path))).ToArray();
            // Never echo a matched credential or line value in failure diagnostics.
            Check(rules.Length == 0, "possible packaged credential: " + Path.GetRelativePath(root, path) + " (" + string.Join(", ", rules) + ")");
        }
        Fixtures();
        Console.WriteLine($"PASS packaging policy: XML templates/rendering, desktop/MAUI/AppStream/Flatpak/MSIX identity/version/license consistency, least-privilege finish-args (no allowlist), {packagingFiles.Length} packaging files credential-free; negative fixtures");
    }

    private static IEnumerable<string> GrantViolations(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            var grant = arg.Split(':')[0];
            if ((BroadGrants.Contains(grant, StringComparer.Ordinal) || grant is "--filesystem=/" or "--filesystem=~") &&
                (!GrantAllowlist.TryGetValue(arg, out var reason) || string.IsNullOrWhiteSpace(reason)))
                yield return "over-broad Flatpak finish-arg: " + arg;
        }
    }
    private static IEnumerable<string> SecretViolations(string text)
    {
        foreach (var (rule, pattern) in SecretPatterns)
            foreach (Match match in pattern.Matches(text))
            {
                if (match.Groups["value"].Success)
                {
                    var value = match.Groups["value"].Value;
                    // Variables and explicit build placeholders are references, not stored secrets.
                    if (value.StartsWith('$') || Regex.IsMatch(value, @"^@[A-Z_]+@$")) continue;
                }
                yield return rule;
            }
    }
    private static JsonObject EmbeddedManifest(string script)
    {
        var matches = Regex.Matches(script, @"<<'MANIFEST'\r?\n(?<json>[\s\S]*?)\r?\nMANIFEST(?:\r?\n|$)");
        Check(matches.Count == 1, "expected one literal Flatpak manifest heredoc");
        return JsonNode.Parse(matches[0].Groups["json"].Value)!.AsObject();
    }
    private static Dictionary<string, string> DesktopEntry(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var inside = false;
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            if (line.StartsWith('[')) { inside = line == "[Desktop Entry]"; continue; }
            if (!inside) continue;
            var pair = line.Split('=', 2);
            Check(pair.Length == 2 && result.TryAdd(pair[0], pair[1]), "malformed/duplicate desktop entry key");
        }
        return result;
    }
    private static HashSet<string> Placeholders(string template) => Regex.Matches(template, "@[A-Z_]+@").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
    private static string Render(string template, string version, string architecture) => template.Replace("@PUBLISHER@", "CN=Synthetic &amp; Validation").Replace("@VERSION@", version + ".0")
        .Replace("@ARCHITECTURE@", architecture).Replace("@INSTALLER_URI@", "https://packages.example.invalid/BRU.appinstaller").Replace("@PACKAGE_URI@", "https://packages.example.invalid/BRU.msix");
    private static string Value(XDocument document, string name) => document.Descendants().Single(e => e.Name.LocalName == name).Value.Trim();
    private static void Fixtures()
    {
        Check(GrantViolations(BroadGrants).Count() == BroadGrants.Length, "broad-grant negative fixtures");
        Check(GrantViolations(["--filesystem=host:ro", "--filesystem=home:ro"]).Count() == 2, "read-only broad grants bypass policy");
        Check(!GrantViolations(["--share=network", "--device=dri", "--socket=wayland"]).Any(), "narrow-grant positive fixtures");
        foreach (var text in new[] { "-----BEGIN PRIVATE KEY-----", "-----BEGIN RSA PRIVATE KEY-----", "https://name:literal@packages.example.invalid", "access_token=\"literal\"", "client_secret: 'literal'", "token: 'literal'", "<Password>literal</Password>", "Bearer " + new string('x', 20), "ghp_" + new string('x', 36), "eyJabcdefghijk.abcdefghijk.abcdefghijk" })
            Check(SecretViolations(text).Any(), "credential negative fixture was not rejected");
        Check(!SecretViolations("password=\"$BuildPassword\" client_secret='@CLIENT_SECRET@'").Any(), "build variable/placeholder positive fixtures");
        try { XDocument.Parse("<Package><Identity></Package>"); throw new Exception("Malformed XML fixture accepted"); }
        catch (System.Xml.XmlException) { }
    }
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, DesktopProject))) return dir.FullName;
        throw new DirectoryNotFoundException("Cannot locate packaging source root.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Packaging policy: " + message); }
}
