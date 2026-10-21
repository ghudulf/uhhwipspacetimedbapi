using System.Text.RegularExpressions;

internal static class DatabaseAccessPolicyTests
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !(File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "BRU-AVTOPARK-AspireAPI")))) directory = directory.Parent;
        return directory?.FullName ?? throw new Exception("Repository global.json not found");
    }

    internal static IEnumerable<string> Sources() => Directory.EnumerateFiles(Path.Combine(Root(), "BRU-AVTOPARK-AspireAPI"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin" or "module_bindings"));

    private static readonly Regex Comments = new("(?<literal>\"\"\"[\\s\\S]*?\"\"\"|@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\")|(?<comment>//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)", RegexOptions.Compiled);

    private static IEnumerable<string> Violations(string path, string source)
    {
        var code = Comments.Replace(source, match => match.Groups["comment"].Success ? "" : match.Value);
        if (Regex.IsMatch(code, @"\bGetConnection\s*\(")) yield return "raw SDK connection";
        foreach (Match call in Regex.Matches(code, @"\.Reducers\.(\w+)\s*\("))
        {
            // A dedicated startup-only owner sender cannot use the API identity's trust-gated
            // queue. Its own bounded/cancellable pump awaits the exact committed callback.
            if (path.EndsWith("/Implementations/DatabaseInstallationProvisioner.cs", StringComparison.Ordinal) && call.Groups[1].Value == "BootstrapInstallation") continue;
            yield return "uncommitted reducer dispatch: " + call.Value;
        }
        if (Regex.IsMatch(code, @"\.GetAwaiter\s*\(\s*\)\s*\.GetResult\s*\(")) yield return "sync-over-async GetResult";
        foreach (Match call in Regex.Matches(code, @"\b(\w+)\.Wait\s*\(\s*\)"))
        {
            // SemaphoreSlim disposal locking, not Task.Wait (separate lifecycle audit).
            if (path.EndsWith("/SignalRRealtimeEventBus.cs", StringComparison.Ordinal) && call.Groups[1].Value == "_disposalLock") continue;
            yield return "blocking Wait: " + call.Value;
        }
        foreach (Match read in Regex.Matches(code, @"\b(\w+)\.Result\b(?!\s*=)"))
        {
            // Non-task properties: cryptographic result, completed tuple projection and MVC
            // action result. These are not permission to block on any Task.Result.
            if (path.EndsWith("/WebAuthnService.cs", StringComparison.Ordinal) && read.Groups[1].Value == "verified" ||
                path.EndsWith("/CatalogMutationExecutor.cs", StringComparison.Ordinal) && read.Groups[1].Value == "committed" ||
                path.EndsWith("/ApiMutationEventFilter.cs", StringComparison.Ordinal) && read.Groups[1].Value == "executedContext") continue;
            yield return "blocking Result: " + read.Value;
        }
    }

    public static void Run()
    {
        var violations = Sources().SelectMany(path => Violations(path.Replace('\\', '/'), File.ReadAllText(path)).Select(message => Path.GetRelativePath(Root(), path) + ": " + message)).ToArray();
        if (violations.Length != 0) throw new Exception("Database access policy: " + string.Join("\n", violations));
        foreach (var sample in new[] { "db.GetConnection();", "var url = \"https://example.test\"; db.GetConnection();", "conn.Reducers.DeleteUser(id);", "task.GetAwaiter().GetResult();", "task.Wait();", "var x = task.Result;" })
            if (!Violations("/new-service.cs", sample).Any()) throw new Exception("Policy negative fixture accepted: " + sample);
        Console.WriteLine("PASS database access policy: no raw request connections/reducer dispatches/task blocking; one observed owner-bootstrap exception and explicit non-task property exclusions; negative fixtures rejected");
    }
}
