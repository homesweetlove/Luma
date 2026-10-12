<div align="center">

# Luma

### A lightweight, local-first AI agent browser for Windows

**Safari-inspired UI · WebView2 · semantic macros · foreground watch · local vault · free-first agent routing**

[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#requirements)
[![WinUI 3](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK-5E5CE6?style=for-the-badge)](#technology)
[![WebView2](https://img.shields.io/badge/WebView2-Chromium-0A84FF?style=for-the-badge&logo=microsoftedge&logoColor=white)](#technology)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#development)
[![Private](https://img.shields.io/badge/Use-Personal%20%2F%20Private-111111?style=for-the-badge)](#project-scope)

[![English](https://img.shields.io/badge/README-English-24292f?style=for-the-badge)](./README.md)
[![한국어](https://img.shields.io/badge/README-한국어-24292f?style=for-the-badge)](./README.ko.md)

</div>

---

## What is Luma?

Luma is a **personal AI browser** designed around one idea:

> Use it like a normal, clean browser most of the time. When you ask for something, let the browser finish the real work for you.

The long-term goal is broader than “AI chat inside a browser.” Luma is meant to become a personal execution layer that can combine:

- web browsing,
- reservation and availability watching,
- shopping research and checkout assistance,
- saved addresses and payment methods,
- email and calendar actions,
- local files,
- Git and coding tools,
- semantic macros,
- visible external AI delegates,
- and optional API/local models.

Luma is being built **for one private user**, not as a public SaaS product. That allows the design to optimize aggressively for personal workflows while still keeping credentials and payment data behind a proper local security boundary.

---

## Why this project exists

Most AI browser projects optimize for one of these environments:

1. a cloud browser,
2. a headless automation worker,
3. a large local agent stack,
4. an API-first product with ongoing model cost,
5. a full Chromium fork that is expensive to build and maintain.

Luma targets a different machine and usage pattern:

```text
Target machine
──────────────
Intel Core i5 13th Gen
8 GB RAM usable baseline
16 GB RAM recommended
Windows
Personal daily use
```

That changes the architecture.

Instead of running a large model for every click, Luma prefers:

```text
DOM / rule
   ↓
recorded macro
   ↓
compact snapshot
   ↓
small/on-demand intelligence
   ↓
visible external AI delegate
   ↓
paid API only when necessary
```

The browser should do most repetitive work without spending tokens at all.

---

## Current status

Luma is in an early functional foundation stage.

### Implemented now

- WinUI 3 Windows app shell
- persistent WebView2 browser profile
- multiple browser tabs
- address bar / search fallback
- back / forward / reload / home
- Safari-inspired compact layout direction
- AI/automation side panel
- compact DOM snapshot extraction
- semantic macro recording
- semantic macro replay
- foreground-only watch mode
- optional auto-click when a matching enabled element appears
- zero-token local intent router for simple commands
- local SQLite audit log
- Windows DPAPI-backed secure vault primitive
- sensitive-action approval policy primitive
- pluggable AI provider interface
- visible external-AI delegate interface
- GitHub Actions Windows build workflow

### Not implemented yet

- production-ready shopping agent
- actual payment autofill UI
- actual email/calendar connectors
- self-healing AI execution
- Gemini/Claude visible delegate implementation
- OpenAI/Claude/Gemini API adapters
- visual macro editor
- Dev Workspace / Monaco / PowerShell
- polished browser settings/history/bookmarks/downloads surfaces

See [ROADMAP.md](docs/ROADMAP.md) for the complete staged plan.

---

## Core idea: broad autonomy, not one-purpose automation

Luma should not have separate hard-coded apps called “Shopping Bot,” “Reservation Bot,” or “Coding Bot.”

Instead, one agent should compose capabilities depending on the goal.

Example:

```text
User: "Find sunscreen."

Luma
 ├─ searches multiple stores
 ├─ normalizes price / volume / shipping
 ├─ returns 3–5 useful choices
 └─ waits for selection

User: "Buy #1."

Luma
 ├─ opens the selected store
 ├─ uses existing login session
 ├─ selects option
 ├─ uses saved address if needed
 ├─ uses a saved payment slot if the site has no registered card
 ├─ reaches checkout confirmation
 └─ requests approval before the purchase boundary
```

Another example:

```text
User: "Watch this Naver reservation page and grab 19:00 if it opens."

Luma
 ├─ stays on the visible reservation tab
 ├─ checks the page at a small interval
 ├─ pauses when Luma loses foreground activation
 ├─ detects an enabled 19:00 control
 ├─ clicks it if Auto-click is enabled
 └─ continues with a recorded macro / approval flow later
```

Another future example:

```text
User: "Fix this repository and ask Gemini for a second opinion."

Luma
 ├─ opens Dev Workspace lazily
 ├─ reads files
 ├─ edits code
 ├─ runs tests
 ├─ opens a visible Gemini delegate
 ├─ sends sanitized context
 ├─ compares the result
 └─ asks before git push
```

---

## Free-first execution

Free usage is a first-class requirement.

Luma should not call an LLM for tasks a browser can solve deterministically.

### Execution priority

```text
1. Deterministic DOM / JavaScript
2. Existing semantic macro
3. Existing site skill
4. Compact DOM/accessibility snapshot
5. Optional tiny/on-demand local model
6. Visible web-AI delegate
7. Paid API provider
```

### Why this matters

A reservation page may be watched for a long time. Calling a model every few seconds would be slow, wasteful, and expensive.

Instead, the normal check should look like:

```text
Is Luma foreground-active?
  ↓ yes
Is the target tab selected and visible?
  ↓ yes
Find enabled interactive element containing "19:00"
  ↓ no
Wait
```

No model call is required.

A model only enters the loop when the deterministic flow breaks or interpretation is genuinely required.

---

## Foreground-only automation

This is intentional behavior, not a limitation to hide.

The personal reservation/watch use case is designed around **visible automation**.

Current rule:

- if Luma is the active window and the target tab is visible → the watch may run,
- if Luma loses activation → the watch effectively pauses,
- no hidden always-on reservation daemon is part of the current design.

This makes automation easier to understand and keeps the user aware of what is happening.

---

## Semantic macros

Luma records **meaningful DOM actions**, not mouse coordinates.

Bad macro:

```text
click x=821 y=417
```

Luma-style macro:

```text
Navigate → https://example.com/reservation
Click → button[aria-label="예약"]
Input → input[name="name"] = ...
```

This survives normal layout movement much better than coordinate automation.

### Recorder safety

The recorder intentionally ignores values for fields that appear to contain:

- passwords,
- credit/debit card data,
- CVC/CVV/security code,
- OTP / one-time codes.

Future secure macros will store **vault references**, not secret values.

Example:

```text
vault-fill → payment/personal-visa
```

---

## DOM snapshots instead of constant screenshots

Luma extracts a compact list of visible interactive elements.

Example:

```json
[
  {
    "ref": "@1",
    "tag": "button",
    "text": "18:30",
    "selector": "button:nth-of-type(2)",
    "disabled": true
  },
  {
    "ref": "@2",
    "tag": "button",
    "text": "19:00",
    "selector": "button:nth-of-type(3)",
    "disabled": false
  }
]
```

This is cheaper and faster than sending a full screenshot to a vision model on every step.

Vision can remain a fallback for:

- canvas-heavy pages,
- visually encoded controls,
- broken DOM semantics,
- complex layout interpretation.

---

## Local Vault

Credentials and personal information should remain local.

The first implementation uses **Windows DPAPI** with `CurrentUser` scope.

Encrypted blobs are stored below:

```text
%LOCALAPPDATA%\Luma\Vault
```

The logical key is hashed before it becomes a file name.

### Planned vault domains

```text
identity/personal
address/home
address/dorm
payment/personal-visa
login/naver-main
provider/openai
provider/anthropic
provider/gemini
```

### Payment rule

Luma may eventually fill a saved card locally, but the model itself should not receive the raw card number.

Preferred flow:

```text
Agent
  ↓
"use payment slot personal-visa"
  ↓
Local secure fill layer
  ↓
Payment form
```

The model sees metadata such as:

```text
Personal Visa •••• 1234
```

not the PAN.

**CVC/CVV is intentionally not persisted.**

---

## Approval policy

The architecture keeps a hard distinction between **observing** and **committing**.

### Default no-approval examples

- read page
- search
- navigate
- compare products
- download
- inspect code

### Default approval-required examples

- send email
- create/change calendar event
- confirm reservation
- purchase
- delete data
- execute shell
- git push

Because Luma is private/personal, future settings can allow trusted rules such as:

```text
trusted site + purchase < 30,000 KRW → auto-confirm
```

but the approval layer remains available in the architecture.

---

## Authentication philosophy

Luma should **not bypass** security challenges.

When a site requires:

- CAPTCHA,
- passkey,
- SMS OTP,
- banking/card app confirmation,
- biometric confirmation,
- device approval,

Luma should stop at the challenge, let the user complete it, and then continue.

```text
agent works
   ↓
auth challenge
   ↓
user completes challenge
   ↓
agent resumes
```

---

## AI providers and delegates

The repository already contains provider/delegate contracts so the browser core does not depend on one model vendor.

### Planned API/local providers

- OpenAI
- Anthropic
- Gemini
- OpenRouter
- Ollama
- LM Studio
- custom OpenAI-compatible endpoints

### Visible delegates

Visible delegates are conceptually different from API providers.

Examples:

- open Gemini in a visible tab/window,
- give it sanitized task context,
- receive a second opinion,
- return the result to Luma’s main planner.

A delegate must never receive raw vault secrets.

---

## External services vision

Future tools can expose capabilities such as:

```text
mail.send
calendar.create
contacts.lookup
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

The goal is tool composition, for example:

```text
"Prepare my trip."

search flights
   + search hotels
   + compare options
   + reserve chosen option
   + create calendar entries
   + collect confirmation email
   + save receipts
```

---

## Dev Workspace

Coding features are intentionally planned as **lazy-loaded** components.

On an 8 GB machine, Monaco, terminal, Git helpers and coding-agent state should not consume memory while the browser is being used normally.

Future flow:

```text
User asks for coding task
   ↓
load Dev Workspace
   ├─ Monaco
   ├─ PowerShell
   ├─ Git status / diff
   ├─ preview tab
   └─ coding tools
   ↓
finish task
   ↓
unload heavy components
```

---

## Technology

| Layer | Technology | Reason |
|---|---|---|
| UI | WinUI 3 | Native Windows UI, lower overhead than a full Electron browser shell |
| Browser | WebView2 | Uses installed Chromium-based Edge runtime; avoids bundling another Chromium |
| Language | C# / .NET 8 | Windows integration, async tooling, simple native services |
| Local DB | SQLite | Small, reliable audit/config/history storage |
| Secret protection | Windows DPAPI | Current-user protected local secrets |
| Automation | JavaScript + DOM | Fast and token-free for common tasks |
| AI integration | Provider interfaces | Avoid vendor lock-in |
| CI | GitHub Actions / Windows | Verifies the Windows build path |

As of October 2026, the current project targets the Windows App SDK 2.5 stable family through the NuGet package line.

---

## Project structure

```text
Luma/
├─ .github/
│  └─ workflows/
│     └─ build.yml
├─ docs/
│  ├─ ARCHITECTURE.md
│  ├─ SECURITY.md
│  └─ ROADMAP.md
├─ src/
│  └─ Luma/
│     ├─ Models/
│     │  └─ AutomationModels.cs
│     ├─ Services/
│     │  ├─ AiProviders.cs
│     │  ├─ ApprovalPolicy.cs
│     │  ├─ AuditLogService.cs
│     │  ├─ BrowserScripts.cs
│     │  ├─ ForegroundWatchService.cs
│     │  ├─ LocalIntentRouter.cs
│     │  ├─ MacroService.cs
│     │  ├─ SecureVaultService.cs
│     │  └─ SnapshotService.cs
│     ├─ App.xaml
│     ├─ App.xaml.cs
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ app.manifest
│     └─ Luma.csproj
├─ .editorconfig
├─ .gitignore
├─ Luma.sln
├─ README.md
└─ README.ko.md
```

---

## Requirements

Recommended development environment:

- Windows 10 1809 or newer
- x64 machine
- .NET 8 SDK
- Visual Studio 2022/2026 with Windows App SDK / WinUI tooling, or equivalent CLI toolchain
- Microsoft Edge WebView2 Runtime
- Windows App Runtime matching the restored Windows App SDK package line

Target usage profile:

```text
Minimum design target: 8 GB RAM
Recommended:          16 GB RAM
CPU target:           13th-gen Core i5 class or similar
GPU:                  no dedicated GPU required for core browsing/macros
```

A large local LLM is **not** required for the core browser.

---

## Development

Clone:

```powershell
git clone https://github.com/homesweetlove/Luma.git
cd Luma
```

Restore:

```powershell
dotnet restore .\src\Luma\Luma.csproj -p:Platform=x64
```

Build:

```powershell
dotnet build .\src\Luma\Luma.csproj -c Debug -p:Platform=x64
```

Release build:

```powershell
dotnet build .\src\Luma\Luma.csproj -c Release -p:Platform=x64
```

---

## Current usage

### Browser

Use the address field like a normal browser.

- full URL → navigate directly
- domain-like text → `https://` is added
- ordinary text → Google search

### Snapshot

Click **Snapshot** or type a local command such as:

```text
스냅샷
화면 읽어
snapshot
```

The sidebar reports the number of visible interactive elements and a short preview.

### Record a macro

Click **● Record**.

Use the page normally. Luma records supported semantic navigation/click/input actions.

Click **■ Stop** when finished.

### Replay

Click **Replay** or type:

```text
매크로 재생
replay
```

### Foreground Watch

Enter visible text, for example:

```text
19:00
```

Enable **Auto-click** if desired and click **Start watching**.

Luma checks only while the window remains active.

You can also use the current local command router:

```text
19:00 자리 생기면
watch 19:00
```

---

## Data locations

Current local data lives under:

```text
%LOCALAPPDATA%\Luma\
```

Important paths:

```text
WebView2Profile\   persistent browser/session profile
Vault\             DPAPI-protected secret blobs
luma.db            SQLite audit database
```

Do not commit these directories.

---

## Security notes

Please read [SECURITY.md](docs/SECURITY.md).

Important defaults:

- raw password values are not recorded into semantic macros,
- card/CVC/OTP-like input values are filtered at the recorder boundary,
- CVV/CVC is not intended to be stored,
- vault blobs use Windows DPAPI CurrentUser protection,
- external AI delegates must receive sanitized context only,
- irreversible actions retain an approval-policy boundary.

---

## Performance strategy

Luma is intentionally not designed like a giant always-on agent runtime.

### Always lightweight

- WinUI shell
- active WebView2 tabs
- tiny timer for watch mode
- SQLite only when writing

### Load only when needed

- screenshots
- external model requests
- local model
- coding editor
- PowerShell
- Git tooling
- preview servers

This is important for the 8 GB RAM target.

---

## Inspiration / reference projects

Luma is an original private project, but its architecture is informed by good ideas visible in open-source agent/browser projects including:

- BrowserOS — agent-friendly Chromium/browser architecture
- Echo — local Windows Chromium/CDP + MCP approach
- ego-lite — separate agent spaces and compact browser control concepts
- Nanobrowser — planner/navigator and local-first browser automation ideas
- Automa — user-friendly recorded browser workflow concepts
- Stagehand — self-healing / reusable automation philosophy
- OpenBrowser — persistent profiles, auth handoff, audit-oriented automation
- Microsoft Playwright MCP — structured browser control instead of screenshot-only loops
- PiDurableKit / pi-durable — durable tool/task concepts for future coding workflows

Luma does not aim to be a clone of any one of these projects.

---

## Project scope

This repository is private and intended for personal use.

Priorities are therefore:

1. useful personal automation,
2. low resource usage,
3. local privacy,
4. broad tool composability,
5. visible and understandable automation,
6. minimal recurring AI cost.

The project is **not** intended to become:

- a CAPTCHA bypass toolkit,
- a 2FA/passkey bypass system,
- a hidden background reservation bot,
- a credential extractor,
- a payment-security bypass tool,
- an always-on cloud scraping service.

---

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Security & Privacy](docs/SECURITY.md)
- [Roadmap](docs/ROADMAP.md)
- [한국어 README](README.ko.md)

---

<div align="center">

**Luma — browse normally, delegate when needed.**

</div>
