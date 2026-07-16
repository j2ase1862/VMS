# 현장 설치 체크리스트 — BODA.VMS.Web + VMS

한 PC(또는 현장 네트워크)에 웹 서버와 VMS 데스크탑을 함께 설치할 때의 표준 순서.
**Web 먼저 → VMS 나중** — Web SSO 를 켤 계획이면 이 순서를 반드시 지킨다.
(SSO 를 안 쓰면 역순도 동작하지만, 마법사에서 Web URL 을 바로 검증할 수 있도록 이 순서를 권장)

상세 근거: [msi_build_guide.md](msi_build_guide.md) §9~§11,
BODA.VMS.Web `docs/Production_Deploy_Runbook.md`

---

## 0. 사전 준비물

- [ ] VMS MSI 설치 파일 — [GitHub Releases](https://github.com/j2ase1862/VMS/releases) 최신 버전 (v1.4.7 기준 작성; 릴리즈 미발행 버전은 USB 패키지의 `VMS-<버전>.msi` 사용)
- [ ] BODA.VMS.Web 게시본 (`dotnet publish -c Release -r win-x64 --self-contained true` 산출물)
- [ ] 비밀번호 사전 준비 (모두 **서로 다르게**, 12자 이상 권장 — msi_build_guide §11.3):
  - [ ] Web admin 초기 비밀번호 (`Initial__AdminPassword`)
  - [ ] VMS admin 비밀번호 (마법사 입력) — **SSO 사용 시 불필요** (v1.4.5 부터 SSO 체크 시 입력란 비활성, admin 로그인은 Web 계정 사용)
  - [ ] VMS local-admin 비밀번호 (마법사 입력 — SSO 여부와 무관하게 항상 필요)
  - [ ] JWT 서명 키 32자 이상 (`Jwt__Key`) — Web 서버 전용. AppSetup 에는 입력란 없음
  - [ ] (API Key enforcement 사용 시) `ClientApiKey__Value` — 각주 [1] 참고
- [ ] 현장 PC 에서 관리자 PowerShell 실행 가능 확인

---

## 1. BODA.VMS.Web 서버 설치 (먼저)

### 1-1. 환경변수 설정 (관리자 PowerShell, **서비스 첫 가동 전**)

```powershell
setx Initial__AdminPassword "<Web admin 비밀번호>" /M
setx Jwt__Key "<32자 이상 랜덤 키>" /M
# 선택 — API Key enforcement 사용 시
# setx ClientApiKey__Value "<API 키>" /M
```

- [ ] `Initial__AdminPassword` 설정 — **미설정 시 Web 이 부팅 자체를 차단** (Option C)
- [ ] `Jwt__Key` 설정 (32자 미만이면 시작 검증 실패)

### 1-2. 게시본 배치 + Windows Service 등록

```powershell
$dir = "C:\Deploy\BodaVmsWeb"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
robocopy <게시본 폴더> $dir /E

sc.exe create BodaVmsWeb binPath= "`"$dir\BODA.VMS.Web.exe`" --environment Production" start= auto DisplayName= "BODA VMS Web Server"
sc.exe description BodaVmsWeb "BODA Vision Management System - Web Server"
sc.exe failure BodaVmsWeb reset= 86400 actions= restart/10000/restart/30000/restart/60000
Start-Service BodaVmsWeb
```

- [ ] 서비스 `BodaVmsWeb` RUNNING 확인 (`Get-Service BodaVmsWeb`)

### 1-3. Web 검증

- [ ] `Invoke-WebRequest http://localhost:5292/health` → **200** (503 이면 DB 미준비)
- [ ] 브라우저 `http://localhost:5292` → admin + 준비한 비밀번호로 로그인 성공
- [ ] 서비스가 즉시 멈추면: 이벤트 뷰어 + `Logs/boda-vms-*.json` 확인
      (대표 원인: `Initial__AdminPassword` 미설정 → 부팅 차단 메시지)

---

## 2. VMS 데스크탑 설치

### 2-1. MSI 설치

- [ ] MSI 실행 — 파일명 `VMS-<버전>.msi` (v1.4.6 부터 파일명에 버전 명시)
      (기존 설치 PC 는 업그레이드로 덮어씀 — 별도 제거 불필요)

### 2-2. AppSetup 마법사 1회 실행 — **업데이트 설치에서도 생략 금지**

> ⚠ **현장 검증 사례 (2026-07-16)**: 업그레이드 설치 후 마법사를 건너뛰고 VMS 를 바로 실행하면
> **"보안 모드가 명시되지 않아 앱을 시작할 수 없습니다"** 로 부팅이 차단될 수 있다
> (구버전 config 에 `securityMode` 키가 없는 경우). 표준 순서는 항상 **MSI → 마법사 → VMS**.
> 마법사 없이 즉시 복구하려면: `setx BODA_VMS_SECURITY_MODE Production /M` 후 VMS 재실행.

- [ ] 시작 메뉴 → **"BODA VMS 설정 마법사 (AppSetup)"** 실행 (v1.4.6 부터 바로가기 제공.
      이전 버전은 `C:\Program Files\VASIM\BODA Vision System\VMS.AppSetup.exe` 직접 실행)
- [ ] 시작 직후 **"설정 로드 실패"** 창이 뜨는지 확인 (v1.4.7):
      뜨면 기존 설정 파일이 손상/비호환 상태 — **저장하지 말고** 창에 표시된
      `system_config.json.invalid.bak` 파일을 수거해 보고. 그대로 저장하면 기존 설정이 기본값으로 덮어써짐
- [ ] 기존 설정 PC 는 각 페이지 값이 이전 설정대로 복원되어 있는지 확인 (마법사 = 기존 설정 편집기)

- [ ] Page 2 "Web Server Integration" — Web URL 입력 (`http://<host>:5292`)
- [ ] (선택) Page 2 "Web Client API Key" — enforcement 사용 시에만 입력, 각주 [1] 참고
- [ ] (SSO 사용 시) Page 2 "Web SSO" 카드 — "Web SSO 활성" 체크
      (v1.4.5 부터 체크 시 admin 비밀번호 입력란이 자동 비활성 + 안내 문구 표시 — 정상 동작)
- [ ] Page 2 "Initial Admin Passwords" 입력:
  - SSO 미사용 → VMS admin / local-admin 비밀번호 **2개** 입력
  - SSO 사용 → **local-admin 만** 입력 (admin 입력란은 비활성 — admin 은 Web 계정으로 로그인)
- [ ] 마지막 7단계 Security Mode — **Production** 유지 → Finish
- [ ] 저장 메시지 확인 (v1.4.1 부터 VMS 선실행 없이도 마법사가 DB 생성 + 시드 — PR #180):
  - SSO 미사용 신규 install → **"✓ admin 계정 초기 시드 완료"** + **"✓ local-admin 비밀번호 변경 완료"**
  - SSO 사용 → **"✓ local-admin 비밀번호 변경 완료"** 만 표시 (admin 시드 없음 — 정상)
  - **"⚠ admin 계정이 이미 존재하여 …"** 표시 → 재실행/업데이트 상황. admin 비밀번호는 바뀌지 않았음 —
    변경이 필요하면 VMS 로그인 후 [사용자 관리] 에서 (v1.4.5 부터 표시, 이전 버전은 무통보)
  - **"⚠ … 8자 미만 …"** 표시 → 해당 비밀번호 미적용. 다시 실행해 8자 이상으로 입력

### 2-3. VMS 검증

- [ ] VMS 부팅 → **"보안 정책 오류" 메시지 없음**
      — 뜨면 2-2 마법사 실행 누락이 대표 원인. 재발 시 **마법사를 실행한 것과 같은 Windows
      계정인지** 확인 (설정은 사용자별 `%LocalAppData%` 저장 — 계정이 다르면 빈 설정으로 보임)
- [ ] admin 로그인 성공 (SSO 활성 시 Web admin 계정으로)
- [ ] 헬스 체크 UI → `Mode=Production, Source=ConfigFile` (또는 `Source=Environment`)
      — `Source=FallbackOnError` 면 보안 모드 명시 누락 (msi_build_guide §9)

---

## 3. SSO 마무리 (SSO 사용 시 — msi_build_guide §10.4 필수)

- [ ] VMS 로그인 화면 → `local-admin` + **마법사에서 입력한 비밀번호**로 1회 로그인 (폴백 진입 확인)
      — 마법사에서 비워뒀다면 디폴트 `vasim1234` 로 로그인 후 사용자 관리 UI 에서 **즉시 강한 비밀번호로 변경**
- [ ] Web admin 계정으로 재로그인 → 감사 로그에 "Web SSO ok" 기록 확인
- [ ] (선택) Web 서비스 일시 중지 → 일반 admin 로그인 거부 + local-admin 폴백 진입 확인 → 서비스 재시작

---

## 4. 연동 확인

- [ ] Web 화면(클라이언트 목록)에서 VMS 클라이언트 자동 등록 + 하트비트(5초 주기) 확인
- [ ] VMS 검사 1회 실행 → Web 검사 이력에 결과 업로드 확인

---

## 실패 시 수거 (보고용)

| 대상 | 수거 항목 |
|------|----------|
| VMS | 오류 메시지 전문 + `%LocalAppData%\BODA VISION AI\system_config.json` 내용 |
| Web | 이벤트 뷰어 오류 + `<게시 폴더>\Logs\boda-vms-{날짜}.json` 당일분 |

---

## 각주

**[1] Client API Key 는 비워도 된다 (호환 모드)**
AppSetup 의 "Web Client API Key" 를 비우면 VMS 가 `X-API-Key` 헤더를 아예 보내지 않는데,
Web 서버 기본 설정(`ClientApiKey:Required=false`, 호환 모드)에서는 그대로 통과하므로
하트비트·검사 결과 업로드·센서 전송 모두 정상 동작한다. Web 쪽에서 enforcement
(`Required=true`)를 켠 경우에만 401 로 거부되어 Web 연동이 끊긴다 (VMS 자체는 정상 부팅,
로컬 검사 가능). 단, 키를 **틀리게** 입력하면 Required 값과 무관하게 401 — 비우거나, 넣으려면
Web 서버의 `ClientApiKey__Value` 와 정확히 일치시킬 것.

---

## 변경 이력

| 날짜 | 내용 |
|------|------|
| 2026-07-15 | 최초 작성 — v1.4.1 현장 검증 대비, Web→VMS 설치 순서 표준화 |
| 2026-07-16 | v1.4.5 반영 — SSO 체크 시 admin 입력란 비활성(PR #190), 저장 메시지 분기(✓/⚠) 판독 기준, admin 미적용 재실행 안내 추가 |
| 2026-07-16 | v1.4.7 반영 — 현장 검증 결과(업그레이드 후 보안 정책 오류 부팅 차단) 트러블슈팅 반영, 시작 메뉴 AppSetup 바로가기(#192), MSI 파일명 버전 명시(#193), 설정 로드 실패 경고·.invalid.bak 대응(#194) |
