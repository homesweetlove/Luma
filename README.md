<div align="center">

# ✦ Luma

### Your browser. Your space. Your AI.

**A lightweight personal Windows browser with a soft glass home, local automation, and visible AI handoffs.**

<br />

[![Windows](https://img.shields.io/badge/Windows-x64-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#build)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#tech)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-5E5CE6?style=for-the-badge)](#tech)
[![WebView2](https://img.shields.io/badge/WebView2-Edge-0A84FF?style=for-the-badge&logo=microsoftedge&logoColor=white)](#tech)

[한국어](README.ko.md) · [Architecture](docs/ARCHITECTURE.md) · [Security](docs/SECURITY.md) · [Roadmap](docs/ROADMAP.md) · [Glass Home & Agent](docs/HOME_AND_AGENT.md)

<br />

**Browse normally · automate the repetitive parts · keep the person in control**

</div>

---

## A browser first

Luma is not an AI chat page pretending to be a browser. It is a WinUI 3 + WebView2 browser for personal Windows use, with bounded local automation layered on top.

The design target is a **13th-generation Intel Core i5 with 8 GB RAM**. Luma does not keep a large local model resident and does not bundle a second Chromium runtime.

```text
You
 │
 ▼
Luma
 ├─ WebView2 browsing
 ├─ Glass Home
 ├─ DOM snapshots
 ├─ semantic macros
 ├─ Foreground Watch
 ├─ typed local Vault
 └─ visible Claude / Gemini handoff
```

---

## 🫧 Glass Home

New tabs now open a local Luma dashboard instead of a plain Google page.

The home surface uses soft glass cards, rounded geometry, static gradient light, restrained blur and short motion. It stays local at `https://luma.local/home.html`; it does **not** load news, weather, ads, analytics or remote JavaScript.

| Widget | What it does |
|---|---|
| ⭐ Favorites | Editable shortcuts stored in the WebView2 profile |
| ⚡ Quick actions | Search, record, watch, Vault, agent panel |
| 🕘 Recent | Recent site origins from the local SQLite audit log |
| 📝 Quick note | A small local note stored in the Home origin |
| ◉ Automation | Shows macro/watch state and offers a stop action |
| 🕒 Today | Local greeting, date and time |

The home page is deliberately a **browser workspace**, not a portal homepage.

---

## What works today

<table>
<tr><td width="50%" valign="top">

### 🌐 Browsing

- persistent WebView2 profile
- multiple tabs
- address/search bar
- back / forward / reload / home
- popup-to-tab handling
- inactive-tab memory reduction

</td><td width="50%" valign="top">

### ⚙️ Local automation

- structured DOM snapshot
- click/navigation recording
- validated JSON macros
- `navigate`, `click`, `input`, `wait`
- `retry`, `if`, `loop`
- cancellation and execution budgets

</td></tr>
<tr><td valign="top">

### 👁 Foreground Watch

- exact visible text matching
- 3-second checks
- bounded schedule
- original tab/origin binding
- optional confirmed click
- optional follow-up macro replay

</td><td valign="top">

### 🔐 Local privacy

- DPAPI-protected typed Vault
- optional Windows Credential Manager for login secrets
- safe local form fill
- SQLite audit log
- full PAN/CVV/PIN intentionally unsupported

</td></tr>
</table>

---

## Human-first automation

Luma does not treat model output as permission.

```text
Observe
  ↓
Validate exact DOM target
  ↓
Check selected foreground tab
  ↓
Ask for click approval when required
  ↓
Revalidate document + target
  ↓
Dispatch once
```

Macro execution keeps hard limits on time, operation count, nesting, retries and selector ambiguity. Sensitive inputs are refused. A dispatched effect is never blindly retried.

---

## 🤖 Agent-ready, not agent-owned

Luma now contains a small transport-agnostic `AgentBrowserFacade` inspired by the useful part of dedicated agent browsers: **compose several browser operations into one validated run instead of burning tokens on endless tiny tool calls**.

The preview contract includes:

```text
SnapshotAsync()
NavigateAsync(url)
ClickAsync(selector)
FillAsync(selector, value)
WaitAsync(selector, condition)
ExecuteBatchAsync(steps)
```

Important: this is currently an **internal integration surface**, not an exposed localhost/MCP remote-control server. External agents cannot silently take over Luma just because the facade exists.

Any future Codex/Claude/MCP adapter must reuse the same foreground gate, DOM validation, click approval and cancellation rules.

See [`skills/luma-browser/SKILL.md`](skills/luma-browser/SKILL.md) and [Glass Home & Agent](docs/HOME_AND_AGENT.md).

---

## Visible AI handoff

Claude and Gemini delegation stays deliberately visible:

```text
bounded task
   ↓
privacy preparation
   ↓
user review
   ↓
visible provider tab
   ↓
manual paste / send
   ↓
selected reply imported as untrusted text
```

Luma does not automatically attach cookies, Vault content, hidden page context or a full conversation to a provider.

---

## Local Vault

The typed Vault supports:

- **Login** — exact HTTPS origin, username, protected password
- **Address** — name, address, region, postal code, email, phone
- **Payment metadata** — brand, last four, holder and expiry only

Luma intentionally has no fields for a full card number, CVV/CVC or PIN. Fill never submits a form and authentication remains manual.

---

## Architecture

```mermaid
flowchart LR
    U[You] --> UI[WinUI 3 shell]
    UI --> H[Glass Home]
    UI --> W[WebView2]
    W --> S[Snapshot]
    W --> M[MacroRuntime]
    W --> F[Foreground Watch]
    M --> G[Foreground + document gate]
    F --> G
    G --> A[Approval]
    A --> W
    UI --> V[DPAPI / Credential Manager Vault]
    UI --> D[SQLite audit]
    X[Future Codex / Claude / MCP adapter] --> B[AgentBrowserFacade]
    B --> M
    B --> S
```

---

## Tech

| Layer | Technology |
|---|---|
| UI | WinUI 3 / Windows App SDK |
| Runtime | .NET 8 |
| Browser | Microsoft Edge WebView2 |
| Local database | SQLite |
| Secret protection | Windows DPAPI / Credential Manager |
| Automation | DOM JavaScript bridge + bounded C# runtime |
| CI | GitHub Actions on `windows-latest` |
| DOM regression tests | Node.js 22 |

---

## Build

Requirements:

- Windows x64
- .NET 8 SDK
- Windows App SDK build tooling
- WebView2 Evergreen Runtime

```powershell
git clone https://github.com/homesweetlove/Luma.git
cd Luma

dotnet restore src/Luma/Luma.csproj -p:Platform=x64 --locked-mode
dotnet build src/Luma/Luma.csproj -c Release -p:Platform=x64 --no-restore
```

Luma targets `net8.0-windows10.0.19041.0`.

---

## Verification

The repository CI performs a Windows Release build plus behavioral, Vault and Foreground Watch regression checks on every push to `main` and every pull request targeting `main`.

For local verification:

```powershell
dotnet restore tests/Luma.Tests/Luma.Tests.csproj -p:Platform=x64 --locked-mode
dotnet build tests/Luma.Tests/Luma.Tests.csproj -c Release -p:Platform=x64 --no-restore
dotnet tests/Luma.Tests/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Luma.Tests.dll
node tests/ForegroundWatchTests.js
```

---

## Boundaries

Luma has no CAPTCHA, passkey, OTP or payment-challenge bypass. It does not currently expose an authenticated remote agent transport, autonomous checkout engine, mail/calendar connector, shell tool or hidden background reservation service.

That is intentional: new capability should be added **behind the existing safety boundary**, not around it.

---

<div align="center">

### ✦ Luma

**Browse · Automate · Delegate · Stay in control**

</div>
