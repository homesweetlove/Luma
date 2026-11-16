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
var navigationRecorder = new MacroService();
navigationRecorder.Start();
foreach (var unsafeUrl in new[]
{
    "https://example.com/?token=secret", "https://example.com/#recovery",
    "https://bob:password@example.com/path", "javascript:alert(1)"
})
    if (navigationRecorder.CaptureNavigation(unsafeUrl))
        throw new Exception("Unsafe URL recorded in a macro.");
if (navigationRecorder.Steps.Count != 0 || !navigationRecorder.CaptureNavigation("https://example.com/public"))
    throw new Exception("Recording skipped a safe navigation or retained an unsafe URL.");
navigationRecorder.Stop();
if (!navigationRecorder.ExportJson().Contains("https://example.com/public"))
    throw new Exception("Safe navigation could not be exported.");
navigationRecorder.ReplaceSteps([new Luma.Models.MacroStep { Kind = "input", Selector = "#query", Value = "hello" }]);
try { navigationRecorder.ExportJson(); throw new Exception("Plaintext macro input exported."); }
catch (MacroExecutionException ex) when (ex.Code == "unsafe-export") { }
navigationRecorder.ReplaceSteps([new Luma.Models.MacroStep { Kind = "navigate", Url = "https://example.com/?session=hidden" }]);
try { navigationRecorder.ExportJson(); throw new Exception("Plaintext token-bearing URL exported."); }
catch (MacroExecutionException ex) when (ex.Code == "unsafe-export") { }
navigationRecorder.ReplaceSteps([new Luma.Models.MacroStep { Kind = "loop", Steps = [new() { Kind = "input", Selector = "#query", Value = "nested" }] }]);
try { navigationRecorder.ExportJson(); throw new Exception("Nested input exported."); }
catch (MacroExecutionException ex) when (ex.Code == "unsafe-export") { }
Console.WriteLine("PASS: recording skips token URLs; plaintext export refuses input values and nested/unsafe steps");

macro.Start();
if (macro.TryCaptureWebMessage("{\"source\":\"luma-macro\",\"kind\":\"input\",\"selector\":\"#unknown\",\"value\":\"secret\"}"))
    throw new Exception("Unapproved page input was recorded.");
if (macro.TryCaptureWebMessage("{\"source\":42}")) throw new Exception("Malformed message accepted.");
Console.WriteLine("PASS: macro JSON roundtrip, atomic refusal and untrusted bridge input rejection");

var originalForEditor = new MacroService();
originalForEditor.ImportJson("[{\"Kind\":\"wait\",\"Selector\":\"#existing\"}]");
var editor = new MacroEditorSession(originalForEditor.Steps);
var root = editor.AddRoot();
root.Kind = "if";
root.Selector = "#approved";
var parent = editor.Flatten().Last();
var child = editor.AddChild(parent);
child.Kind = "click";
child.Selector = "#continue";
var otherwise = editor.AddChild(editor.Flatten().First(item => ReferenceEquals(item.Step, root)), elseBranch: true);
otherwise.Kind = "wait";
otherwise.Selector = "#fallback";
if (editor.Flatten().Count != 4 || originalForEditor.Steps.Count != 1 ||
    !editor.Flatten().Any(item => item.Path.Contains("else", StringComparison.Ordinal)))
    throw new Exception("Visual macro editor did not preserve nested/else structure or detached edits.");
var edited = new MacroService();
edited.ReplaceSteps(editor.Steps);
if (edited.Steps.Count != 2 || edited.Steps[1].Steps.Count != 1 || edited.Steps[1].ElseSteps.Count != 1)
    throw new Exception("Visual editor nested-step output was not valid for MacroRuntime.");
Console.WriteLine("PASS: visual editor working-copy isolation and nested if/else validation");

var tempRoot = Path.Combine(Path.GetTempPath(), "luma-macro-library-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var library = new MacroLibraryService(tempRoot);
    await library.SaveAsync("연구 문헌 조회", originalForEditor);
    if ((await library.ListAsync()).Count != 1) throw new Exception("Named macro not listed.");
    var loaded = new MacroService();
    await library.LoadAsync("연구 문헌 조회", loaded);
    if (loaded.Steps.Count != originalForEditor.Steps.Count || loaded.ExportJson() != originalForEditor.ExportJson())
        throw new Exception("Named macro roundtrip failed.");
    foreach (var badName in new[] { "../unsafe", " ", "name\\other", "bad:name" })
    {
        try { await library.SaveAsync(badName, originalForEditor); throw new Exception("Unsafe macro name accepted."); }
        catch (ArgumentException) { }
    }
    var beforeCount = (await library.ListAsync()).Count;
    var sensitive = new MacroService();
    sensitive.ReplaceSteps([new Luma.Models.MacroStep { Kind = "input", Selector = "#search", Value = "private" }]);
    try { await library.SaveAsync("sensitive", sensitive); throw new Exception("Sensitive plaintext macro saved."); }
    catch (MacroExecutionException ex) when (ex.Code == "unsafe-export") { }
    if ((await library.ListAsync()).Count != beforeCount) throw new Exception("Sensitive macro left a library file.");
}
finally { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
Console.WriteLine("PASS: named macro save/load with Unicode names and plaintext safety");

var aiRequest = AiMacroPlanner.BuildRequest("Visit https://example.com/path?session=private#secret and wait for #ready user@example.com");
if (aiRequest.Contains("session=private", StringComparison.Ordinal) || aiRequest.Contains("user@example.com", StringComparison.Ordinal))
    throw new Exception("AI macro planning request leaked URL query or email.");
var plan = AiMacroPlanner.ValidateReply("```json\n[{\"Kind\":\"navigate\",\"Url\":\"https://example.com\"},{\"Kind\":\"wait\",\"Selector\":\"#ready\"}]\n```");
if (!plan.Contains("#ready", StringComparison.Ordinal)) throw new Exception("Safe AI plan could not be validated.");
foreach (var badPlan in new[]
{
    "Click here: [{\"Kind\":\"click\",\"Selector\":\"#buy\"}]",
    "[{\"Kind\":\"input\",\"Selector\":\"#search\",\"Value\":\"hello\"}]",
    "[{\"Kind\":\"navigate\",\"Url\":\"https://example.com/?api=hidden\"}]",
    "[{\"Kind\":\"click\",\"Selector\":\"#go\",\"AllowSelfHeal\":true}]",
    "[{\"Kind\":\"shell\"}]"
})
{
    try { AiMacroPlanner.ValidateReply(badPlan); throw new Exception("Unsafe AI macro plan accepted."); }
    catch (Exception ex) when (ex is ArgumentException or MacroExecutionException) { }
}
Console.WriteLine("PASS: reviewed AI macro JSON rejects prose, input, unsafe URLs and self-healing");

var now = DateTimeOffset.Parse("2026-10-12T09:00:00+09:00");
if (!WatchSchedule.TryParse("2026-10-12T10:00:00+09:00", "2026-10-12T12:00:00+09:00", now, out _, out _) ||
    WatchSchedule.TryParse("2026-10-12T10:00:00", "2026-10-12T12:00:00", now, out _, out _) ||
    WatchSchedule.TryParse("2026-10-12T10:00:00+09:00", "2026-10-14T12:00:00+09:00", now, out _, out _) ||
    WatchSchedule.TryParse("2026-10-11T10:00:00+09:00", "2026-10-11T12:00:00+09:00", now, out _, out _))
    throw new Exception("Watch timezone/expiration/budget validation failed.");
Console.WriteLine("PASS: explicit watch timezone, expiration and duration bounds");
