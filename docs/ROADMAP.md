# Roadmap

## Phase 1 — lightweight browser foundation ✅ / in progress

- [x] WinUI 3 shell
- [x] WebView2 persistent profile
- [x] multiple tabs
- [x] address/search bar
- [x] compact DOM snapshot
- [x] semantic macro recording
- [x] macro replay
- [x] foreground-only watch
- [x] local SQLite audit log
- [x] DPAPI vault primitive
- [x] approval policy primitive
- [x] provider/delegate interfaces
- [x] zero-token local intent router
- [ ] polish Safari-inspired visuals
- [ ] bookmarks / history UI
- [ ] downloads UI

## Phase 2 — self-healing automation

- [ ] stable element reference registry
- [ ] selector confidence scoring
- [ ] wait/assert/branch/loop macro steps
- [ ] macro serialization to SQLite
- [ ] macro naming and reusable templates
- [ ] record from current state instead of always full navigation
- [ ] self-healing hook when a selector fails
- [ ] site-specific learned skills
- [ ] screenshot fallback only when DOM reasoning fails

## Phase 3 — personal assistant tools

- [ ] address profiles
- [ ] login vault slots
- [ ] payment metadata + secure fill
- [ ] Windows Hello approval option
- [ ] email tool
- [ ] calendar tool
- [ ] contacts tool
- [ ] local files tool
- [ ] downloads organizer
- [ ] receipt/order confirmation capture

## Phase 4 — shopping and booking

- [ ] shopping search tool returning 3–5 candidates
- [ ] normalized price/shipping/size comparison
- [ ] "buy option N" workflow
- [ ] saved-address fill
- [ ] saved-card secure fill
- [ ] checkout approval card
- [ ] reservation workflow tool
- [ ] "watch until slot appears" workflow
- [ ] recorded follow-up flow after a watched slot appears
- [ ] retry/backoff policies

## Phase 5 — AI routing

- [ ] OpenAI provider adapter
- [ ] Anthropic provider adapter
- [ ] Gemini provider adapter
- [ ] OpenRouter adapter
- [ ] Ollama / local OpenAI-compatible adapter
- [ ] model router: Free First / Balanced / Best Quality
- [ ] visible Gemini delegate
- [ ] visible Claude delegate
- [ ] context redaction pipeline
- [ ] per-task provider policy

## Phase 6 — Dev Workspace

Loaded only when explicitly needed.

- [ ] Monaco editor
- [ ] PowerShell terminal
- [ ] Git status/diff
- [ ] repository clone/open
- [ ] preview server tab
- [ ] coding agent read/write/edit tools
- [ ] build/test runner
- [ ] approval before Git push
- [ ] durable task recovery

## Phase 7 — richer automation UX

- [ ] natural-language macro creation
- [ ] visual macro timeline
- [ ] drag/drop condition editor
- [ ] session replay
- [ ] automation history
- [ ] per-site permissions
- [ ] reusable skill library
- [ ] trusted-site policies

## Non-goals

- hidden cloud browser service
- CAPTCHA bypass
- 2FA/passkey bypass
- always-running background reservation bot
- always-on large local LLM
- coordinate-only macro engine
- storing CVV/CVC
