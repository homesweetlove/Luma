# Glass Home and agent-control foundation

This document covers the lightweight dashboard and the agent-facing foundation added to Luma.

## Glass Home

Luma Home is served from a local WebView2 virtual host:

```text
https://luma.local/home.html
```

`HomeDashboardService` writes the page into `%LOCALAPPDATA%\Luma\Home` and maps only that folder through `SetVirtualHostNameToFolderMapping`. The page contains no remote scripts, fonts, images, weather, news, analytics or feeds.

The visual direction is intentionally soft rather than flashy: translucent cards, rounded geometry, static radial gradients, modest backdrop blur and short motion. `prefers-reduced-motion` disables the entrance animation. Heavy animated backgrounds are avoided because Luma targets an 8 GB Windows machine.

Home widgets currently include:

- editable local favorites;
- search/address entry;
- quick actions for a clean search tab, macro recording, foreground watch, Vault and the agent panel;
- recent site origins read from Luma's local SQLite audit log;
- a local quick note stored in the `luma.local` WebView2 origin;
- current macro/watch status and a stop action.

Favorites and the quick note use the persistent WebView2 profile. Recent items expose only origins already written by the existing audit log, not page content.

## Home bridge security

Home-to-native messages are accepted only when both conditions are true:

1. the active document is exactly the Luma Home virtual-host document; and
2. `WebMessageReceived.Source` resolves to the `https://luma.local` origin.

Unknown messages are ignored. The bridge offers only a small allowlist of UI actions. The dashboard is cosmetic and must never become an alternate path around `CanExecute`, Vault rules, or macro approval.

## Agent control: what changed

`AgentBrowserFacade` is a transport-agnostic wrapper around the existing automation primitives. It is intentionally **not** a network API.

The useful idea borrowed from dedicated agent browsers is composability: an external agent adapter should be able to submit one bounded browser program rather than spending tokens on dozens of command/observation round trips.

The facade exposes:

```text
SnapshotAsync
NavigateAsync
ClickAsync
FillAsync
WaitAsync
ExecuteBatchAsync
```

`ExecuteBatchAsync` creates a separate `MacroService` runner, validates all steps before the first effect, and reuses `MacroRuntime`. This means an agent batch does not replace the user's recorded macro.

## Safety boundary

The agent layer inherits, rather than replaces, the existing rules:

- selected foreground tab required;
- operation and duration limits;
- cancellation;
- unique visible enabled DOM target checks;
- sensitive-field refusal;
- document/source revalidation before effects;
- explicit click approval;
- no automatic retry after an effect has been dispatched.

`FillAsync` adds an early refusal for obvious credential/API-key and payment-card-like values. Full payment credentials, passkeys, OTPs and CAPTCHA remain outside the agent contract.

## Why there is no local HTTP server yet

A localhost endpoint is still a remote-control surface. Shipping one safely requires authentication, origin/process binding, lifecycle management, replay protection and a clear approval UX. Adding an unauthenticated `127.0.0.1` API simply to match another agent browser would weaken Luma's current model.

The next integration step should be a narrow local adapter — for example an authenticated named-pipe/MCP host — that calls `AgentBrowserFacade` without duplicating any browser execution logic.
