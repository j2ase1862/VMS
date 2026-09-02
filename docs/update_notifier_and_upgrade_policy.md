# In-app 업데이트 알림 & MSI 업그레이드 정책

문서 버전: v1.1 (2026-08-14 — `release_and_update_guide_v1.0.md` 에서 유효 내용만 분리)

> 관련 문서:
> - 릴리즈 발행 절차(6단계): [release_manual_procedure.md](release_manual_procedure.md)
> - MSI 빌드·설치 구성: [msi_build_guide.md](msi_build_guide.md)
> - 코드 서명: [gs_msi_code_signing_guide.md](gs/guides/gs_msi_code_signing_guide.md)
>
> 전신 문서(`release_and_update_guide_v1.0.md`)의 Phase A(태그 push 자동 발행,
> release.yml)는 **PR #255 로 폐지**되어 본 문서에 승계하지 않았다 — 발행은
> `release_manual_procedure.md` 의 수동 6단계가 유일 표준이다.

---

## 1. In-app 업데이트 알림 체커

### 1.1 동작 흐름

```
앱 시작
  ↓ (mainWindow.Show 직후)
GitHubUpdateService.CheckAsync()          # best-effort, 10s 타임아웃
  ↓
응답 OK & 새 버전 ?
  ├─ Yes → MainViewModel.LatestUpdate 저장 → 사이드 패널 배지 자동 노출
  └─ No / 실패 → 무동작 (silent)

사용자 클릭 흐름:
  배지 클릭         → ShowUpdateDetails    → 다이얼로그
  Check for updates → CheckForUpdates     → 다이얼로그 (결과 명시)
  Confirm "다운로드"  → ProcessService     → 기본 브라우저로 ReleaseUrl 열기
                                              → 사용자가 직접 MSI 다운로드 후 실행
```

조회 엔드포인트 (v1.5.2 저장소 분리 이후 — 배포 전용 public repo):

```
GET https://api.github.com/repos/j2ase1862/VMS-Releases/releases/latest
Accept: application/vnd.github+json
```

응답 핵심 필드: `tag_name`(vX.Y.Z) · `html_url`(다운로드 페이지) · `body`(릴리스 노트,
plain text 표시) · `assets[]` · `published_at`.

### 1.2 코드 / 파일 위치

| 책임 | 파일 |
|---|---|
| 인터페이스 | `VMS.Core/Interfaces/IUpdateService.cs` |
| 결과 모델 | `VMS.Core/Models/Updates/UpdateInfo.cs` |
| GitHub API 호출 + 비교 | `VMS.Core/Services/GitHubUpdateService.cs` |
| ViewModel 통합 | `VMS\ViewModels\MainViewModel.cs` (`#region Update Notifier`) |
| 사이드 패널 UI | `VMS\Views\MainWindow.xaml` (`Updates` 섹션) |
| DI + 시작 시 트리거 | `VMS\App.xaml.cs` (`GitHubUpdateService("j2ase1862", "VMS-Releases")` 생성 + `CheckForUpdatesSilentAsync()` fire-and-forget) |
| 단위 테스트 | `VMS.Core.Tests/Services/GitHubUpdateServiceTests.cs` |

### 1.3 UI 노출 조건

| 요소 | 노출 조건 |
|---|---|
| "Updates" 섹션 헤더 | 항상 |
| **배지** ("새 버전 vX.Y.Z") | `MainViewModel.IsUpdateAvailable == true` (= `LatestUpdate.LatestVersion > CurrentVersion`) |
| **Check for updates** 버튼 | 항상 |

### 1.4 다이얼로그 문구

- **새 버전 감지**:
  ```
  새 버전 vX.Y.Z 이 출시되었습니다.
  현재 버전: vA.B.C
  최신 버전: vX.Y.Z

  <release notes (최대 600자, 초과 시 ... 트렁케이트)>

  다운로드 페이지를 열까요?
  ```
- **최신 상태**: `이미 최신 버전입니다 (vA.B.C).`
- **통신 실패**: `업데이트 정보를 가져올 수 없습니다. 네트워크 상태 또는 GitHub 접근 가능 여부를 확인해 주세요.`

### 1.5 설계 결정 (왜 이렇게?)

| 결정 | 이유 |
|---|---|
| 통신/파싱 실패 시 **예외 무발산** + `null` 반환 | 시작 시 silent check 가 운영 라인을 멈추지 않도록 |
| **InformationalVersion** 우선 사용(어셈블리 버전 폴백) | `Directory.Build.props` 의 `<Version>` 이 자동 반영 |
| **pre-release / build metadata 거부** | semver 정렬 비교가 안전하지 않음. 안정 태그만 발행 |
| **MajorUpgrade** 신뢰 — 별도 in-app 다운로드/설치 자동화 안 함 | 산업 환경에서 *명시적 사용자 동의* 가 안전. SmartScreen 노출도 사용자 인지 1회로 끝 |
| API rate limit: **익명 60 req/h/IP** 충분 | VMS 한 대당 매 시작 1회 호출 |

---

## 2. MSI 업그레이드 정책

### 2.1 MajorUpgrade

- `Package.wxs` 의 `<MajorUpgrade DowngradeErrorMessage="최신 버전이 이미 설치되어 있습니다." />` 가 활성.
- 새 MSI 실행 시 기존 버전을 **자동 제거 후 재설치**. 사용자 별도 액션 불필요.
- `UpgradeCode` 는 **절대 변경 금지** (현재 `A70E988A-739F-4CDC-BE08-EBFE03DACA32`).
  변경하면 in-place 업그레이드가 깨지고 두 버전이 병존 설치됨.

### 2.2 사용자 데이터 보존

업그레이드 시 `%LocalAppData%\BODA VISION AI\` 의 다음 자산은 그대로 유지:

- `system_config.json`, `layout_config.json`, `plc_signals.json`
- `BodaVision.db` (사용자 / BCrypt 해시)
- `recipes/`, `audit/`, `upload_queue/`

MSI 는 `INSTALLFOLDER`(Program Files) 만 교체. `%LocalAppData%` 는 미터치.
동봉 Web 서버의 DB(`C:\ProgramData\BODA\VMS`)·`appsettings.Production.json` 보존은
[msi_build_guide.md](msi_build_guide.md) §13.3 참조.

### 2.3 운영 시점 권장

- **업그레이드 권장 시점**: 운영 종료 후 / 야간 / 라인 정지 시간.
- **운영 중 알림이 떠도 즉시 설치 강제 X** — 사용자가 Confirm 다이얼로그를 닫으면 다음 시작까지 무동작.
- 큰 변경(MAJOR 또는 사용자 영향 큰 MINOR)은 운영자에게 사전 공지 권장 — Release notes 에 명시.

---

## 3. 트러블슈팅

### 3.1 알림이 안 뜸

| 체크 항목 | 확인 방법 |
|---|---|
| 인터넷 연결 | `curl -s https://api.github.com/repos/j2ase1862/VMS-Releases/releases/latest` |
| 현재 어셈블리 버전 | `(Get-Item VMS.exe).VersionInfo.FileVersion` |
| 새 release 가 실제 더 높은가 | `gh release view --repo j2ase1862/VMS-Releases --json tagName` vs 위 버전 |
| API 응답 정상 여부 | 위 curl 응답에 `tag_name`, `html_url`, `assets` 필드 존재 |

체크해도 원인 불명이면 `%LocalAppData%\BODA VISION AI\audit\` 의 최근 로그에서 `[UpdateCheck]` 키워드 검색.

### 3.2 알림은 떴는데 브라우저가 안 열림

- `ProcessService.LaunchProcess(url)` 가 `UseShellExecute = true` 로 호출 — 시스템 기본 브라우저가 등록되어 있어야 함.
- 실패 시 다이얼로그에서 수동 URL 노출(`업데이트 페이지 열기 실패` 메시지 + 클립보드 복사 가능 URL).

### 3.3 MajorUpgrade 가 동작 안 함 (두 버전 병존)

- `Package.wxs` 의 `UpgradeCode` 가 변경됐을 가능성 — §2.1 참고.
- WiX log: `msiexec /i VMS-1.x.y.msi /L*v upgrade.log` 후 `RemoveExistingProducts` 시퀀스 확인.

### 3.4 인앱 설치 중 ".NET Desktop Runtime 을 다운로드하시겠습니까?" 대화상자 → 이전 버전이 그대로 실행됨

- 증상: 다운로드는 끝났는데 런타임 다운로드 대화상자가 뜨고, 예/아니오와 무관하게 곧바로
  **이전 버전** VMS 가 다시 실행된다. `%ProgramData%\BODA\VMS\update-bootstrap.log` 에
  `install exit code: -2147450749` (0x80008083 = hostfxr 부재) + `install FAILED - previous version remains`.
- 원인: v1.25.0 부터 VMS.Updater 가 self-contained 인데, 부트스트랩이 임시 폴더로 `VMS.Updater.*` 와
  CommunityToolkit.Mvvm.dll 만 복사해 실행했다. self-contained 호스트는 자기 옆 폴더에서만 런타임을
  찾으므로(전역 .NET 설치 여부와 무관) 대화상자를 띄우고 종료했고, MSI 는 실행조차 되지 않았다.
- 수정 (v1.28.0, `UpdateInstallService`): ① 설치 폴더의 `VMS.Updater.deps.json` 에서 런타임 팩 파일
  목록(약 238개·151MB)을 읽어 함께 복사 + 설치본에 hostfxr.dll 이 있는데 복사되지 않았으면 업데이터
  대신 msiexec 로 진행, ② 업데이터 종료 코드가 MSI 결과 범위(0~3010) 밖이면 msiexec /passive 로 재시도.
- **주의**: 부트스트랩 스크립트는 *실행 중인 구버전* VMS 가 만들므로, **v1.25.0~v1.27.0 이 설치된 PC 는
  한 번은 MSI 를 직접 받아 설치**해야 한다. 그 뒤부터 인앱 업데이트가 정상 동작한다.

---

## 4. 변경 이력

| 버전 | 일자 | 주요 변경 |
|---|---|---|
| v1.2 | 2026-09-02 | §3.4 self-contained 업데이터 스테이징 사고(런타임 대화상자 → 이전 버전 재실행) 원인·수정·수동 설치 1회 필요 안내 |
| v1.1 | 2026-08-14 | `release_and_update_guide_v1.0.md` 에서 유효 내용(Phase B·업그레이드 정책·트러블슈팅)만 분리. 폐지된 Phase A(release.yml)·구 repo URL 예시 제거, 엔드포인트를 VMS-Releases 로 갱신 |
| v1.0 | 2026-06-01 | (전신) release_and_update_guide 최초 작성 |
