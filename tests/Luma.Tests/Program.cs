using Luma.Automation.Tests;
using Luma.Tests;
using Luma.Services;

await AutomationRuntimeTests.RunAllAsync();
Console.WriteLine("PASS: macro branches, loops, delayed DOM, ambiguity, approval, gate, cancellation, budgets and self-heal");
await VaultTests.RunAsync();
Console.WriteLine("PASS: typed vault, exact origins, field allowlists, DPAPI tamper/cancellation and Credential Manager roundtrip");

var prompt = DelegatePrivacy.PreparePrompt("Compare options https://example.com/path?token=private#secret user@example.com 4111 1111 1111 1111");
if (prompt.Contains("private") || prompt.Contains("#secret") || prompt.Contains("user@example.com") || prompt.Contains("4111"))
    throw new Exception("Delegate privacy test failed.");
try { DelegatePrivacy.PreparePrompt("password=do-not-share"); throw new Exception("Credential prompt accepted."); }
catch (ArgumentException) { }
foreach (var url in new[] { "https://alice:demo-pass@127.0.0.1/path", "HTTPS://alice:demo-pass@example.com/path" })
{
    try { DelegatePrivacy.PreparePrompt(url); throw new Exception("URL credential accepted."); }
    catch (ArgumentException) { }
}
if (DelegatePrivacy.PreparePrompt("HTTPS://example.com/path?token=private#secret").Contains("private"))
    throw new Exception("Uppercase URL token leaked.");
Console.WriteLine("PASS: delegate prompt redaction and credential refusal");

var router = new LocalIntentRouter();
foreach (var command in new[] { "매크로 녹화 중지", "녹화 중지", "매크로 녹화 종료", "녹화 멈춰", "stop recording" })
    if (router.Parse(command).Kind != "stop-recording")
        throw new Exception("Recording stop command was misclassified: " + command);
foreach (var command in new[] { "매크로 녹화", "record macro", "record" })
    if (router.Parse(command).Kind != "record")
        throw new Exception("Recording start command was misclassified: " + command);
var preview = new Luma.Models.MacroStep { Kind = "input", Selector = "#example", Value = "private-sensitive-input" }.ToString();
if (preview.Contains("private-sensitive-input", StringComparison.Ordinal))
    throw new Exception("Sensitive macro input value leaked into UI preview.");
Console.WriteLine("PASS: local recording intent precedence and redacted input preview");

var macro = new MacroService();
macro.ImportJson("[{\"Kind\":\"wait\",\"Selector\":\"#seat\"}]");
if (macro.Steps.Count != 1 || !macro.ExportJson().Contains("#seat")) throw new Exception("Macro roundtrip failed.");
foreach (var bad in new[] { "null", "[{\"Kind\":\"shell\"}]", "[{\"Kind\":\"loop\",\"Steps\":null}]", "[{\"Kind\":\"navigate\",\"Url\":\"javascript:alert(1)\"}]" })
{
    try { macro.ImportJson(bad); throw new Exception("Unsafe macro imported."); }
    catch (MacroExecutionException) { }
    if (macro.Steps.Count != 1) throw new Exception("Failed import replaced the existing macro.");
}
macro.Start();
if (macro.TryCaptureWebMessage("{\"source\":\"luma-macro\",\"kind\":\"input\",\"selector\":\"#unknown\",\"value\":\"secret\"}"))
    throw new Exception("Unapproved page input was recorded.");
if (macro.TryCaptureWebMessage("{\"source\":42}")) throw new Exception("Malformed message accepted.");
Console.WriteLine("PASS: macro JSON roundtrip, atomic refusal and untrusted bridge input rejection");

var now = DateTimeOffset.Parse("2026-10-12T09:00:00+09:00");
if (!WatchSchedule.TryParse("2026-10-12T10:00:00+09:00", "2026-10-12T12:00:00+09:00", now, out _, out _) ||
    WatchSchedule.TryParse("2026-10-12T10:00:00", "2026-10-12T12:00:00", now, out _, out _) ||
    WatchSchedule.TryParse("2026-10-12T10:00:00+09:00", "2026-10-14T12:00:00+09:00", now, out _, out _) ||
    WatchSchedule.TryParse("2026-10-11T10:00:00+09:00", "2026-10-11T12:00:00+09:00", now, out _, out _))
    throw new Exception("Watch timezone/expiration/budget validation failed.");
Console.WriteLine("PASS: explicit watch timezone, expiration and duration bounds");
