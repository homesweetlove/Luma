# Luma Browser Skill

Use this skill when working on or integrating with Luma's browser automation surface.

## Status

Developer preview. The repository now contains a transport-agnostic `AgentBrowserFacade`, but it does **not** expose a socket, HTTP server, MCP endpoint, or unattended remote-control channel. Do not claim that an external agent can drive a running Luma instance until a host adapter is explicitly configured.

## Design goal

Luma borrows the useful part of agent browsers — a small composable browser API — while preserving Luma's human-first execution policy.

Preferred primitives:

- `SnapshotAsync()` — structured interactive DOM snapshot.
- `NavigateAsync(url)` — HTTP(S) navigation only; embedded URL credentials are rejected.
- `ClickAsync(selector)` — requires Luma's click approval callback.
- `FillAsync(selector, value)` — uses the existing DOM safety checks and rejects obvious credential/card-like values.
- `WaitAsync(selector, condition, timeout)` — bounded DOM wait.
- `ExecuteBatchAsync(steps)` — composes multiple existing `MacroStep` operations into one bounded run.

## Hard constraints

Never bypass these safeguards when adding a transport:

1. The exact target WebView must still be the selected foreground tab.
2. DOM selectors must resolve uniquely and remain visible/enabled.
3. Sensitive fields stay blocked.
4. Clicks keep the caller approval callback.
5. Cancellation, operation budgets, time budgets, document identity checks and no-effect-retry rules remain in `MacroRuntime`.
6. An agent batch must not overwrite the user's recorded macro.
7. Credentials, full payment numbers, CVV/CVC, OTPs and API secrets must not be accepted as normal agent input.
8. CAPTCHA, passkeys, OTP and payment authentication remain human work.

## Integration pattern

Create a host adapter around `AgentBrowserFacade`; do not duplicate browser scripts in the adapter.

```text
Codex / Claude / MCP host
        ↓
future local adapter
        ↓
AgentBrowserFacade
        ↓
MacroRuntime + SnapshotService
        ↓
foreground gate + approval + DOM validation
        ↓
selected WebView2 tab
```

For complex tasks, prefer one validated batch over a long loop of tiny tool calls, but keep the batch small enough for a person to understand and cancel.

## Do not

- add a public unauthenticated loopback HTTP endpoint;
- import Chrome passwords or cookies into prompts;
- disable the existing foreground gate to imitate another agent browser;
- automatically approve clicks because a model requested them;
- turn external model text into executable macro JSON without validation.

See `docs/HOME_AND_AGENT.md`, `docs/ARCHITECTURE.md`, and `docs/SECURITY.md` before changing the execution boundary.
