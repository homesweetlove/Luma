# Security and Privacy / 보안과 개인정보

Luma is intended for private/personal use on Windows. Its native host handles browser sessions and vault data while websites and external AI text remain untrusted. This document describes implemented safeguards and their limits; it is not a security certification.

Luma는 Windows 개인용 앱입니다. 네이티브 호스트가 세션과 Vault를 관리하고 웹 페이지와 외부 AI 텍스트는 신뢰하지 않습니다. 아래 보호 기능은 보안 인증이나 모든 사이트에 대한 안전 보장이 아닙니다.

## WebView2 boundary / 웹 콘텐츠 경계

Microsoft recommends origin checks around script execution and messages, validation of data from web content, narrow host interactions, and avoiding unnecessary host-object access. Luma disables host objects and does not expose a general native tool dispatcher to a page. JavaScript and recording messages remain enabled because the browser/recorder need them. DevTools remains enabled; this is a personal development app. [Microsoft WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).

The recorder bridge installed on document creation is not privileged authorization. Native code checks the recording tab, current message source, and foreground state; MacroService validates the message schema and bounds. Page content can still be misleading. Snapshot labels, selectors, and replies must be treated as data rather than instructions.

Run the app as an ordinary Windows user. Keep WebView2 Evergreen updated. Host checks reduce exposure but cannot protect an already compromised Windows account or make a malicious matching website trustworthy.

Microsoft 지침처럼 origin과 메시지를 검사하고 임의 네이티브 도구 호출을 노출하지 않습니다. 브리지 메시지는 녹화 탭·출처·형식·제한을 검사하지만 페이지에서 온 값 자체는 신뢰하지 않습니다. 관리자 권한 없이 실행하고 Evergreen을 갱신하세요.

## Data classes / 데이터별 보호

| Data | Current location/protection | Limits |
| --- | --- | --- |
| Browser sessions | Persistent WebView2 profile | Not an app-managed encrypted vault or separate identity per tab |
| Typed login/address/payment metadata | DPAPI CurrentUser records in `Vault/Typed-v1` | Windows-account boundary, not a separate master-password/Hello lock |
| Login password | DPAPI record or optional Windows Credential Manager | Resolved only for the local fill path; not a display/list-model property |
| Payment metadata | Brand, last four, holder, expiry only | No PAN, security code, PIN, or arbitrary payment-field bag |
| Macro JSON | Explicit local export with safety checks | Plaintext; export/save rejects input-value steps and navigation URLs with query, fragment or embedded credentials. Other paths/selectors can still be sensitive |
| Audit DB | SQLite `luma.db` | Not encrypted; origins and action metadata can identify browsing behavior |
| Delegate prompt | Native review, explicit clipboard copy | External provider/clipboard privacy applies after user disclosure |

DPAPI protects typed records for the current Windows user. Credential Manager optionally stores only the login secret; metadata stays in DPAPI records. The broker uses a profile-specific Luma target namespace. Byte buffers are cleared where implemented, but passwords also exist temporarily as managed strings and in the destination page. Do not claim guaranteed memory erasure or protection against software running as the same user.

Unknown typed-record properties, invalid IDs/types/metadata, malformed records, and oversized records are rejected. Payment labels and holder names reject recognizable full-card-number and security-code content, including normalized full-width characters. Free-text validation is not a universal secret detector; never enter secrets into labels/address fields.

DPAPI는 현재 Windows 사용자 기준으로 보호합니다. 자격 증명 관리자는 선택적 로그인 비밀 저장소이며 메타데이터는 계속 DPAPI로 보호합니다. 같은 계정의 악성 프로그램이나 일시적인 메모리/페이지 노출을 막는 별도 보안 경계가 아닙니다. 결제 슬롯에는 카드번호·보안 코드·PIN을 저장하지 않습니다.

## Vault fill / Vault 채우기

A fill requires a native user action and a ready selected tab in the actual OS foreground window. The service validates HTTPS scheme, normalized host and port, full document URL, and a single-use document token. A subdomain, HTTP page, or different port is not the same origin. Native checks and in-document checks are repeated before values are written.

The fill stops recording before resolving the secret and does not restart recording. It is confined to the top-level document, with exact single-token autocomplete allowlists:

| Slot | Allowed tokens |
| --- | --- |
| Login | `username`, `current-password` |
| Address | `name`, `address-line1`, `address-line2`, `address-level2`, `address-level1`, `postal-code`, `country`, `email`, `tel` |
| Payment metadata | `cc-name`, `cc-exp-month`, `cc-exp-year` |

The service refuses ambiguous/unsafe matching fields, hidden or disabled controls, sensitive/conflicting field hints, and document changes. Compound tokens such as `billing cc-name` are unsupported. Login fields require a same-origin HTTPS POST form; where a form is present for other supported fields, its action/method is checked too. Brand/last-four are display metadata and are not sent to a field.

Fill sets eligible values without clicking, submitting, or dispatching input/change events. Controlled forms may require manual interaction. Once filled, the destination page can read those values. Allowlisting the origin is a routing constraint, not proof that the site's scripts are safe.

The typed vault does not load, migrate, or delete legacy untyped `Vault/*.bin` files. The existing browser profile and audit database remain in place. Old files may retain previous data; review them separately. There is no automatic migration, cloud sync, vault export, or Windows Hello approval implemented.

채우기는 사용자의 네이티브 동작, 실제 전경 창, 선택 탭, 정확한 HTTPS origin과 문서 URL/token을 검사합니다. 녹화는 중지하고 다시 시작하지 않습니다. 단일 autocomplete 허용 목록만 사용하며 제출·클릭·change 이벤트를 발생시키지 않습니다. 채운 값은 해당 사이트 스크립트가 읽을 수 있습니다. 이전 `Vault/*.bin`은 읽기·변환·삭제하지 않으며 기존 프로필과 감사 DB도 유지합니다.

## Macros and click approval / 매크로와 클릭 승인

The whole program is validated before effects. Unknown steps and arbitrary script/shell kinds are not accepted. Defaults bound replay to 120 seconds, 1,000 operations, and 16 nesting levels, with per-step limits detailed in [Architecture](ARCHITECTURE.md#macro-programs).

Current recording captures clicks/navigation. Input recording is disabled unless a host explicitly supplies `InputCaptureValidator`; Main does not. Imported input is separately supported and live sensitivity/uniqueness/readiness checks apply. Manually pasted JSON values are not automatically scrubbed of every possible secret.

Each macro click requires a native confirmation. The observed source URL and document identity bind approval to that document; runtime and effect scripts recheck them after approval. A navigation/reload requires a fresh attempt rather than transferring consent. Watch clicks similarly bind the approved target to its observed source/document tokens and revalidate before dispatch.

Foreground enforcement checks the OS window handle, selected target, and loading state. Macro gate denial stops replay; watch checks pause. Retry only repeats pre-effect readiness. Explicit loops can repeat effects. Cancellation and timeout cannot retract a WebView2 script already dispatched, so uncertain effects are not automatically retried.

The approval-policy enum includes purchases, reservations, mail, calendar, shell, and Git publication, but these integrations are absent. It is not evidence of comprehensive transaction detection. Review every click and navigate/input destination in a macro.

매크로는 전체 검증 후 실행하며 임의 script/shell 단계가 없습니다. 기본 제한은 120초·1,000작업·16중첩입니다. 클릭 승인은 관찰된 URL/문서 token에 연결하고 실행 직전 다시 검사합니다. 감시 클릭도 승인된 문서/대상에 연결합니다. 취소는 이미 전송한 효과를 되돌리지 못하며 retry는 준비 상태만 재시도합니다.

## Watch scope and authentication / 감시와 인증

Immediate watch is limited to 30 minutes. Scheduled watch requires full ISO timestamps with offset or `Z`, a future end, ordered endpoints, and a maximum 24-hour interval. It stays on the original tab/origin. Auto-click still asks for **Click once**; optional replay requires a successful approved click. It is not an unattended booking service and does not wake the PC.

Exact, case-sensitive text after whitespace normalization must match one eligible unobstructed control. There is no cross-origin iframe, shadow-root, or canvas automation bypass. CAPTCHA, passkeys, OTP, banking/card confirmation, and other authentication remain human tasks. There is no automatic challenge solver or guaranteed challenge/resume state machine.

감시는 원래 탭/origin과 시간 범위에 한정됩니다. Auto-click 옵션도 수동 승인이 필요하고 예약 서비스를 무인으로 수행하지 않습니다. CAPTCHA·passkey·OTP·결제 인증은 사람이 처리하며 iframe·shadow DOM·canvas를 우회하지 않습니다.

## External AI and clipboard / 외부 AI와 클립보드

The actual flow is native task entry → Prepare → review filtered task → Open visible tab → explicit Copy task → manual paste/send. No DOM snapshot, browser cookies, vault content, or automatic page context is attached.

Prompt preparation limits tasks to 8,000 characters and rejects recognizable credential assignments. It processes HTTP(S) URLs case-insensitively, rejects URLs containing user information **before email redaction**, removes query/fragment components, and redacts emails and long numeric sequences. These heuristics cannot detect every secret or personal fact; review the final prompt.

Only the explicit Copy task choice writes the reviewed prompt to the clipboard. Clipboard history, sync, and other apps may retain it. Luma does not automatically clear the clipboard or control provider retention.

Read selected reply imports at most 16,000 selected characters from the last opened delegate's selected foreground tab at its exact origin. It does not verify authorship or that the selection is an assistant reply. It displays untrusted text without executing tools, altering macros, or transferring approval.

The composer helper exists but Main does not automatically invoke it; it does not send a message. Provider APIs/local models are not implemented. The interface alone does not authorize sharing context.

작업 입력→Prepare→검토→보이는 탭→명시적 Copy task→사용자 붙여넣기/전송 순서입니다. URL은 대소문자 구분 없이 처리하고 이메일 가리기 전에 userinfo 포함 URL을 거절합니다. 답변은 선택한 최대 16,000자를 비신뢰 텍스트로 표시할 뿐 도구나 매크로를 실행하지 않습니다. 클립보드와 외부 공급자의 보존 정책은 별도입니다.

## Audit, tests, and remaining limits / 감사·검증·한계

Current audit call sites record origins, provider names, action outcomes, and counts rather than passwords, full card numbers, cookies, or full task/reply bodies. The SQLite database is not a complete approval ledger or a security-monitoring product. Macro recording skips URL queries/fragments/userinfo, while export/save denies input values and unsafe navigation URLs. Previously saved files, URL path segments, selectors and WebView2 profile history may still contain sensitive data even when audit targets are reduced to origins.

The integration run reports a zero-warning Release build and passing console suites. The final tests include 15 macro-runtime scenarios, 17 generated vault JavaScript cases, and 35 watch production-script cases. Generated vault scripts are emitted by VaultTests when `LUMA_VAULT_TEST_SCRIPT_DIRECTORY` is set; Node 22 runs the script regressions. See [README verification](../README.md#verification).

Those tests and the launch/sidebar/Google/snapshot/Ctrl+T smoke do not establish complete vendor handoffs, reservation compatibility, adversarial isolation, or hardware resource limits. Native capture showed a blank WebView surface with available DOM/accessibility; its possible composition-capture cause is unconfirmed.

For resource work, Microsoft describes Evergreen updates, shared environments, lifecycle management, and measuring startup/memory/CPU rather than assuming low cost from native UI. Luma shares its environment and requests inactive-tab suspension/low memory targets. The i5 13th-generation/8 GB target has no hardware benchmarks yet. [Microsoft WebView2 performance guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance).

No always-on model, resident coding workspace, hidden background scheduler, or image-per-step model loop is implemented. Outstanding work is listed in [Roadmap](ROADMAP.md), including real-site validation, runtime resource measurement, capture investigation, and explicit legacy-data migration.

최종 검증에는 매크로 런타임 15개, Vault 생성 JS 17개, 감시 production-script 35개 사례가 포함됩니다. 콘솔/모의 DOM 검증과 제한된 화면 스모크는 공급자·예약 사이트 전체 동작이나 보안 인증을 뜻하지 않습니다. 하드웨어 벤치마크와 캡처 원인 확인은 남은 작업입니다.
