<div align="center">

# Luma

### 가볍고, 로컬 우선이며, 실제 일을 대신 처리하는 Windows 개인용 AI 브라우저

**Safari 감성 UI · WebView2 · 의미 기반 매크로 · 포그라운드 감시 · 로컬 Vault · Free-first AI 라우팅**

[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#요구-환경)
[![WinUI 3](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK-5E5CE6?style=for-the-badge)](#기술-스택)
[![WebView2](https://img.shields.io/badge/WebView2-Chromium-0A84FF?style=for-the-badge&logo=microsoftedge&logoColor=white)](#기술-스택)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#개발)
[![Private](https://img.shields.io/badge/Use-Personal%20%2F%20Private-111111?style=for-the-badge)](#프로젝트-범위)

[![English](https://img.shields.io/badge/README-English-24292f?style=for-the-badge)](./README.md)
[![한국어](https://img.shields.io/badge/README-한국어-24292f?style=for-the-badge)](./README.ko.md)

</div>

---

## Luma가 뭔가?

Luma는 단순히 **브라우저 옆에 AI 채팅창을 붙이는 프로젝트가 아니다.**

목표는 평소에는 가볍고 깔끔한 일반 브라우저처럼 사용하다가, 사용자가 원하는 일을 말하면 브라우저가 실제 웹페이지와 로컬 도구를 조합해 끝까지 처리하는 것이다.

예를 들면:

```text
"썬크림 찾아"
→ 여러 사이트 검색
→ 가격/용량/배송 비교
→ 3~5개 후보 표시

"1번 결제해"
→ 상품 페이지 진입
→ 로그인 세션 사용
→ 옵션 선택
→ 배송지 입력
→ 필요하면 로컬 Vault의 저장 카드 자동 입력
→ 결제 직전 승인
```

또는:

```text
"네이버 예약 19:00 자리 생기면 잡아"
→ 현재 예약 페이지를 화면에 띄워둠
→ 포그라운드 상태에서만 감시
→ 19:00 버튼 활성화 감지
→ 필요 시 자동 클릭
→ 녹화해둔 후속 예약 매크로 실행
→ 최종 예약 승인
```

또는:

```text
"이 코드 고치고 제미나이한테도 한번 검토시켜"
→ Dev Workspace 필요할 때만 로드
→ 코드 수정
→ 테스트
→ 화면에 Gemini delegate 열기
→ 비밀정보 제거한 문맥만 전달
→ 결과 비교
→ Git push 전 승인
```

장기적으로는 브라우저, 예약, 쇼핑, 메일, 캘린더, 파일, Git, 코딩, 매크로, 외부 AI를 **하나의 개인용 Agent 실행환경**으로 묶는 것이 목표다.

---

## 개인용 프로젝트로 잡은 이유

이 저장소는 **비공개 + 1인 사용**을 전제로 한다.

따라서 공개 SaaS처럼 모든 사용자에게 맞춘 복잡한 설정이나 서버 구조보다 다음을 우선한다.

1. 내가 실제로 자주 쓰는 작업을 빠르게 자동화
2. 로컬 개인정보 보호
3. 8GB RAM에서도 버티는 가벼운 구조
4. API 비용을 가능한 한 줄이는 구조
5. 화면을 보면서 작업 상태를 이해할 수 있는 구조
6. 필요할 때만 무거운 기능을 로드

개인용이라고 해서 보안을 대충 하지 않는다. 오히려 로그인, 주소, 카드, 세션을 실제로 넣을 수 있는 브라우저이기 때문에 Vault 경계는 처음부터 따로 둔다.

---

## 대상 PC

Luma는 다음 정도의 PC에서도 돌아가는 것을 설계 기준으로 삼는다.

```text
CPU: Intel Core i5 13세대급
RAM: 8GB 사용 가능 기준
권장 RAM: 16GB
OS: Windows
GPU: 핵심 기능에는 외장 GPU 불필요
```

그래서 다음 선택을 했다.

### 하지 않는 것

- Chromium 전체 포크 상시 빌드/배포
- Electron 안에 Chromium 한 벌 더 번들
- 대형 로컬 LLM 상시 실행
- 매 단계마다 전체 스크린샷을 AI에게 전송
- 좌표 기반 매크로만 사용
- 숨겨진 백그라운드 예약 봇

### 대신 하는 것

- WinUI 3
- WebView2
- DOM/JavaScript 우선 자동화
- 의미 기반 매크로
- compact snapshot
- 필요할 때만 AI 호출
- Dev Workspace 지연 로드

---

## 현재 구현된 기능

### 브라우저

- [x] WinUI 3 앱
- [x] WebView2 기반 브라우징
- [x] WebView2 로컬 영구 프로필
- [x] 여러 탭
- [x] 주소창
- [x] 검색어 자동 Google 검색
- [x] 뒤로 / 앞으로 / 새로고침 / 홈
- [x] AI/자동화 사이드패널

### Agent / Automation

- [x] 현재 페이지 DOM Snapshot
- [x] 보이는 상호작용 요소만 추출
- [x] 의미 기반 클릭 기록
- [x] 일반 입력 기록
- [x] 탐색 기록
- [x] Macro Replay
- [x] Foreground-only Watch
- [x] 특정 텍스트 버튼/링크 활성화 감지
- [x] 선택적으로 자동 클릭
- [x] 간단한 한국어 로컬 명령 해석

### Privacy / Security

- [x] Password 입력값 매크로 기록 제외
- [x] 카드/CVC/CVV 계열 입력값 기록 제외
- [x] OTP/one-time-code 기록 제외
- [x] Windows DPAPI Vault primitive
- [x] SQLite 로컬 감사로그
- [x] 민감 행동 승인 정책 primitive

### AI 확장 구조

- [x] Provider 인터페이스
- [x] External AI Delegate 인터페이스
- [x] Vendor 독립적인 Provider Registry
- [ ] OpenAI 실제 adapter
- [ ] Claude 실제 adapter
- [ ] Gemini 실제 adapter
- [ ] Ollama adapter
- [ ] Gemini visible delegate

---

## Free-first가 핵심

Luma는 가능한 한 **AI를 안 써도 되는 일은 AI를 쓰지 않는다.**

우선순위는 다음과 같다.

```text
1. DOM / JavaScript 규칙
2. 이미 성공한 Macro
3. 사이트 Skill
4. Compact DOM/Accessibility Snapshot
5. 필요한 순간에만 작은 로컬 모델
6. 화면에 보이는 무료 Web AI Delegate
7. 정말 필요할 때만 유료 API
```

예를 들어 네이버 예약을 1시간 감시한다고 해서 3초마다 LLM을 호출할 이유가 없다.

```text
Luma가 활성창인가?
  ↓
대상 탭이 현재 화면에 보이는가?
  ↓
19:00 버튼이 존재하는가?
  ↓
활성화되어 있는가?
  ↓
아니면 다음 주기에서 다시 확인
```

이 과정은 전부 로컬 JS로 처리할 수 있다.

사이트 UI가 바뀌어서 기존 흐름이 깨졌을 때만 AI가 개입하는 구조가 목표다.

---

## Foreground-only Watch

이 기능은 일부러 백그라운드 서비스로 만들지 않는다.

현재 원칙:

```text
Luma 창이 활성 상태
+ 감시 대상 탭이 현재 선택되어 있음
= 감시 실행
```

Luma가 비활성화되면 실제 watch check가 수행되지 않는다.

사용자가 자동화가 돌아가는 화면을 볼 수 있는 상태를 기본값으로 둔다.

### 현재 사용 예시

Watch 입력란:

```text
19:00
```

Auto-click 체크:

```text
✓ Auto-click
```

그러면 활성화된 `19:00` 버튼/링크가 보일 때까지 감시하고, 찾으면 클릭할 수 있다.

로컬 명령으로도 가능하다.

```text
19:00 자리 생기면
watch 19:00
```

---

## Macro

Luma Macro는 좌표를 기록하지 않는다.

### 좋지 않은 방식

```text
x=820, y=430 클릭
```

화면 위치가 조금만 바뀌어도 깨진다.

### Luma 방식

```text
Navigate → 예약페이지
Click → button[aria-label="예약"]
Input → input[name="name"]
Click → #nextButton
```

DOM selector를 사용한다.

### 현재 기록하는 것

- navigate
- click
- input

### 앞으로 추가할 것

- select
- waitFor
- assert
- if/else
- loop
- retry
- download
- approval
- vault-fill
- delegate
- tool-call

---

## 개인정보 / Local Vault

현재 Vault primitive는 **Windows DPAPI CurrentUser**를 사용한다.

저장 위치:

```text
%LOCALAPPDATA%\Luma\Vault\
```

Logical key 자체도 파일명으로 직접 쓰지 않고 SHA-256 hash를 사용한다.

장기적으로 저장할 데이터 구조 예시:

```text
identity/personal
address/home
address/dorm
payment/personal-visa
login/naver-main
login/google-main
provider/openai
provider/anthropic
provider/gemini
```

### 카드정보 원칙

AI에게 카드번호를 프롬프트로 넘기지 않는다.

AI는 다음 정도만 알아야 한다.

```text
Personal Visa •••• 1234
```

실제 번호를 입력하는 것은 Local Secure Fill 계층이 한다.

```text
Agent
 ↓
usePayment("personal-visa")
 ↓
Local Vault
 ↓
브라우저 결제폼 직접 입력
```

**CVV/CVC는 저장 대상에서 제외한다.**

---

## Approval Layer

Luma는 조회와 실제 행동을 구분한다.

### 기본 자동 가능

- 페이지 읽기
- 검색
- 이동
- 상품 비교
- 다운로드
- 코드 읽기

### 기본 승인 필요

- 메일 전송
- 캘린더 생성/수정
- 예약 확정
- 결제
- 데이터 삭제
- Git push
- Shell 실행

개인용이므로 나중에는 다음 같은 규칙도 가능하다.

```text
trusted site
+ 30,000원 이하
+ 저장된 반복 구매
→ 자동 결제 허용
```

하지만 구조 자체에는 항상 승인 경계를 남긴다.

---

## 쇼핑 Agent 최종 목표

예시:

```text
나: 썬크림 찾아
```

Luma:

```text
1. 제품 A
   50ml
   17,900원
   내일 배송

2. 제품 B
   60ml
   21,000원
   무료배송

3. 제품 C
   50ml
   15,500원 + 배송비
```

이후:

```text
나: 1번 결제해
```

흐름:

```text
상품 페이지
→ 옵션 선택
→ 장바구니
→ 주문
→ 기존 사이트 등록 카드 확인
→ 없으면 Luma Payment Vault 사용
→ 배송지 자동입력
→ 금액/배송지/카드 마지막 확인
→ 결제 승인
→ 추가 카드사 인증이 있으면 사용자에게 넘김
→ 인증 후 다시 이어서 주문 완료 확인
```

---

## 예약 Agent 최종 목표

예시:

```text
"10월 20일 19시 2명 자리 생기면 예약해"
```

예상 Planner 결과:

```text
visible_only: true
site: current page
condition:
  date: 2026-10-20
  time: 19:00
  people: 2

on_available:
  - click available slot
  - replay reservation-details macro
  - secure-fill contact
  - approval: confirm-reservation
```

반복 감시 자체에는 모델을 사용하지 않고, UI 변경이나 예외 상황에서만 self-healing AI가 개입하는 것이 목표다.

---

## 메일 / 캘린더 / 파일

장기적으로 Agent가 사용할 Tool 예시:

```text
mail.send
calendar.create
calendar.update
contacts.lookup
files.read
files.write
files.move
downloads.organize
```

예:

```text
"교수님한테 내일 연구실 못 간다고 메일 보내고 일정에도 표시해"
```

Agent:

```text
calendar 확인
→ 문맥 파악
→ 메일 초안
→ 발송 승인
→ 메일 발송
→ 캘린더 상태 업데이트
```

---

## External AI Delegate

API Provider와 Web AI Delegate는 별도 개념으로 둔다.

향후 예:

```text
Main Luma Agent
 ├─ Gemini Delegate
 ├─ Claude Delegate
 └─ Local/remote providers
```

사용 예:

```text
"이 에러 네가 먼저 보고 제미나이한테도 물어봐"
```

흐름:

```text
Luma 코드/로그 분석
→ Gemini 화면 열기
→ 필요한 비밀정보 제거
→ 문제 설명 전달
→ 결과 회수
→ Luma 판단과 비교
→ 최종 수정
```

Web AI를 숨겨진 API처럼 사용하는 것이 아니라 **화면에 보이는 delegate 작업공간**으로 다루는 방향이다.

---

## Dev Workspace

코딩 기능은 항상 켜두지 않는다.

8GB RAM 기준이기 때문에 실제 코딩 작업을 요청했을 때만 로드하는 구조가 목표다.

```text
Dev Workspace
├─ Monaco Editor
├─ PowerShell
├─ Git
├─ Preview tab
└─ Coding Agent tools
```

작업이 없을 때는 unload한다.

향후 지원할 명령 예:

```text
"이 GitHub 프로젝트 받아서 실행해"
"오류 고쳐"
"페이지 디자인 지금 보고 있는 사이트 느낌으로 바꿔"
"테스트 돌려"
"제미나이에게 한번 검토시켜"
"괜찮으면 커밋해"
```

Git push는 기본 승인 대상이다.

---

## 기술 스택

| 영역 | 기술 | 선택 이유 |
|---|---|---|
| UI | WinUI 3 | Windows 네이티브 UI, 가벼운 구조 |
| 브라우저 | WebView2 | 시스템 Chromium 활용, Chromium 별도 번들 불필요 |
| 언어 | C# / .NET 8 | Windows API/비동기/로컬 서비스 구현 편리 |
| 로컬 DB | SQLite | 작고 안정적인 로컬 기록 저장 |
| Secret | Windows DPAPI | 현재 Windows 사용자 단위 암호화 |
| Automation | JavaScript / DOM | 빠르고 무료, LLM 불필요 |
| AI | Provider abstraction | OpenAI/Claude/Gemini/Ollama 종속 제거 |
| CI | GitHub Actions | Windows 빌드 검증 |

2026년 10월 기준 Windows App SDK 2.5 stable 계열을 대상으로 설정했다.

---

## 프로젝트 구조

```text
Luma/
├─ .github/workflows/build.yml
├─ docs/
│  ├─ ARCHITECTURE.md
│  ├─ SECURITY.md
│  └─ ROADMAP.md
├─ src/Luma/
│  ├─ Models/
│  │  └─ AutomationModels.cs
│  ├─ Services/
│  │  ├─ AiProviders.cs
│  │  ├─ ApprovalPolicy.cs
│  │  ├─ AuditLogService.cs
│  │  ├─ BrowserScripts.cs
│  │  ├─ ForegroundWatchService.cs
│  │  ├─ LocalIntentRouter.cs
│  │  ├─ MacroService.cs
│  │  ├─ SecureVaultService.cs
│  │  └─ SnapshotService.cs
│  ├─ App.xaml
│  ├─ App.xaml.cs
│  ├─ MainWindow.xaml
│  ├─ MainWindow.xaml.cs
│  ├─ app.manifest
│  └─ Luma.csproj
├─ Luma.sln
├─ README.md
└─ README.ko.md
```

---

## 요구 환경

개발 권장:

- Windows 10 1809 이상
- x64
- .NET 8 SDK
- Visual Studio + WinUI / Windows App SDK tooling 또는 동등한 CLI 환경
- Microsoft Edge WebView2 Runtime
- Windows App Runtime

---

## 개발

```powershell
git clone https://github.com/homesweetlove/Luma.git
cd Luma
```

Restore:

```powershell
dotnet restore .\src\Luma\Luma.csproj -p:Platform=x64
```

Debug Build:

```powershell
dotnet build .\src\Luma\Luma.csproj -c Debug -p:Platform=x64
```

Release Build:

```powershell
dotnet build .\src\Luma\Luma.csproj -c Release -p:Platform=x64
```

---

## 현재 간단 사용법

### Snapshot

상단 **Snapshot** 버튼 또는:

```text
스냅샷
화면 읽어
페이지 읽어
snapshot
```

### Macro 녹화

```text
매크로 녹화
```

또는 `● Record`.

종료:

```text
녹화 끝
```

재생:

```text
매크로 재생
```

### Watch

```text
19:00 자리 생기면
```

또는:

```text
watch 19:00
```

---

## 로컬 데이터 위치

```text
%LOCALAPPDATA%\Luma\
```

현재 주요 데이터:

```text
WebView2Profile\  로그인/쿠키 등을 포함한 브라우저 프로필
Vault\            DPAPI 암호화 blob
luma.db           감사로그 SQLite DB
```

이 데이터는 Git에 올리지 않는다.

---

## 보안 원칙

자세한 내용은 [SECURITY.md](docs/SECURITY.md) 참고.

핵심:

- 비밀번호를 AI prompt에 넣지 않기
- 카드번호를 AI prompt에 넣지 않기
- CVV 저장 안 하기
- OTP 저장 안 하기
- Macro에 secret 값 기록 안 하기
- 외부 AI에는 필요한 문맥만 전달
- 결제/예약/메일/삭제/쉘/Git push는 승인 경계 유지

---

## 참고한 오픈소스 아이디어

구조와 아이디어를 조사할 때 참고한 프로젝트:

- BrowserOS
- Echo
- ego-lite
- Nanobrowser
- Automa
- Stagehand
- OpenBrowser
- Microsoft Playwright MCP
- PiDurableKit / pi-durable

Luma는 특정 프로젝트 하나를 복제하는 것이 아니라, 필요한 아이디어를 참고해 **Windows 개인용 저사양 친화 구조로 새로 설계**하는 프로젝트다.

---

## 프로젝트 범위

이 프로젝트는 개인 비공개 사용용이다.

하지 않는 것:

- CAPTCHA 우회
- 2FA/passkey 우회
- 카드사 인증 우회
- 숨겨진 백그라운드 예약 봇
- 자격증명 추출
- CVV 저장
- 무조건적인 자동 결제
- 대형 로컬 LLM 상시 실행

---

## 문서

- [Architecture](docs/ARCHITECTURE.md)
- [Security & Privacy](docs/SECURITY.md)
- [Roadmap](docs/ROADMAP.md)
- [English README](README.md)

---

<div align="center">

### Luma

**평소에는 가벼운 브라우저. 필요할 때는 일을 끝내주는 개인 Agent.**

</div>
