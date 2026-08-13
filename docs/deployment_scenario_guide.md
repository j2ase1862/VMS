# VMS 구성 시나리오 선택 가이드 (올인원 vs 클라우드 분리)

문서 버전: v1.0 (2026-08-14, v1.5.12 기준)

> 관련 문서: MSI 빌드·Web 동봉 상세 [msi_build_guide.md](msi_build_guide.md) §13 ·
> 현장 설치 체크리스트 [field_install_checklist.md](field_install_checklist.md) ·
> Web 운영 배포 런북 `BODA.VMS.Web/docs/Production_Deploy_Runbook.md`

## 한눈에 보기

VMS 는 Database 에 직접 접근하지 않는다. 모든 Web 연동(SSO 로그인, 레시피 원장,
파라미터 연동, 검사 이력 업로드, SignalR 실시간 반영)은 `system_config.json` 의
**`webServerUrl` 하나**를 통한 HTTP + SignalR 이다. Database(SQLite `BodaVision.db`)는
**Web 서버가 돌아가는 PC** 에 있고 Web 만 접근한다. 따라서 Web 을 어디에 두느냐가
곧 구성 시나리오다.

| | **A. 올인원 (단일 PC)** | **B. 클라우드/서버 분리** |
|---|---|---|
| 대상 현장 | 인터넷 없는 오프라인 현장, 1대 운영 | 중앙 관제, 여러 라인/현장 PC 공유 |
| Local PC | VMS + 동봉 Web + DB | **VMS 만** (동봉 Web 미가동) |
| Web/DB 위치 | 같은 PC | Server PC (예: boda-vms.com — Cloudflare Tunnel) |
| webServerUrl | `http://localhost:5292` | `https://boda-vms.com` |
| http/https | loopback 은 http 허용 (Production 포함) | **원격은 https 필수** (`InsecureUrlGuard` 가 http+원격을 차단) |
| MSI 설치 | 기본 설치 (WebServer Feature 포함) | 기본 설치 후 Web 서비스 안 켬, 또는 `INSTALLWEB=0` |

두 시나리오 모두 **같은 MSI 하나**로 커버된다. 동봉 Web 서비스(`BodaVmsWeb`)는
demand(수동) 등록 + 미시작이 기본이라, AppSetup 에서 명시적으로 초기 구성을 실행하기
전에는 아무것도 돌지 않는다.

## A. 올인원 (단일 PC) 설치 순서

1. MSI 기본 설치 (WebServer Feature 포함됨)
2. AppSetup 마법사 → "Web 서버 초기 구성 (이 PC)" 카드 → admin 비밀번호 입력 →
   [초기 구성 실행] (Jwt Key 생성 + 서비스 auto 전환·시작, msi_build_guide §13.3)
3. AppSetup 의 `WebServerUrl` 은 기본값 `http://localhost:5292` 그대로
4. 확인: `http://localhost:5292` admin 로그인 + VMS 로그인(SSO 사용 시)

## B. 클라우드/서버 분리 설치 순서

**Server PC (1회):** Web 설치·운영은 Production_Deploy_Runbook 를 따른다
(BodaVmsWeb 서비스 + cloudflared Tunnel, DB 는 `C:\ProgramData\BODA\VMS\BodaVision.db`).

**각 현장 Local PC:**

1. MSI 설치 — 둘 중 하나:
   - 기본 설치 후 **"Web 서버 초기 구성" 카드를 실행하지 않음** (서비스 미시작 유지), 또는
   - `msiexec /i VMS-<버전>.msi INSTALLWEB=0` 으로 Web 자체를 제외
2. AppSetup 마법사에서:
   - `WebServerUrl` = `https://boda-vms.com` (Production 보안 모드에서 원격 http 는 시작 거부됨)
   - `ClientIndex` = **PC 마다 겹치지 않게** 지정 (Web 이 라인을 구분하는 키 —
     설비 현황/Fleet 화면·레시피 내려받기가 이 번호 기준)
3. 확인: VMS 시작 → Web 로그인(SSO) 성공, Web `/clients` 에서 해당 ClientIndex 가
   online 으로 보이면 정상 (SignalR 이 Tunnel 을 통과한다는 뜻)

## 공통 주의사항

- **시나리오 B 에서 동봉 Web 서비스를 켜지 말 것** — 켜면 Local 에 별도 DB 가 생겨
  데이터가 서버와 갈라진다. 기본값(미시작)을 건드리지 않으면 안전.
- 레시피 주의: VMS 는 자기 `ClientIndex` 의 레시피만 내려받는다 — Web 에서 레시피를
  만들 때 대상 라인을 정확히 선택할 것 (RecipeFormDialog 에 대상 라인 배너 표시됨).
- 시나리오 전환(A→B): Local 의 `BodaVision.db` 를 Server 로 옮기고(서비스 중지 상태에서
  .db/-wal/-shm 함께 복사), Local Web 서비스는 중지·비활성화, `webServerUrl` 만 변경.
- Web 만 업데이트할 때: A 는 다음 VMS MSI 업그레이드에 포함되길 기다리거나
  `Install-Web-Offline.ps1` 단독 업데이트, B 는 Server 에서 런북 배포만 하면 전 현장 반영.

## 변경 이력

| 버전 | 일자 | 주요 변경 |
|---|---|---|
| v1.0 | 2026-08-14 | 최초 작성 — v1.5.12(Web 동봉 + 파라미터 연동) 기준 두 시나리오 정리 |
