# Security & Privacy Model

Luma is a private, personal browser, but private use does not justify weak secret handling. The default architecture assumes the browser may eventually hold account sessions, addresses, payment metadata, provider credentials, and automation permissions.

## Core rule

**The model should not need to know a secret in order to use a secret.**

Instead of placing a password or card number in a prompt, the agent should request a capability:

```text
vault.fillLogin(account="naver-main")
vault.fillAddress(address="home")
vault.fillPayment(card="personal-visa")
```

A trusted local component performs the actual fill operation.

## Storage classes

| Data | Planned storage | Model visibility |
|---|---|---|
| Browser cookies/session | WebView2 local profile | No direct export by default |
| Password | DPAPI-protected vault | Never raw |
| Address | DPAPI-protected vault | Field-level release only |
| Card number | DPAPI-protected payment vault | Never raw |
| Card expiry | Protected payment metadata | Only when fill is approved |
| CVC/CVV | **Not persisted** | Never |
| OTP/2FA | Not persisted | Human input/handoff |
| API keys | DPAPI-protected provider vault | Provider adapter only |
| Macro definitions | Local files/SQLite | Allowed, but no secret values |
| Audit history | SQLite | Sanitized only |

## DPAPI boundary

The first implementation uses Windows DPAPI with `CurrentUser` scope. Encrypted blobs are therefore bound to the current Windows user context.

This is appropriate for the first personal-only Windows build because:

- no master password has to be stored by Luma,
- decryption is tied to the Windows account,
- encrypted files copied to another ordinary account are not directly useful,
- implementation is significantly lighter than embedding a full password-manager stack.

Future hardening can add Windows Hello consent for selected vault categories.

## Macro recorder safety

The injected recorder excludes values from fields matching password/payment/OTP patterns.

A macro can remember that a secure fill happened, but should store only a vault reference:

```json
{
  "kind": "vault-fill",
  "slot": "payment/personal-visa"
}
```

not:

```json
{
  "kind": "input",
  "value": "4111 1111 1111 1111"
}
```

## AI prompt boundary

Before sending context to an external provider or visible delegate, the future context builder should:

1. use DOM/accessibility text before screenshots,
2. redact input values marked sensitive,
3. remove cookies, authorization headers and tokens,
4. replace vault references with non-secret labels,
5. limit context to the active task,
6. log which provider received which non-secret context category.

## Approval boundary

Observation and commitment are separate.

High-impact actions should have an approval checkpoint available even if a personal policy later disables it for trusted cases.

Examples:

- booking confirmation,
- payment,
- sending a message,
- destructive changes,
- shell execution,
- publishing code.

## Authentication challenges

Luma must not attempt to bypass:

- CAPTCHA,
- passkeys,
- SMS OTP,
- card issuer challenge flows,
- biometric authentication,
- device approval.

The intended behavior is:

```text
agent works → authentication challenge appears → user completes challenge → agent resumes
```

## Payment design

The browser may eventually automate form filling and checkout navigation. The model should see only metadata such as:

```text
Personal Visa •••• 1234
```

The secure fill layer handles the real PAN locally.

CVV/CVC remains manual by design.

## Audit requirements

Audit logs may contain:

- action name,
- site/domain,
- timestamp,
- macro/tool name,
- approval result,
- non-sensitive status.

Audit logs must not contain:

- passwords,
- full card numbers,
- CVC/CVV,
- OTP,
- session cookies,
- bearer tokens,
- provider API keys,
- full sensitive form payloads.

## External AI delegates

Visible delegates such as a Gemini or Claude web tab are treated as untrusted external processors for secret-handling purposes.

They can receive:

- sanitized screenshots,
- selected source code,
- public page context,
- problem descriptions.

They must not receive raw vault material.

## Threats explicitly considered

- accidental secret leakage into prompts,
- accidental secret capture by macro recording,
- plaintext logs,
- local database inspection,
- an agent making an irreversible action without approval,
- automation continuing while the user thinks it is paused,
- stale selectors clicking the wrong target,
- copied vault files being inspected outside the Windows user account.

## Private repository note

The repository is private and intended for one user. This reduces distribution risk but does not change the storage model: code should be written as if the local profile can hold real credentials.
