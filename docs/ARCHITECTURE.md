# Architecture / 아키텍처

Luma is a private Windows x64 browser with bounded local automation. This document describes implemented code, including the current UI wiring. Extension contracts are identified separately from working integrations.

Luma는 개인용 Windows x64 브라우저입니다. 이 문서는 실제 구현과 현재 화면 연결을 설명하며, 확장용 인터페이스를 완성된 기능으로 취급하지 않습니다.

## Runtime and ownership / 실행 구조

The app uses C#/.NET 8, native WinUI 3, Windows App SDK **2.5.1**, and WebView2 Evergreen. Each browser tab owns a WebView2 control; all tabs share one `CoreWebView2Environment` and the persistent `%LOCALAPPDATA%\Luma\WebView2Profile` directory.

```text
MainWindow + MainWindow.Vault
  ├─ Native toolbar, tabs, sidebar, dialogs and keyboard accelerators
  ├─ WebView2 tabs → untrusted websites and persistent browser sessions
  ├─ SnapshotService → bounded DOM observations
  ├─ MacroService → validated programs → MacroRuntime → WebViewMacroHost
  ├─ WatchSchedule + ForegroundWatchService → original-tab observation/click
  ├─ SecureVaultService → DPAPI typed records + optional CredentialManagerStore
  │    └─ VaultAutofillService → restricted, user-initiated fill
  ├─ DelegatePrivacy + VisibleWebAiDelegate → reviewed manual web-AI handoff
  └─ AuditLogService → SQLite action metadata
```

`MainWindow.xaml.cs` owns tab lifecycle, navigation state, cancellation, watch state, visible handoffs, and native approvals. `MainWindow.Vault.cs` owns the vault dialogs and calls the fill service. `App.xaml` provides compact native-control styles and theme resources. The sidebar starts collapsed and opens at 360 px.

Tabs use shared profile/session state rather than independent disposable browser identities. Closed tabs dispose their controls. Inactive tabs request a low memory target and suspension when not loading; selection resumes the active tab. Suspension is best effort, not a memory quota.

네이티브 MainWindow가 탭·승인·취소·감시를 관리합니다. 탭은 동일한 브라우저 프로필을 공유합니다. 비활성 탭의 메모리 목표를 낮추고 일시 정지를 시도하지만 메모리 사용량을 보장하지 않습니다.

## Execution gate / 전경 실행 게이트

`CanExecute` requires an open app, window activation, equality with the actual Windows foreground handle, the selected registered WebView, and a page that is not loading. It is more specific than checking a Boolean “active” flag.

- Macro replay stops on gate denial. It does not pause and automatically resume.
- Watch checks pause on a different tab/window, and can resume on the original tab within the original schedule.
- Closing the target stops its watch/recording and cancels automation.
- Changing the watched origin stops the watch.
- Cancellation cannot undo a navigation or JavaScript effect already dispatched.

Macro DOM observation carries the source URL and a document identity based on `performance.timeOrigin`. Runtime checks bind a click/input to that observed document, recheck after approval, and require the same identity inside the effect script. This prevents reusing approval after a navigation or reload; it does not prove that arbitrary page content is trustworthy.

실제 OS 전경 창과 선택된 WebView를 검사합니다. 매크로는 게이트가 닫히면 중지하고 감시는 원래 시간 범위 안에서 일시 정지/재개합니다. 취소는 이미 전송한 클릭이나 이동을 되돌리지 못합니다. 매크로 승인에는 URL과 문서 identity를 연결하고 실행 직전 다시 확인합니다.

## DOM snapshots / DOM 스냅샷

`SnapshotService` executes `BrowserScripts.SnapshotScript` in the current document. It returns at most 500 visible interactive elements: `ref`, `tag`, `role`, `text`, `selector`, and `disabled`. Labels are capped at 240 characters and recognized sensitive fields are labeled `[sensitive]`; it does not export input values as a form payload.

Selectors prefer IDs, test IDs, names, and accessible labels, then fall back to a DOM path. `@1`-style references are snapshot-local, not a durable element registry. Visibility is a DOM heuristic, not a screenshot or complete accessibility-tree audit. Snapshot text remains untrusted and may contain personal information.

The native Snapshot action shows a short preview and logs an item count; it does not send the snapshot to a model.

최대 500개 요소와 240자 라벨을 추출합니다. 스냅샷 참조는 일시적이며 영구 요소 레지스트리가 아닙니다. 페이지 텍스트는 신뢰하지 않으며 자동으로 외부 모델에 보내지 않습니다.

## Macro programs

### Recording and persistence / 녹화와 저장

`MacroBridgeScript` observes trusted click/change events and posts narrow recording messages. Native code accepts them only for the recording tab, from the current page source, while the execution gate permits capture. Messages are still untrusted and validated by `MacroService`.

**Current UI recording captures safe navigation URLs and clicks only.** URLs with queries, fragments, embedded credentials or unsupported schemes are skipped with status feedback. Input capture requires `InputCaptureValidator`, and Main supplies no validator. Sensitive-field heuristics in the bridge are not sufficient authorization to record values. A new recording replaces the current steps; the current URL is captured first.

Import is a pasted JSON array. Property names are case-insensitive; kinds and conditions use the lowercase names below. Unknown properties, invalid types, explicit null step arrays, and unsupported programs are rejected before replacement. Import failure leaves the existing program intact.

Plaintext export/save deliberately refuses macros with input values or non-recordable navigation URLs, including nested steps. The application does not rewrite old exported files. A URL path or selector may still contain identifying information, so manual review remains necessary.

The import text editor limits input to **131,072 characters**; the service separately limits JSON import/export to **1 MiB UTF-8**. Export shows the JSON, then saves only on **Save locally**. Main calls `SaveAsync(_dataRoot, "macro.json")`, using a temporary file and replacement in the same directory. General service helpers default to `macros.json`; this is not the UI filename. There is no automatic startup load, named library, or SQLite macro persistence.

현재 화면에서는 안전 검사를 통과한 URL 이동과 클릭만 녹화합니다. 값이 있는 입력 단계와 쿼리·프래그먼트 등이 포함된 URL은 평문 내보내기 및 저장에서 거부됩니다. 입력 녹화에는 Main이 제공하지 않는 명시적 허용 검증기가 필요합니다. JSON 가져오기는 전체 검증 후 교체하며 실패 시 기존 매크로를 유지합니다. 편집창 제한은 131,072자, 서비스 제한은 UTF-8 1 MiB입니다. 내보내기는 사용자의 저장 선택 후 `macro.json`을 원자적으로 교체하며 자동 시작 로드는 없습니다.

### Step semantics / 단계 의미

| Kind | Required data | Behavior |
| --- | --- | --- |
| `navigate` | Absolute HTTP(S) `Url` | Navigate and await completion of the matching navigation |
| `click` | `Selector` | Await one ready nonsensitive target, obtain native approval, revalidate, click once |
| `input` | `Selector`, `Value` | Await one ready nonsensitive input/textarea, set value and dispatch input/change |
| `wait` | `Selector`, optional `Condition` | Poll a DOM condition without an effect |
| `retry` | Exactly one click/input/wait in `Steps` | Retry readiness within the child's total timeout; never replay a dispatched effect |
| `if` | `Selector`, `Condition`, `Steps`, `ElseSteps` | Observe once and execute the matching branch |
| `loop` | `LoopCount`, `Steps` | Repeat the body a fixed number of times |

Conditions are `ready` (default), `exists`, `absent`, `visible`, and `enabled`. “Ready” means exactly one visible, enabled, nonsensitive match without an observation error. Multiple matches and sensitive targets fail, including during conditions. Imported inputs are limited to supported ordinary input types or textarea; there is no arbitrary JavaScript step.

`retry` is not a recovery mechanism for an uncertain click. An explicit `loop` can intentionally repeat effects and must be reviewed accordingly. Every macro click has native approval; ordinary navigation/input do not have an additional click-style confirmation.

`wait`는 조건을 반복 관찰하고 `if`는 한 번 관찰합니다. `retry`는 준비 상태만 재시도하며 이미 실행한 효과는 반복하지 않습니다. `loop`는 효과를 의도적으로 반복할 수 있으므로 검토가 필요합니다. 클릭은 매번 승인하고 일반 이동/입력에는 별도 클릭 확인창이 없습니다.

### Bounds / 제한

| Limit | Current default or accepted range |
| --- | --- |
| Total replay duration | 120,000 ms |
| Operations/program nodes | 1,000; observations and loop iterations consume execution operations |
| Nesting | 16 levels |
| Step timeout | 1–30,000 ms; click default 30,000 ms, other steps 5,000 ms |
| Poll interval | 10–5,000 ms; default 100 ms |
| Retry count | 0–10 additional readiness attempts; default 0 |
| Retry delay | 0–5,000 ms; default 200 ms |
| Loop count | 1–100; default 1 |
| Strings | Selector 2,048, value 4,096, URL 8,192 characters |

Approval and optional healing time count toward budgets. The interpreter validates/detaches the complete program before its first effect and prevents simultaneous replay. Effects are rechecked at dispatch; they are never retried after dispatch. Cancellation/timeouts do not guarantee that an already-dispatched WebView2 script had no effect.

Self-healing requires both `AllowSelfHeal` on the step and a caller-supplied callback. Main supplies no callback or model. A missing selector therefore fails rather than invoking AI.

기본 총 시간은 120초, 작업 예산은 1,000, 중첩은 16입니다. 관찰과 반복 횟수도 작업 예산에 포함됩니다. 승인 대기 역시 제한 시간에 포함되며 self-heal 플래그만 켜도 AI가 작동하지 않습니다.

### Nested JSON example / 중첩 JSON 예제

This schema example requires a controlled fixture page with unique, ordinary `#search` and `#search-button` elements. Replace the illustrative URL with your own fixture. It is not a live-site compatibility test or a reservation workflow.

아래는 스키마 예제입니다. 유일한 일반 입력 요소와 버튼이 있는 테스트 페이지가 필요하며 URL은 자신의 fixture로 바꿔야 합니다. 실제 예약 사이트에서 검증한 매크로가 아닙니다.

```json
[
  {
    "Kind": "navigate",
    "Url": "https://example.com/search",
    "TimeoutMs": 15000
  },
  {
    "Kind": "wait",
    "Selector": "#search",
    "Condition": "ready",
    "TimeoutMs": 10000
  },
  {
    "Kind": "loop",
    "LoopCount": 2,
    "Steps": [
      {
        "Kind": "if",
        "Selector": "#search",
        "Condition": "ready",
        "Steps": [
          {
            "Kind": "retry",
            "RetryCount": 2,
            "RetryDelayMs": 200,
            "Steps": [
              {
                "Kind": "input",
                "Selector": "#search",
                "Value": "museum",
                "TimeoutMs": 5000
              }
            ]
          }
        ],
        "ElseSteps": [
          {
            "Kind": "wait",
            "Selector": "#search",
            "Condition": "ready",
            "TimeoutMs": 5000
          }
        ]
      }
    ]
  },
  {
    "Kind": "click",
    "Selector": "#search-button",
    "TimeoutMs": 30000
  }
]
```

The loop intentionally repeats input when the branch is ready. Its alternative only waits; it does not retroactively run the other branch. The final click requires native approval.

## Foreground watch / 전경 감시

`StartWatch` captures the selected WebView, origin, target text, auto-click choice, and replay choice. Changing the checkboxes later does not modify the running watch. Immediate watches start now and end after 30 minutes.

`WatchSchedule.TryParse` requires an ISO timestamp with uppercase `T` and timezone `Z` or `±HH:mm`; seconds are optional, and fractional seconds are accepted with seconds. It validates actual dates, start < end, a future end, and an interval no longer than 24 hours. It does not create an OS-scheduled job.

Every 3 seconds, active processing checks the original schedule and execution gate. Matching is case-sensitive exact text after whitespace normalization. Eligible button/link controls must be enabled, visible, and unobstructed within the viewport, with exactly one candidate. The service does not scroll, refresh, traverse frames, or search shadow roots.

Finding a candidate ends the watch after handling it. Auto-click requests **Click once**, binding the observed selector, source URL, and document identity to that approval. The native service and effect script recheck these tokens and the gate before dispatch. Declining confirmation does not trigger a click or replay. Optional replay occurs only after a successful confirmed watch click. Read-only detection alone does not start a macro.

시작 시 원래 탭과 선택 설정을 고정하며 실행 중 체크박스 변경은 반영하지 않습니다. 즉시 감시는 30분, 예약은 시간대가 있는 ISO 형식과 최대 24시간 구간을 요구합니다. 정확한 대소문자 구분 텍스트를 감시하고 발견된 후보가 하나여야 합니다. 자동 클릭 옵션도 수동 승인을 요구하며 클릭 성공 후에만 후속 매크로를 재생합니다.

## Vault and delegation / Vault와 외부 AI

Typed login/address/payment-metadata records live under `Vault/Typed-v1`; DPAPI protects all records, with optional Credential Manager login-secret storage. Payment types have no PAN/security-code/PIN fields. Exact HTTPS-origin fill is native-user initiated, stops recording without restarting it, uses an autocomplete allowlist, and never submits or dispatches change events. See [Security](SECURITY.md) for the full boundary.

The delegate UI enters a task → prepares a filtered prompt → requests native review → opens a visible Gemini/Claude tab → offers explicit Copy task. The user signs in, pastes, and sends manually. The selected reply is shown as untrusted text, not a tool instruction. `FillComposerAsync` exists but is not called by Main.

`LocalIntentRouter` recognizes a small set of English/Korean commands. `IAiProvider`, `AiRequest`, `AiResponse`, and `ProviderRegistry` are contracts only; no API/local-model adapter is registered. `ApprovalPolicy` models sensitive actions but does not implement mail, purchase, shell, or Git tools.

Vault는 메타데이터 전용 결제 슬롯을 포함한 타입별 저장입니다. 외부 AI는 작업 검토 후 사용자가 직접 복사/붙여넣기/전송합니다. 답변은 신뢰하지 않는 텍스트로만 표시합니다. 공급자/승인 정책 인터페이스가 존재해도 메일·결제·shell·Git 연동이 구현된 것은 아닙니다.

## Data and compatibility / 데이터와 호환성

`AuditLogService` uses `luma.db` with timestamp/category/action/target/detail columns and parameterized inserts. Current call sites log origins, provider names, counts, and action outcomes rather than full form payloads. The database is not encrypted by this service.

The existing browser profile and audit database remain in their existing locations. Legacy untyped `Vault/*.bin` records are **not loaded, migrated, or deleted** by the typed vault. Retained old files require separate review; an automatic migration workflow is future work. Do not mistake an empty typed slot list for deletion of old vault data.

기존 브라우저 프로필과 감사 DB는 유지합니다. 이전 `Vault/*.bin` 파일은 새 Vault가 읽거나 변환하거나 삭제하지 않습니다. 타입별 목록이 비어 있어도 예전 파일이 제거된 것은 아닙니다.

## Verification and resource limits / 검증과 자원 한계

The Windows console harness links the relevant source files and checks interpreter behavior, vault storage/validation, delegate privacy, JSON, and schedule parsing. CI uses locked restore, Release builds, direct DLL test execution, and Node 22. VaultTests exports generated fill scripts through `LUMA_VAULT_TEST_SCRIPT_DIRECTORY`; `node tests/VaultAutofillTests.js <scripts-dir>` runs 17 cases. `node tests/ForegroundWatchTests.js` exercises 35 production-script cases. The final macro-runtime suite has 15 scenarios. Commands are in [README](../README.md#verification).

Reported integration results include a zero-warning Release build and passing macro/vault/Credential Manager/privacy/JSON suites. Live smoke confirmed launch, sidebar, Google navigation, a 20-element snapshot, and Ctrl+T. Native window capture showed a blank WebView surface while DOM/accessibility remained available; a composition-capture limitation is a possible explanation, not a confirmed diagnosis.

The Node tests exercise generated JavaScript with simulated DOM objects. The console fake-host tests do not prove live website, focus-race, vendor composer, or reservation behavior. No startup/working-set/CPU/battery benchmarks exist for the i5 13th-generation/8 GB design target.

Core DOM automation does not bypass cross-origin iframes, shadow roots, canvas rendering, CAPTCHA, passkeys, OTP, or payment challenges. No always-on model, background watch daemon, code workspace, visual macro editor, or autonomous agent is implemented.

검증은 콘솔/생성 JS 회귀 테스트와 제한된 화면·DOM 스모크입니다. 실제 공급자나 예약 사이트 자동화 완료를 증명하지 않습니다. i5 13세대/8 GB 설계 목표의 하드웨어 벤치마크는 없으며 인증·iframe·shadow DOM·canvas를 우회하지 않습니다.
