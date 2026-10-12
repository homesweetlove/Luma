# Luma

**A private Windows browser with local automation and visible AI handoffs.**

[한국어](README.ko.md) · [Architecture](docs/ARCHITECTURE.md) · [Security](docs/SECURITY.md) · [Roadmap](docs/ROADMAP.md)

Luma combines a compact, Safari-inspired WinUI 3 shell with Microsoft Edge WebView2, bounded DOM macros, a foreground watch, and a typed local vault. It is intended for personal use on a Windows x64 machine. The design target is a **13th-generation Intel Core i5 with 8 GB RAM**, without a resident model or a second bundled Chromium distribution; this is **not a measured hardware requirement or performance guarantee**.

The current app provides deterministic tools and a manual Gemini/Claude handoff. It does not implement an autonomous shopping, booking, coding, email, or calendar agent.

## Available now

| Area | Implemented behavior |
| --- | --- |
| Browsing | Persistent WebView2 profile, multiple tabs, address/search box, back/forward/reload/home, popup requests opened as tabs, loading indicator |
| Shell | Compact tabs, icon buttons with accessible names and tooltips, light/dark/high-contrast resources, initially hidden 360 px agent sidebar |
| Page snapshot | Up to 500 visible interactive DOM elements; labels, selectors, and disabled state, with labels limited to 240 characters |
| Recording | Current-session clicks and navigation on the recording tab; starting a new recording replaces the previous steps |
| Macro execution | Validated JSON, `navigate`, `click`, `input`, `wait`, `retry`, `if`, and `loop`; bounded time/operations/nesting; explicit approval for each click |
| Foreground watch | Original-tab watch, exact normalized text matching, 3-second checks, bounded schedule, manual click confirmation, optional follow-up replay |
| Vault | Add/list/delete/fill typed login, address, and payment-metadata slots; DPAPI protection and optional Windows Credential Manager for login secrets |
| External AI | Native task review, visible Gemini/Claude tab, explicit Copy task, manual paste/send, selected-reply display |
| Local services | SQLite audit records; English/Korean rule-based local commands; provider interfaces without installed API adapters |
| Verification | Windows Release build and console behavioral harness; dependency lock files and locked restore in CI |

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+L | Focus the address box and select its text |
| Ctrl+T | Open a new tab |
| Ctrl+W | Close the selected tab |
| F5 | Reload the selected tab |

The agent button reveals the sidebar. Closing the final tab creates a new home tab. Empty addresses use the Google home page; text that is not recognized as a URL becomes a Google search.

## Setup and build

Use Windows x64, the **.NET 8 SDK**, Windows/WinUI build tooling, and the **WebView2 Evergreen Runtime**. The project targets `net8.0-windows10.0.19041.0` and declares a minimum platform version of `10.0.17763.0`. Check compatibility with the installed Windows App Runtime on the Windows version you use.

The app is an unpackaged WinUI application and is not Windows App SDK self-contained. Install the Windows App Runtime required by the pinned SDK, in addition to WebView2 Evergreen. A large local LLM and provider API keys are not required.

| Direct package | Pinned version |
| --- | --- |
| Microsoft.WindowsAppSDK | 2.5.1 |
| Microsoft.Web.WebView2 | 1.0.4258.31 |
| Microsoft.Data.Sqlite | 8.0.31 |
| System.Security.Cryptography.ProtectedData | 8.0.0 |

The WebView2 NuGet package version identifies the development API package; Evergreen supplies and updates the browser runtime separately.

From the repository root:

```powershell
dotnet restore src/Luma/Luma.csproj -p:Platform=x64 --locked-mode
dotnet build src/Luma/Luma.csproj -c Release -p:Platform=x64 --no-restore
```

Open `Luma.sln` in a Windows WinUI development environment to launch/debug the app, or launch the generated x64 Release executable after installing its runtime prerequisites.

Lock files are checked in. CI uses locked restore so dependency resolution cannot silently change. Intentional package updates require updating and reviewing the lock files.

## Everyday workflows

### Record, import, and replay a macro

1. Select the page you want to automate and open the sidebar.
2. Choose **Record**. The current URL is captured and the app records subsequent clicks/navigation for that recording tab.
3. Stop recording. Review the recorded steps; a new recording clears the current macro.
4. Choose **Replay** with Luma in the foreground and the same intended page selected. Each click requires a native confirmation.
5. Use **Cancel automation** to cancel replay, stop recording, and stop watching.

**Input recording is disabled by default.** `MacroService.InputCaptureValidator` must be explicitly supplied by the host, and Main does not supply it. The page bridge alone cannot authorize capture of an input value. Imported `input` steps are supported, but the live target is checked for sensitivity, visibility, enabled state, and uniqueness before execution.

**Import** accepts a pasted JSON array, validates the entire program, and replaces the current macro only after successful validation. **Export** displays JSON; **Save locally** writes `%LOCALAPPDATA%\Luma\macro.json`. Export saving uses the service's atomic save path. Ordinary imported input values and URLs can be personal data; exported macros are not encrypted. The app does not automatically reload a saved macro or maintain a named macro library.

See the [macro schema, limits, and nested example](docs/ARCHITECTURE.md#macro-programs). The example requires a controlled page with the stated selectors; it is not a ready-made reservation script.

### Watch one visible control

Enter the exact displayed text in **Foreground Watch**. Whitespace is normalized, but the service does not use substring or fuzzy matching. It requires exactly one eligible control, enabled and unobstructed in the viewport. Observation does not scroll or refresh the page.

- **Start watching** creates a 30-minute watch from now.
- **Schedule watch** requires full ISO timestamps with a timezone offset or `Z`, for example `2026-10-20T19:00:00+09:00` and `2026-10-20T19:30:00+09:00`. `WatchSchedule.TryParse` checks the format and parses the actual dates. Start must precede end, end must be in the future, and the interval must be at most 24 hours. Timestamps without a timezone are rejected.
- The watch stays attached to its original tab and origin. Switching tabs or leaving Luma pauses work; returning can resume within the schedule. Closing that tab or navigating it to another origin stops the watch.
- **Auto-click when found** still requires a native **Click once** confirmation. It does not authorize unattended clicking.
- **Replay macro when found** runs the current macro only after a confirmed watch click actually succeeds. A find without a click does not start replay.
- The app must remain open. There is no system wakeup, durable scheduler, or background reservation service.

The execution gate checks the actual foreground OS window handle, selected WebView, and loading state. Expiry is checked on active timer processing; reactivating an expired watch does not extend it.

### Use the local vault

Open the vault button, choose **Add slot**, and select a type:

| Slot | Stored data | Eligible autocomplete fields |
| --- | --- | --- |
| Login | Label, exact HTTPS origin, username; password in DPAPI or optional Credential Manager | `username`, `current-password` |
| Address | Name, address lines, city/region, postal code, country code, email, phone | `name`, `address-line1`, `address-line2`, `address-level2`, `address-level1`, `postal-code`, `country`, `email`, `tel` |
| Payment metadata | Brand, last four digits, holder, expiry month/year | `cc-name`, `cc-exp-month`, `cc-exp-year` |

Payment slots have **no full card number/PAN, CVV/CVC/security code, or PIN fields**. Brand/last-four identify a slot and are not injected into the page. Full card entry and payment authentication stay manual.

Choose **Fill selected** on the matching HTTPS page. Fill stops recording, checks the foreground/selected tab and document identity, and only uses exact, single-token autocomplete fields in the top-level document. Ambiguous, hidden, disabled, sensitive, or unsafe fields cause refusal. Compound tokens such as `shipping address-line1` are unsupported. Login requires a same-origin HTTPS POST form; detected forms for other supported fields are checked too.

Fill does not submit, click, or dispatch input/change events. Some controlled forms therefore may not register a filled value; review the form and complete it manually. Filling releases data to that site's page scripts. Exact origin matching does not make a site trustworthy.

### Ask a visible delegate

1. Select **Gemini** or **Claude**.
2. Enter a bounded task in the native dialog; do not include credentials or personal data.
3. Review the prepared prompt and choose **Open visible tab**.
4. Complete any sign-in manually. Choose **Copy task** only if you want the reviewed prompt placed on the clipboard.
5. Paste and send it yourself in the visible tab.
6. Select reply text and choose **Read selected reply**. Luma displays up to 16,000 selected characters as **untrusted text**.

No page snapshot, cookies, vault content, or automatic page context is attached. Prompt preparation rejects recognizable credential assignments and credential-bearing URLs before email redaction, handles HTTP(S) URLs case-insensitively, redacts email addresses and long numeric sequences, and strips URL query/fragment components. These rules are defensive heuristics, not comprehensive detection of personal information; inspect the prepared prompt.

`VisibleWebAiDelegate.FillComposerAsync` exists as a helper but the Main UI does **not** call it automatically. The app does not send prompts automatically, scrape an entire conversation, execute reply instructions, or turn replies into macros. Provider accounts, terms, usage limits, and possible charges remain the user's responsibility; visible handoff does not guarantee free AI service.

### Local commands

The command box uses simple English/Korean rules, without an LLM. Examples: `snapshot`, `record`, `stop recording`, `replay`, `watch 19:00`, `스냅샷`, `매크로 녹화`, `녹화 중지`, and `매크로 재생`. Unknown commands show guidance; they do not invoke an API provider or an autonomous planner.

## Architecture and Free-First execution

```text
WinUI 3 shell + shared WebView2 environment
  ├─ local commands → DOM snapshots / bounded macros / foreground watch
  ├─ user-approved local fill → typed DPAPI / Credential Manager vault
  ├─ reviewed task → visible Gemini / Claude tab → manual send
  └─ origins and action counts → local SQLite audit
```

Deterministic tools are the default: recording, replay, and watch do not call a model. Tabs share one environment; inactive tabs request suspension and a low memory target. The optional self-heal callback and provider contracts remain explicit extension points. No local LLM is kept resident, and no paid API fallback runs automatically. See [Architecture](docs/ARCHITECTURE.md) for the macro schema, execution limits, document guards, and nested examples.

## Local data

| Location | Contents |
| --- | --- |
| `%LOCALAPPDATA%\Luma\WebView2Profile` | Persistent browser profile, including site session state |
| `%LOCALAPPDATA%\Luma\luma.db` | SQLite audit records |
| `%LOCALAPPDATA%\Luma\Vault\Typed-v1\<guid>.vault` | DPAPI-encrypted typed records |
| Windows Credential Manager | Optional login secrets under a Luma profile namespace |
| `%LOCALAPPDATA%\Luma\macro.json` | Explicitly saved plaintext macro JSON |

There is no app-managed synchronization or automatic cloud vault backup. Browser profile data, audit metadata, exported macros, and explicitly copied delegate prompts require their own privacy care. DPAPI protection covers vault records, not every file under the data directory.

Existing browser profile and audit data are preserved. Legacy untyped `Vault/*.bin` files are not loaded, migrated, or deleted by the new typed vault. An empty typed slot list does not mean old files were removed. User-directed migration remains future work.

## Verification

Run the Windows x64 console harness using build followed by direct DLL execution:

```powershell
dotnet restore tests/Luma.Tests/Luma.Tests.csproj -p:Platform=x64 --locked-mode
dotnet build tests/Luma.Tests/Luma.Tests.csproj -c Release -p:Platform=x64 --no-restore
dotnet tests/Luma.Tests/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Luma.Tests.dll
```

This harness is not a `dotnet test` test-adapter project. Direct DLL execution avoids the platform-specific output-path mismatch of `dotnet run`.

Node **22** is used for DOM script regressions. Generate the vault scripts by setting the fixture directory before executing the DLL, then run both JavaScript suites:

```powershell
$env:LUMA_VAULT_TEST_SCRIPT_DIRECTORY = Join-Path $env:TEMP 'luma-dom-tests'
New-Item -ItemType Directory -Force $env:LUMA_VAULT_TEST_SCRIPT_DIRECTORY | Out-Null
dotnet tests/Luma.Tests/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Luma.Tests.dll
node tests/VaultAutofillTests.js $env:LUMA_VAULT_TEST_SCRIPT_DIRECTORY
node tests/ForegroundWatchTests.js
```

VaultTests emits the current production fill scripts into that directory. The JavaScript suites exercise generated vault scripts and production watch scripts in simulated DOM environments; they are not live browser/vendor tests. CI installs .NET 8 and Node 22, performs locked restore for app/tests, builds Release, and runs the DLL and JavaScript regression suites.

Coverage includes branches/loops, delayed DOM readiness, unsafe or ambiguous targets, approval and foreground gates, cancellation, single-flight execution, time/operation limits, no effect retries, explicit self-heal opt-in, typed vault validation, exact HTTPS origins, field allowlists, DPAPI tamper/cancellation checks, Credential Manager roundtrip, prompt privacy, JSON roundtrip/refusal, and unapproved bridge input rejection.

The current integration run reports a **Release build with zero warnings**, passing vault/Credential Manager, delegate-privacy, JSON, and schedule checks, and **15 macro-runtime scenarios, 17 vault JavaScript cases, and 35 watch production-script cases passing**. Macro and watch click approvals bind the observed source/document tokens and revalidate before dispatch. A live shell/DOM smoke check confirmed launch, sidebar rendering/toggling, Google navigation, a snapshot of 20 DOM elements, and Ctrl+T opening a tab. Native window capture showed a blank WebView surface despite available DOM/accessibility content; a composition-capture limitation is a possible, unconfirmed explanation.

These checks do not certify complete Gemini/Claude handoffs, reservation-site automation, or performance on an 8 GB machine. Hardware startup, memory, CPU, and battery benchmarks have not been collected.

## Boundaries and next work

Automation uses ordinary DOM selectors. It has no cross-origin iframe, shadow-DOM, canvas, CAPTCHA, passkey, OTP, or payment-challenge bypass. Human authentication remains required. DOM changes and site policies can prevent a macro, watch, or delegate from working.

No API/local model adapter, always-on model, automatic self-healing provider, visual macro editor, mail/calendar connector, Git/shell tool, or autonomous checkout/reservation planner is implemented. `IAiProvider`, the registry, the approval-policy enum, and the optional self-heal callback are extension points, not completed integrations.

See [Security](docs/SECURITY.md) for trust boundaries and [Roadmap](docs/ROADMAP.md) for clearly separated future work. This repository is for private/personal use; it is not a hosted service or a claim of production readiness.

The next priorities are real-site and native-focus validation, measured resource use on the target hardware, a named macro library/editor, and explicitly configured optional AI adapters. Mail/calendar and coding tools remain later, separately scoped work.
