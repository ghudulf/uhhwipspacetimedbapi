using System.Text.RegularExpressions;

internal static class LogHygieneTests
{
    private static readonly Regex Calls = new(@"\b(?:\w*[Ll]ogger|_?log|Log)\.(?:Log(?:Trace|Debug|Information|Warning|Error|Critical)|Verbose|Debug|Information|Info|Warning|Error|Fatal)\s*\(", RegexOptions.Compiled);
    private static readonly Regex Secret = new(@"password|secret|token|code|otp|jwt|nonce|signature|assertion|credential|auth(?:orization)?header", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Protected = new(@"(?:[\w.]*\.)?(?:DebugPayload\.(?:Json|Headers|Url|Binary|Fingerprint|Mask)|TrafficDiagnostics\.(?:Json|AuthJson|Serialize|Body|ReducerArguments)|SafeDiagnostics\.(?:Fingerprint|JsonShape))\(", RegexOptions.Compiled);
    private static readonly HashSet<string> SafePlaceholders = new(StringComparer.OrdinalIgnoreCase)
    { "TokenId", "TokenType", "StatusCode", "ErrorCode", "ErrorType", "ExceptionType", "CredentialFile", "_credentialFile", "HasPayload" };

    private static IEnumerable<(string Text, int Offset)> LogCalls(string source)
    {
        foreach (Match match in Calls.Matches(source))
        {
            var depth = 1; var quoted = false; var escaped = false; var end = match.Index + match.Length;
            for (; end < source.Length && depth > 0; end++)
            {
                var c = source[end];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == '(') depth++;
                else if (c == ')') depth--;
            }
            yield return (source[match.Index..end], match.Index);
        }
    }

    private static IEnumerable<string> Findings(string path, string source)
    {
        foreach (var (call, offset) in LogCalls(source))
        {
            var line = source[..offset].Count(c => c == '\n') + 1;
            foreach (Match placeholder in Regex.Matches(call, @"\{[@$]?(\w+)(?:[^}]*)\}"))
            {
                var name = placeholder.Groups[1].Value;
                if (Secret.IsMatch(name) && !SafePlaceholders.Contains(name) && !name.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase) && !Protected.IsMatch(call))
                    yield return $"{line}: credential-bearing log placeholder {name}";
            }
            var arguments = Regex.Replace(call, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            // Explicit safe derived diagnostics, not a blanket sanitizer exemption.
            foreach (Match fingerprint in Protected.Matches(arguments).Cast<Match>().Reverse())
            {
                var end = fingerprint.Index + fingerprint.Length; var depth = 1;
                while (end < arguments.Length && depth > 0)
                { if (arguments[end] == '(') depth++; else if (arguments[end] == ')') depth--; end++; }
                arguments = arguments[..fingerprint.Index] + "fingerprint" + arguments[end..];
            }
            arguments = Regex.Replace(arguments, @"(?:string\.)?IsNullOrEmpty\([^()]*\)", "present");
            arguments = Regex.Replace(arguments, @"\b[\w.]+\??\.(?:Length|Count|Keys)\b", "count");
            arguments = Regex.Replace(arguments, @"\btoken\??\.(?:Id|DatabaseId|Revision|Type|Status|CreationDate|ExpirationDate|RedemptionDate|AuthorizationId|ApplicationClientId|OpenIddictTokenId)\b", "metadata");
            arguments = Regex.Replace(arguments, @"\btoken\s*!=\s*null|\btoken\.Payload\s*!=\s*null", "present");
            arguments = Regex.Replace(arguments, @"\bjwt\.(?:ValidFrom|Header\.Alg)\b", "metadata");
            arguments = Regex.Replace(arguments, @"\b(?:code|result\.Code)\b", m => path.EndsWith("/Realtime/V2/ApiSocketSession.cs", StringComparison.Ordinal) ? "protocolOutcome" : m.Value);
            foreach (Match identifier in Regex.Matches(arguments, @"\b\w+\b"))
            {
                var name = identifier.Value;
                if (Secret.IsMatch(name) && name is not "LogInformation" and not "LogWarning" and not "LogError" && !SafePlaceholders.Contains(name))
                    yield return $"{line}: credential-bearing log argument {name}";
            }
            if (call.StartsWith("Log.Fatal", StringComparison.Ordinal) && !path.EndsWith("/Program.cs", StringComparison.Ordinal))
                yield return $"{line}: fatal severity outside process startup";
        }
    }

    public static void Run()
    {
        var findings = DatabaseAccessPolicyTests.Sources().SelectMany(path => Findings(path.Replace('\\', '/'), File.ReadAllText(path)).Select(finding => Path.GetRelativePath(DatabaseAccessPolicyTests.Root(), path) + ": " + finding)).ToArray();
        if (findings.Length != 0) throw new Exception("Log hygiene policy:\n" + string.Join("\n", findings));
        foreach (var sample in new[] { "_logger.LogInformation(\"Password {Password}\", password);", "customLogger.LogError(\"proof {Value}\", accessToken);", "Log.Debug(\"proof {Value}\", request.RefreshToken);", "logger.LogInformation(\"OTP {Code}\", verificationCode);", "logger.LogInformation(\"proof {Fingerprint} {Value}\", SafeDiagnostics.Fingerprint(proof), password);", "Log.Fatal(\"Request failed\");" })
            if (!Findings("/new-controller.cs", sample).Any()) throw new Exception("Log policy accepted negative fixture: " + sample);
        if (Findings("/new-controller.cs", "logger.LogWarning(\"Verification rejected; credential {CredentialFingerprint}\", SafeDiagnostics.Fingerprint(proof));").Any())
            throw new Exception("Log policy rejected a fingerprint/outcome");
        foreach (var sample in new[] { "Log.Debug(\"JSON {Body}\", DebugPayload.Json(raw));", "Log.Debug(\"Token {Token}\", DebugPayload.Json(token));", "Log.Debug(\"User {Username} Email {Email} State {State}\", username, email, phase);", "Log.Debug(\"JSON {Body}\", TrafficDiagnostics.AuthJson(raw));" })
            if (Findings("/new-controller.cs", sample).Any()) throw new Exception("Log policy rejected redactor/business data fixture: " + sample);
        if (!Findings("/new-controller.cs", "Log.Debug(\"JSON {Body} proof {Value}\", DebugPayload.Json(raw), accessToken);").Any()) throw new Exception("Redactor hid a separate raw credential argument");
        Console.WriteLine("PASS log hygiene policy: credentials require DebugPayload/TrafficDiagnostics protection; business identifiers allowed; mixed protected/raw credential fixtures rejected");
    }
}
