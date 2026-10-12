# Roadmap / 로드맵

Luma is a private/personal Windows project. The baseline below is implemented; everything in the future-work sections is **unimplemented work**, not a promised release or an available tool. Priorities can change after real-site testing.

Luma는 개인용 Windows 프로젝트입니다. 현재 상태와 향후 작업을 분리합니다. 향후 항목은 미구현 작업이며 출시 일정이나 사용 가능한 기능을 약속하지 않습니다.

## Implemented baseline / 구현된 기반

| Area | Current state |
| --- | --- |
| Native browser | WinUI 3 compact shell, persistent WebView2 profile, tabs/navigation, loading indicator, shortcuts, accessible icons, collapsed agent sidebar |
| DOM inspection | Bounded interactive-element snapshots, native status preview |
| Recording | Current-session clicks/navigation; input capture defaults disabled and Main provides no allowlist validator |
| Macro programs | Validated JSON import/export, atomic local export save, navigate/click/input/wait/retry/if/loop, bounded interpreter and cancellation |
| Macro safety | Native click approvals bound to observed source/document identity, runtime/effect rechecks, fail-closed target checks, no dispatched-effect retry |
| Foreground watch | Original tab/origin, 3-second exact-text checks, immediate 30-minute window, timezone-aware ISO scheduling up to 24 hours, manually confirmed click, document-bound approval, optional replay after successful click |
| Local vault | Typed login/address/payment-metadata UI, DPAPI, optional Credential Manager login secret, exact HTTPS/autocomplete-restricted fill, no PAN/security-code fields |
| Visible AI | Gemini/Claude native review → visible tab → explicit copy → manual paste/send; selected untrusted reply display |
| Verification | Release build, console suites, generated vault JS and watch production-script regressions, locked CI restore, limited shell/DOM smoke |

The provider registry, sensitive-action policy, self-heal hook, and composer helper are extension points. Main has no API/local-model adapter, automatic self-heal callback, or automatic composer use. There is no autonomous shopping/booking agent.

공급자 레지스트리·승인 정책·self-heal 콜백·composer helper는 확장 지점입니다. 현재 화면에 API/로컬 모델, 자동 self-heal, 자동 composer 입력이 연결된 것은 아닙니다. 자율 쇼핑/예약 에이전트도 없습니다.

## Verification status / 검증 상태

The current integration reports a Release build with zero warnings and passing tests, including 15 macro-runtime scenarios, 17 generated vault JavaScript cases, and 35 watch production-script cases. Windows vault coverage includes Credential Manager roundtrip; other suites check delegate privacy, JSON validation, and schedule bounds.

Live smoke confirmed launch, sidebar rendering/toggle, Google navigation, a snapshot of 20 DOM elements, and Ctrl+T. Native capture showed a blank WebView surface despite DOM/accessibility content. Composition capture is a possible explanation requiring investigation.

Complete Gemini/Claude account flows, real reservation sites, long-running focus races, install/upgrade compatibility, and resource performance on the **i5 13th-generation/8 GB** target are not established by these checks.

경고 없는 Release 빌드와 테스트 통과, 제한된 실행/사이드바/Google/DOM/Ctrl+T 스모크가 확인되었습니다. WebView 캡처는 빈 화면으로 관찰되었고 원인은 미확정입니다. 공급자 계정 흐름·실제 예약 사이트·설치/업그레이드·하드웨어 성능은 별도 검증이 필요합니다.

## Priority 1: validate the current workflows / 우선순위 1: 현재 기능 검증

- [ ] Exercise native approvals, cancellation, focus loss, tab switching/closing, and same-URL reload/document changes in a real WebView2 session.
- [ ] Validate Gemini/Claude handoffs with manual authentication and selected replies; document account/site restrictions without implying automated vendor support.
- [ ] Test watch expiry, duplicate/obstructed controls, declined approvals, navigation during approval, and optional replay on controlled fixtures and selected real sites.
- [ ] Investigate WebView composition/capture behavior and provide a reliable visual-validation method.
- [ ] Test vault field refusal and framework-controlled forms against real pages; preserve manual submission and the restricted metadata policy.
- [ ] Confirm runtime installation and unpackaged startup on clean Windows x64 environments.
- [ ] Measure cold start, total WebView process working set, idle CPU, battery impact, and tab suspension/resume on the i5 13th-generation/8 GB target. Publish measured results before asserting budgets.

실제 WebView에서 승인·취소·포커스·문서 변경을 검증하고, 공급자 수동 전달과 감시를 테스트합니다. 캡처 원인을 조사하고 깨끗한 Windows 환경의 설치/실행과 실제 자원 사용량을 측정합니다. 측정 전 성능 수치를 약속하지 않습니다.

## Priority 2: improve local workflow clarity / 우선순위 2: 로컬 작업 사용성

- [ ] Named macro library, explicit load/save locations, and a visual nested-step editor with the same validation/budgets.
- [ ] A clear explanation of click/navigation-only recording; any input recording must use an explicit host-approved nonsensitive allowlist.
- [ ] Better watch/approval outcome messages, including declined or rejected clicks, without implying that Auto-click removes consent.
- [ ] Durable selector diagnostics and fixtures; snapshot references are not a stable registry today.
- [ ] Browser settings, bookmarks/history/downloads surfaces, and explicit data retention controls.
- [ ] Legacy vault migration: inspect retained untyped `Vault/*.bin`, obtain user-directed mapping to typed slots, and preserve the ban on PAN/security-code storage. No migration or deletion is implemented today.
- [ ] Optional Windows Hello/user-presence consent for vault access after compatibility testing.

현재 매크로는 세션 단위입니다. 이름별 라이브러리, 시각적 편집기, 명시적 load/save, 정확한 상태 안내와 데이터 보존 설정은 향후 작업입니다. 이전 `Vault/*.bin`은 유지되며 자동 변환/삭제가 없습니다. 마이그레이션에서도 카드번호/보안 코드 저장 금지는 유지합니다.

## Priority 3: optional intelligence / 우선순위 3: 선택적 AI

- [ ] Explicitly configured API/local-model adapters behind `IAiProvider`, with account/cost and data-sharing controls.
- [ ] A bounded planner that proposes reviewable steps rather than silently executing external text.
- [ ] Selector repair through the existing explicit opt-in self-heal callback, with document/target validation and fresh approval.
- [ ] A reviewed context-sharing builder; current delegates receive only the user-authored task.
- [ ] Broader privacy test cases for user-authored prompts without claiming comprehensive secret detection.

Keep deterministic DOM rules and macros as the default. No always-on model is required or planned as a prerequisite. There is no automatic free/paid model router today.

모델은 명시적으로 설정하는 선택 기능으로 검토합니다. 현재는 자동 라우터·계획기·selector 복구 모델·페이지 문맥 공유기가 없습니다. 결정적 DOM/매크로를 기본으로 유지하고 상주 모델을 필수로 삼지 않습니다.

## Priority 4: separately scoped tools / 우선순위 4: 별도 도구 확장

- [ ] Mail, calendar, contacts, and local-file integrations with concrete permissions and native approval boundaries.
- [ ] Product research/price comparison and reservation workflows after real-site and transaction-boundary validation.
- [ ] Human-reviewed address/payment-metadata assistance; full card numbers, security codes, and payment authentication remain outside the stored payment model.
- [ ] An optional on-demand coding workspace with editor, terminal, Git, and test tools, with resource measurement and approval for publication/destructive actions.
- [ ] Recovery/history for interrupted tasks only after the execution and data model are defined.

These are not current capabilities. An approval enum naming an action does not implement its API, account connection, or safety model.

메일·일정·파일·상품 비교·예약·코딩 workspace는 미구현입니다. 승인 enum에 액션 이름이 있어도 실제 API/계정 연결/안전 모델이 완성된 것은 아닙니다. 구현 전 권한과 데이터 경계를 개별적으로 정의해야 합니다.

## Scope boundaries / 범위 제한

Luma does not provide a hosted/cloud browser, hidden background reservation daemon, OS wakeup scheduler, always-on large LLM, authentication bypass, or arbitrary page-to-native command execution. It has no cross-origin iframe, shadow-DOM, or canvas bypass. CAPTCHA, passkeys, OTP, and payment challenges remain human tasks. Vault payment slots do not store PAN, CVV/CVC/security codes, or PINs.

There is no release-date promise, production-readiness certification, signed installer/update system, automatic vault migration, or hardware benchmark claim. See [Architecture](ARCHITECTURE.md) and [Security](SECURITY.md) for current implementation boundaries and [README](../README.md#verification) for reproducible test commands.

호스팅 브라우저·숨겨진 예약 서비스·OS 깨우기·상주 대형 모델·인증 우회·임의 네이티브 명령 호출은 제공하지 않습니다. 인증은 사람이 수행합니다. 출시 날짜, 프로덕션 인증, 서명된 설치/업데이트 시스템, 자동 마이그레이션, 측정하지 않은 성능을 주장하지 않습니다.
