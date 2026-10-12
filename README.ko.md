# Luma

**로컬 자동화와 보이는 AI 작업 전달을 제공하는 개인용 Windows 브라우저.**

[English](README.md) · [아키텍처](docs/ARCHITECTURE.md) · [보안](docs/SECURITY.md) · [로드맵](docs/ROADMAP.md)

Luma는 Safari에서 영감을 받은 간결한 WinUI 3 화면에 Microsoft Edge WebView2, 실행 범위가 제한된 DOM 매크로, 전경 감시, 타입이 지정된 로컬 Vault를 결합합니다. Windows x64 환경의 개인 사용을 위한 프로젝트입니다. 상주 모델이나 별도로 번들한 Chromium 없이 **Intel Core i5 13세대와 8 GB RAM**을 설계 목표로 삼지만, **측정으로 검증한 최소 사양이나 성능 보장은 아닙니다.**

현재 구현은 결정적 로컬 도구와 Gemini/Claude 수동 작업 전달입니다. 쇼핑·예약·코딩·메일·일정 작업을 스스로 완수하는 범용 에이전트는 구현되어 있지 않습니다.

## 현재 구현된 기능

| 영역 | 실제 동작 |
| --- | --- |
| 브라우징 | 영구 WebView2 프로필, 여러 탭, 주소/검색 입력, 뒤로/앞으로/새로고침/홈, 팝업 요청을 탭으로 열기, 로딩 표시 |
| 화면 | 간결한 탭, 접근성 이름과 툴팁이 있는 아이콘 버튼, 밝은/어두운/고대비 테마, 기본 숨김 상태의 360 px 에이전트 패널 |
| DOM 스냅샷 | 보이는 인터랙티브 DOM 요소 최대 500개, 라벨·선택자·비활성 상태, 라벨당 최대 240자 |
| 녹화 | 현재 세션에서 녹화 탭의 클릭과 이동 기록, 새 녹화 시작 시 기존 단계 교체 |
| 매크로 | JSON 검증, `navigate`·`click`·`input`·`wait`·`retry`·`if`·`loop`, 시간/작업/중첩 제한, 클릭별 수동 승인 |
| 전경 감시 | 원래 탭에 연결된 정확한 텍스트 감시, 3초 간격 확인, 시간 범위 설정, 수동 클릭 확인, 선택적 후속 매크로 |
| Vault | 로그인·주소·결제 메타데이터 슬롯 추가/조회/삭제/채우기, DPAPI, 로그인 비밀의 선택적 Windows 자격 증명 관리자 저장 |
| 외부 AI | 네이티브 작업 검토, 보이는 Gemini/Claude 탭, 명시적 Copy task, 사용자의 붙여넣기/전송, 선택한 답변 표시 |
| 로컬 서비스 | SQLite 감사 기록, 한국어/영어 규칙 기반 명령, API 어댑터가 연결되지 않은 공급자 인터페이스 |
| 검증 | Windows Release 빌드, 콘솔 동작 검증 하네스, 의존성 잠금 파일과 CI locked restore |

### 단축키

| 단축키 | 동작 |
| --- | --- |
| Ctrl+L | 주소 입력창으로 이동하고 전체 선택 |
| Ctrl+T | 새 탭 |
| Ctrl+W | 선택한 탭 닫기 |
| F5 | 선택한 탭 새로고침 |

에이전트 버튼으로 패널을 열 수 있습니다. 마지막 탭을 닫으면 새 홈 탭을 만듭니다. 빈 주소는 Google 홈으로 이동하고 URL로 인식되지 않는 텍스트는 Google 검색으로 처리합니다.

## 개발 환경과 빌드

Windows x64, **.NET 8 SDK**, Windows/WinUI 빌드 도구, **WebView2 Evergreen Runtime**이 필요합니다. 프로젝트 대상은 `net8.0-windows10.0.19041.0`, 선언된 최소 플랫폼 버전은 `10.0.17763.0`입니다. 사용하는 Windows 버전과 설치된 Windows App Runtime의 호환성도 확인해야 합니다.

앱은 패키징되지 않은 WinUI 앱이며 Windows App SDK 자체 포함 배포가 아닙니다. 고정된 SDK에 필요한 Windows App Runtime과 WebView2 Evergreen을 설치하세요. 대형 로컬 LLM이나 공급자 API 키는 기본 브라우징/매크로에 필요하지 않습니다.

| 직접 의존성 | 고정 버전 |
| --- | --- |
| Microsoft.WindowsAppSDK | 2.5.1 |
| Microsoft.Web.WebView2 | 1.0.4258.31 |
| Microsoft.Data.Sqlite | 8.0.31 |
| System.Security.Cryptography.ProtectedData | 8.0.0 |

WebView2 NuGet 버전은 개발 API 패키지의 버전입니다. 실제 브라우저 엔진은 별도의 Evergreen Runtime이 제공하고 업데이트합니다.

저장소 루트에서 실행합니다.

```powershell
dotnet restore src/Luma/Luma.csproj -p:Platform=x64 --locked-mode
dotnet build src/Luma/Luma.csproj -c Release -p:Platform=x64 --no-restore
```

Windows WinUI 개발 환경에서 `Luma.sln`을 열어 실행/디버깅하거나, 런타임 설치 후 생성된 x64 Release 실행 파일을 실행할 수 있습니다. 잠금 파일은 저장소에 포함됩니다. CI는 locked restore로 의존성의 임의 변경을 막으며, 의도적인 패키지 변경 시 잠금 파일도 갱신하고 검토해야 합니다.

## 사용 흐름

### 매크로 녹화, 가져오기, 재생

1. 작업할 페이지를 선택하고 패널을 엽니다.
2. **Record**를 누릅니다. 현재 URL과 이후 해당 녹화 탭의 클릭/이동을 기록합니다.
3. 녹화를 중지하고 단계를 검토합니다. 새 녹화는 현재 매크로를 지웁니다.
4. Luma를 전경에 두고 원하는 탭을 선택한 상태에서 **Replay**를 누릅니다. 매크로의 각 클릭에는 네이티브 확인창이 나타납니다.
5. **Cancel automation**은 재생 취소와 녹화/감시 중지를 요청합니다.

**입력 값 녹화는 기본적으로 비활성화되어 있습니다.** 호스트가 `MacroService.InputCaptureValidator`를 명시적으로 제공해야 하지만 Main은 이를 제공하지 않습니다. 웹 페이지가 보내는 메타데이터만으로 입력 값 녹화를 허용하지 않습니다. JSON으로 가져온 `input` 단계는 사용할 수 있으나, 실행 시 대상의 민감성·가시성·활성 상태·유일성을 다시 검사합니다.

**Import**는 JSON 배열을 붙여넣는 기능입니다. 전체 프로그램 검증이 성공해야 현재 매크로를 교체합니다. **Export**는 JSON을 표시하고, **Save locally**는 서비스의 원자적 저장 경로를 사용해 `%LOCALAPPDATA%\Luma\macro.json`에 저장합니다. 일반 입력 값이나 URL에도 개인정보가 들어갈 수 있으며 내보낸 JSON은 암호화되지 않습니다. 저장 파일을 자동으로 불러오거나 이름별 매크로 라이브러리를 관리하지 않습니다.

[매크로 스키마·제한·중첩 예제](docs/ARCHITECTURE.md#macro-programs)를 참고하세요. 예제는 명시된 선택자가 있는 테스트 페이지가 필요하며 특정 예약 사이트용 완성 스크립트가 아닙니다.

### 화면에 보이는 컨트롤 감시

**Foreground Watch**에 컨트롤에 실제로 표시되는 정확한 텍스트를 입력합니다. 공백은 정규화하지만 부분 문자열이나 유사도 매칭은 사용하지 않습니다. 현재 뷰포트에서 가려지지 않고 활성화된 대상이 정확히 하나여야 합니다. 관찰은 페이지를 스크롤하거나 새로고침하지 않습니다.

- **Start watching**은 현재부터 30분 동안 감시합니다.
- **Schedule watch**는 시간대 offset 또는 `Z`가 있는 전체 ISO 타임스탬프를 요구합니다. 예: `2026-10-20T19:00:00+09:00`, `2026-10-20T19:30:00+09:00`. `WatchSchedule.TryParse`가 형식과 실제 날짜를 검증합니다. 시작은 종료보다 앞서야 하고 종료는 미래여야 하며 간격은 최대 24시간입니다. 시간대 없는 입력은 거절합니다.
- 감시는 원래 탭과 origin에 연결됩니다. 다른 탭이나 다른 앱으로 이동하면 작업을 일시 정지하고, 시간 범위 안에서 돌아오면 이어갈 수 있습니다. 원래 탭을 닫거나 다른 origin으로 이동하면 감시를 종료합니다.
- **Auto-click when found**를 켜도 발견 후 **Click once** 확인이 필요합니다. 무인 자동 클릭 승인이 아닙니다.
- **Replay macro when found**는 승인된 감시 클릭이 실제 성공한 뒤에만 현재 매크로를 재생합니다. 클릭 없이 발견만 한 경우 재생하지 않습니다.
- Luma가 열려 있어야 합니다. OS 깨우기, 영구 예약 스케줄러, 백그라운드 예약 서비스는 없습니다.

실행 게이트는 실제 전경 OS 창 핸들, 선택된 WebView, 로딩 상태를 확인합니다. 만료는 활성 타이머 처리 때 검사하며, 만료된 감시에 다시 돌아와도 종료 시간이 연장되지 않습니다.

### 로컬 Vault

Vault 버튼에서 **Add slot**을 선택합니다.

| 종류 | 저장 정보 | 허용되는 autocomplete |
| --- | --- | --- |
| 로그인 | 라벨, 정확한 HTTPS origin, 사용자명; DPAPI 또는 선택적 자격 증명 관리자에 비밀번호 | `username`, `current-password` |
| 주소 | 이름, 주소 두 줄, 도시/지역, 우편번호, 국가 코드, 이메일, 전화 | `name`, `address-line1`, `address-line2`, `address-level2`, `address-level1`, `postal-code`, `country`, `email`, `tel` |
| 결제 메타데이터 | 브랜드, 마지막 네 자리, 명의자, 만료 월/연도 | `cc-name`, `cc-exp-month`, `cc-exp-year` |

결제 슬롯에는 **전체 카드번호/PAN, CVV/CVC/보안 코드, PIN 필드가 없습니다.** 브랜드와 마지막 네 자리는 표시용이며 페이지에 주입하지 않습니다. 카드번호 입력과 결제 인증은 사용자가 직접 수행합니다.

일치하는 HTTPS 페이지에서 **Fill selected**를 선택하면 녹화를 중지하고 전경/탭/문서 상태를 검사합니다. 최상위 문서의 정확한 단일 autocomplete 토큰만 사용합니다. 중복·숨김·비활성·민감·안전하지 않은 필드는 거절합니다. `shipping address-line1` 같은 복합 토큰은 지원하지 않습니다. 로그인은 같은 origin의 HTTPS POST 폼이 필요하며 다른 종류도 발견된 폼의 action/method를 검사합니다.

채우기는 제출·클릭·input/change 이벤트를 발생시키지 않습니다. 일부 제어형 폼은 값을 인식하지 못할 수 있으므로 직접 확인하세요. 채운 정보는 해당 사이트의 페이지 스크립트에 노출될 수 있습니다. 정확한 origin 일치가 사이트의 신뢰성을 보장하지는 않습니다.

### 보이는 Gemini/Claude 작업 전달

1. **Gemini** 또는 **Claude**를 선택합니다.
2. 네이티브 창에 범위가 명확한 작업을 입력합니다. 비밀이나 개인정보를 넣지 마세요.
3. 준비된 프롬프트를 검토하고 **Open visible tab**을 선택합니다.
4. 필요한 로그인은 직접 완료합니다. 클립보드에 복사할 때만 **Copy task**를 선택합니다.
5. 보이는 탭에서 직접 붙여넣고 전송합니다.
6. 답변 텍스트를 선택한 뒤 **Read selected reply**를 누릅니다. 최대 16,000자를 **신뢰하지 않는 외부 텍스트**로 표시합니다.

DOM 스냅샷, 쿠키, Vault, 자동 페이지 문맥은 첨부하지 않습니다. 프롬프트 준비는 인식 가능한 비밀 할당을 거절하고, 이메일 가리기 전에 자격 증명이 포함된 URL을 거절합니다. HTTP(S) URL은 대소문자 구분 없이 처리하며 이메일·긴 숫자열을 가리고 URL의 쿼리/fragment를 제거합니다. 이는 보조 규칙이며 모든 개인정보를 탐지하지 못하므로 반드시 내용을 검토하세요.

`VisibleWebAiDelegate.FillComposerAsync` 보조 메서드는 존재하지만 Main 화면은 이를 자동으로 호출하지 않습니다. 자동 전송, 전체 대화 수집, 답변 지시 실행, 답변의 매크로 변환은 없습니다. 외부 공급자의 계정·약관·사용량 제한·요금은 별도로 적용되며 무료 사용을 보장하지 않습니다.

### 로컬 명령

명령창은 LLM 없이 한국어/영어 규칙으로 동작합니다. 예: `snapshot`, `record`, `stop recording`, `replay`, `watch 19:00`, `스냅샷`, `매크로 녹화`, `녹화 중지`, `매크로 재생`. 알 수 없는 명령은 안내를 표시할 뿐 공급자 API나 범용 계획기를 실행하지 않습니다.

## 아키텍처와 Free-First 실행

```text
WinUI 3 화면 + 공유 WebView2 환경
  ├─ 로컬 명령 → DOM 스냅샷 / 제한된 매크로 / 전경 감시
  ├─ 사용자 승인 로컬 입력 → 타입별 DPAPI / 자격 증명 관리자 Vault
  ├─ 검토한 작업 → 보이는 Gemini / Claude 탭 → 사용자 전송
  └─ origin과 작업 건수 → 로컬 SQLite 감사 기록
```

녹화·재생·감시는 모델 호출 없이 동작합니다. 탭은 하나의 환경을 공유하고 비활성 탭에는 실행 일시 중지와 낮은 메모리 목표를 요청합니다. self-heal 콜백과 공급자 인터페이스는 명시적으로 연결하는 확장 지점입니다. 로컬 LLM을 상주시키거나 유료 API로 자동 전환하지 않습니다. 매크로 스키마·실행 제한·문서 검사·중첩 예제는 [아키텍처](docs/ARCHITECTURE.md)에 설명되어 있습니다.

## 로컬 데이터

| 위치 | 내용 |
| --- | --- |
| `%LOCALAPPDATA%\Luma\WebView2Profile` | 사이트 세션 등을 포함한 영구 브라우저 프로필 |
| `%LOCALAPPDATA%\Luma\luma.db` | SQLite 감사 기록 |
| `%LOCALAPPDATA%\Luma\Vault\Typed-v1\<guid>.vault` | DPAPI로 보호한 타입별 기록 |
| Windows 자격 증명 관리자 | Luma 프로필 네임스페이스의 선택적 로그인 비밀 |
| `%LOCALAPPDATA%\Luma\macro.json` | 사용자가 명시적으로 저장한 평문 매크로 JSON |

앱 자체의 동기화나 자동 클라우드 Vault 백업은 없습니다. 브라우저 프로필, 감사 메타데이터, 매크로, 명시적으로 복사한 작업 프롬프트도 개인정보로 관리해야 합니다. DPAPI 보호는 Vault 기록에 적용되며 데이터 디렉터리 전체를 암호화하지 않습니다.

기존 브라우저 프로필과 감사 데이터는 유지합니다. 이전 비타입 `Vault/*.bin`은 새 Vault가 읽거나 변환하거나 삭제하지 않습니다. 타입별 목록이 비어 있어도 예전 파일이 제거된 것은 아닙니다. 사용자 지시 기반 마이그레이션은 향후 작업입니다.

## 검증

Windows x64 콘솔 하네스는 빌드 후 DLL을 직접 실행합니다.

```powershell
dotnet restore tests/Luma.Tests/Luma.Tests.csproj -p:Platform=x64 --locked-mode
dotnet build tests/Luma.Tests/Luma.Tests.csproj -c Release -p:Platform=x64 --no-restore
dotnet tests/Luma.Tests/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Luma.Tests.dll
```

`dotnet test` 어댑터 기반 프로젝트가 아닙니다. DLL 직접 실행은 `dotnet run`의 플랫폼별 출력 경로 불일치를 피합니다.

DOM 스크립트 회귀 검증에는 **Node 22**를 사용합니다. DLL 실행 전에 fixture 디렉터리를 지정하여 Vault 스크립트를 생성한 뒤 두 JS 테스트를 실행합니다.

```powershell
$env:LUMA_VAULT_TEST_SCRIPT_DIRECTORY = Join-Path $env:TEMP 'luma-dom-tests'
New-Item -ItemType Directory -Force $env:LUMA_VAULT_TEST_SCRIPT_DIRECTORY | Out-Null
dotnet tests/Luma.Tests/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Luma.Tests.dll
node tests/VaultAutofillTests.js $env:LUMA_VAULT_TEST_SCRIPT_DIRECTORY
node tests/ForegroundWatchTests.js
```

VaultTests가 현재 구현의 fill 스크립트를 해당 디렉터리에 생성합니다. JS 테스트는 생성 Vault/production watch 스크립트와 모의 DOM을 검사하며 실제 공급자 브라우저 테스트가 아닙니다. CI는 .NET 8/Node 22 설치, 앱·테스트 locked restore, Release 빌드, DLL과 JS 회귀 테스트를 실행하도록 구성됩니다.

분기/반복, 지연된 DOM 준비, 모호하거나 안전하지 않은 대상, 승인/전경 게이트, 취소, 단일 재생, 시간/작업 예산, 실행된 효과 재시도 금지, 명시적 self-heal 동의, Vault 타입 검증·정확한 origin·필드 허용 목록, DPAPI 변조/취소, 자격 증명 관리자 왕복, 프롬프트 개인정보 처리, JSON 왕복/거절, 승인되지 않은 브리지 입력 거절을 검사합니다.

현재 통합 검증에서는 **Release 빌드 경고 0개**, Vault/자격 증명 관리자·delegate privacy·JSON·스케줄 검증 통과, **매크로 런타임 15개·Vault JS 17개·감시 production-script 35개 사례 통과**가 보고되었습니다. 매크로/감시 클릭 승인은 관찰한 source/document token에 연결하고 실행 직전 다시 검사합니다. 실제 화면 스모크에서는 실행, 사이드바 표시/토글, Google 이동, DOM 스냅샷 20개, Ctrl+T 새 탭을 확인했습니다. 네이티브 창 캡처에서 WebView 영역이 빈 상태로 보였지만 DOM/접근성 정보는 있었으며, composition 캡처 제한 가능성은 아직 확정하지 않았습니다.

이 검증은 Gemini/Claude 전체 동작이나 예약 사이트 자동화 완료를 의미하지 않습니다. 8 GB 환경의 시작 시간·메모리·CPU·배터리 하드웨어 벤치마크도 아직 없습니다.

## 지원 범위와 다음 작업

자동화는 일반 DOM 선택자를 사용합니다. cross-origin iframe, shadow DOM, canvas, CAPTCHA, passkey, OTP, 결제 인증을 우회하지 않습니다. 인증은 사람이 수행하며, 사이트 DOM 변경이나 정책 때문에 작업이 중단될 수 있습니다.

API/로컬 모델 어댑터, 상주 모델, 자동 self-healing 공급자, 시각적 매크로 편집기, 메일/일정 연동, Git/shell 도구, 자율 결제/예약 계획기는 구현되어 있지 않습니다. `IAiProvider`, 레지스트리, 승인 정책 enum, self-heal 콜백은 확장 지점이며 완성된 연동이 아닙니다.

[보안 문서](docs/SECURITY.md)와 [향후 작업](docs/ROADMAP.md)을 참고하세요. Luma는 개인/비공개 사용을 위한 프로젝트이며 호스팅 서비스나 프로덕션 준비 완료를 주장하지 않습니다.

다음 우선순위는 실제 사이트·네이티브 포커스 검증, 목표 하드웨어에서의 자원 측정, 이름별 매크로 라이브러리·편집기, 명시적으로 설정하는 선택적 AI 어댑터입니다. 메일·일정·코딩 도구는 이후 별도 범위로 구현합니다.
