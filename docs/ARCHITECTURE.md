# Luma Architecture

Luma is a personal, Windows-first, local-first agent browser optimized for a machine with an Intel Core i5 13th-gen CPU and 8 GB RAM. The architecture deliberately avoids a bundled Chromium fork and avoids keeping a local LLM resident in memory.

## Design goals

1. **8 GB RAM must be usable.** Browser automation should normally be deterministic JavaScript/DOM work, not a permanent LLM loop.
2. **The browser remains visible.** Automation is designed around the browser the user is actively watching.
3. **Foreground-only automation is a first-class mode.** A watch can pause as soon as Luma loses foreground activation.
4. **Secrets stay local.** Passwords, payment data, addresses, provider keys, and session state must never be placed into ordinary agent prompts.
5. **Agents compose tools.** Shopping, booking, calendar, email, code, files, and macros are tools behind one agent runtime rather than separate products.
6. **Free-first execution.** Rules and macros run before any model. Models are escalation paths, not the default execution engine.

## High-level layout

```text
┌─────────────────────────────────────────────────────────────────┐
│ Luma WinUI 3 shell                                              │
│  Safari-inspired toolbar · tabs · sidebar · approval surfaces   │
├─────────────────────────────────────────────────────────────────┤
│ WebView2 runtime                                                │
│  persistent local profile · cookies · downloads · normal login  │
├────────────────────────┬────────────────────────────────────────┤
│ Deterministic engine   │ Agent layer                            │
│ DOM snapshot           │ Local intent router                    │
│ semantic click/fill    │ provider registry                      │
│ macro recorder/replay  │ visible external-AI delegates          │
│ foreground watch       │ future planning/self-healing           │
├────────────────────────┴────────────────────────────────────────┤
│ Local services                                                   │
│ DPAPI Vault · SQLite audit log · approval policy · preferences   │
├─────────────────────────────────────────────────────────────────┤
│ Optional lazy tools                                               │
│ Dev workspace · PowerShell · Git · Monaco · mail/calendar tools  │
└─────────────────────────────────────────────────────────────────┘
```

## Why WebView2

Luma uses the installed Microsoft Edge WebView2 runtime instead of packaging another Chromium distribution.

Benefits for this project:

- lower disk usage than a full Chromium fork,
- lower development and update burden,
- normal Chromium-compatible web behavior,
- a persistent local profile for real logins,
- direct JavaScript execution and browser events,
- support for DevTools-based expansion later,
- good fit for Windows-only personal software.

The trade-off is that Luma is intentionally Windows-specific in its first phase.

## Browser shell

`MainWindow.xaml` contains the first usable shell:

- back / forward / reload / home,
- omnibox-style address/search box,
- TabView-based multiple tabs,
- compact agent sidebar,
- semantic macro recorder controls,
- foreground watch controls,
- local command box.

Each `TabViewItem` owns one `WebView2` instance. All tabs share one `CoreWebView2Environment` and one local profile directory under `%LOCALAPPDATA%\Luma\WebView2Profile`.

## Snapshot engine

`SnapshotService` executes `BrowserScripts.SnapshotScript` in the active page.

The script extracts only visible interactive elements and returns a compact structure:

```json
{
  "ref": "@12",
  "tag": "button",
  "role": "",
  "text": "19:00",
  "selector": "button:nth-of-type(3)",
  "disabled": false
}
```

This is deliberately cheaper than repeatedly sending full screenshots to a vision model.

Sensitive form fields are masked at the extraction boundary. Passwords and payment-like fields never return their value.

## Macro engine

The macro system is semantic rather than coordinate-based.

Recorder events are injected into each page with `AddScriptToExecuteOnDocumentCreatedAsync`. The script observes user interaction and reports actions through `window.chrome.webview.postMessage`.

Recorded step types currently include:

- `navigate`
- `click`
- `input`

A future version can add:

- `select`
- `waitFor`
- `assert`
- `branch`
- `loop`
- `download`
- `approval`
- `delegate`
- `tool`

### Sensitive-input rule

The recorder rejects values when a field looks like:

- password,
- card number,
- CVC/CVV/security code,
- OTP/one-time-code.

A macro should refer to a vault slot, for example `vault://payment/personal`, rather than storing the real secret in the macro definition.

## Foreground Watch

`ForegroundWatchService` is the first implementation of the requested visible-only monitor.

The current flow is:

```text
Luma active?
  └─ no → do nothing
  └─ yes
      active tab visible?
        └─ no → do nothing
        └─ yes
            find enabled interactive element containing target text
              └─ missing → wait for next tick
              └─ found → report, optionally click, stop watch
```

The first UI uses visible button/link text because it is generic across many sites. Later versions can let the agent convert natural language into a durable condition graph.

Example future intent:

```text
"10월 20일 19:00 자리 생기면 예약해"
```

Planner output:

```text
watch:
  visible_only: true
  target: reservation time 19:00
  date: 2026-10-20
  when_available:
    - click time
    - replay macro: reservation-details
    - approval: confirm-reservation
```

## Free-first agent routing

The intended decision ladder is:

```text
1. Existing deterministic rule
2. Existing site skill / recorded macro
3. DOM/accessibility snapshot reasoning
4. Tiny/on-demand local model (optional)
5. Visible web-AI delegate (optional)
6. Paid API provider (optional)
```

The first repository version implements steps 1-3 structurally and provides interfaces for the later layers.

## Provider abstraction

`IAiProvider` is intentionally small. Providers can later wrap:

- OpenAI API,
- Anthropic API,
- Gemini API,
- OpenRouter,
- Ollama,
- LM Studio,
- any OpenAI-compatible local/server endpoint.

`IExternalAiDelegate` is separate because a visible web-based AI worker has different semantics from an API provider. Delegates must never receive raw vault secrets.

## Local Vault

`SecureVaultService` uses Windows DPAPI with `DataProtectionScope.CurrentUser`.

The current implementation stores an opaque encrypted blob per logical key. File names are SHA-256 hashes of the logical key, so the vault directory does not directly expose site/account labels.

Planned vault domains:

```text
identity/
  personal
  work
address/
  home
  dorm
payment/
  personal-visa
login/
  example.com/main
provider/
  openai
  anthropic
  gemini
```

CVV/CVC is intentionally excluded from persistent storage.

## Audit log

`AuditLogService` creates `%LOCALAPPDATA%\Luma\luma.db` using SQLite.

The first schema records:

- timestamp,
- category,
- action,
- target,
- non-secret detail.

Raw passwords, payment values, OTP values, cookies, and provider tokens must never be written to the audit database.

## Approval policy

`ApprovalPolicy` separates observation from commitment.

Default no-approval examples:

- read page,
- navigate,
- download.

Default approval-required examples:

- send email,
- create calendar event,
- confirm reservation,
- purchase,
- delete data,
- Git push,
- local shell execution.

Because this is a personal browser, the policy can later support user-defined trust rules such as:

```text
trusted-site + purchase < 30,000 KRW → allow automatic confirmation
```

but the architecture keeps a visible approval boundary available.

## Future tool system

The long-term agent should not know implementation details for every service. It should see capabilities such as:

```text
browser.snapshot
browser.click
browser.fill
browser.navigate
macro.record
macro.replay
watch.start
vault.fillAddress
vault.fillPayment
mail.send
calendar.create
booking.confirm
purchase.confirm
files.read
files.write
git.diff
git.commit
git.push
shell.run
delegate.gemini
```

Each tool declares:

- permissions,
- whether approval is required,
- whether it is safe to replay,
- what data classes it can access,
- whether it needs foreground visibility.

## Dev workspace

Development features should be lazy-loaded because Luma targets 8 GB RAM.

When no coding task exists:

- Monaco is not loaded,
- PowerShell is not running,
- Git helpers are idle,
- no coding model is resident.

When a coding task starts, a future `DevWorkspace` can attach:

```text
workspace
├─ Monaco editor
├─ terminal (PowerShell)
├─ Git status/diff
├─ preview tab
└─ coding-agent tools
```

and unload it when the task ends.

## Resource strategy

### Always alive

- WinUI shell
- WebView2 tabs the user keeps open
- small SQLite connection only when writing
- lightweight timers for foreground watch

### On demand

- model provider calls
- screenshots
- local models
- dev workspace
- shell processes
- Git operations

### Explicitly avoided

- always-on large local LLM,
- bundled second Chromium engine,
- screenshot-per-step automation,
- coordinate-only macros,
- background reservation monitors hidden from the user.
