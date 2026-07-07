# 릴리스 발행 & 자동 업데이트 알림 가이드 (BODA Vision AI)

문서 버전: v1.0
대상 빌드: master @ 2026-06-01 (v1.2.0)
범위: 새 버전 MSI 의 GitHub Release 자동 발행(Phase A) + VMS in-app 업데이트 알림 체커(Phase B)

> 관련 문서:
> - MSI 빌드 자체는 [msi_build_guide.md](msi_build_guide.md)
> - 코드 서명은 [gs_msi_code_signing_guide.md](gs/guides/gs_msi_code_signing_guide.md) (미적용 상태)

---

## 1. 시스템 개요

릴리스 파이프라인은 두 구성으로 나뉜다.

| 구성 | 책임 | 위치 |
|---|---|---|
| **Phase A — Release 발행** | `v*` 태그 push 시 MSI 빌드 + GitHub Release 자동 생성 + 자산(MSI/SHA-256) 첨부 | `.github/workflows/release.yml` |
| **Phase B — In-app 알림 체커** | VMS 가 시작 시 GitHub Releases API 조회 → 새 버전 감지 시 사이드 패널 배지 + 다이얼로그 안내 | `VMS.Core/Services/GitHubUpdateService.cs` + `VMS\ViewModels\MainViewModel.cs` + `VMS\Views\MainWindow.xaml` |

두 구성의 단일 진실 공급원은 **GitHub Releases**. 별도 매니페스트 서버 / CDN 불필요. Public 레포(`j2ase1862/VMS`)라 익명 호출 가능.

```
┌────────────────────────────┐
│  Developer                 │
│  ── Directory.Build.props  │
│     <Version> bump         │
│  ── git tag v1.x.y         │
│  ── git push origin v1.x.y │
└──────────┬─────────────────┘
           │ push
           ▼
┌────────────────────────────┐
│  release.yml (GH Actions)  │
│  ── dotnet build -p:Version│
│  ── MSI + SHA-256          │
│  ── Create GH Release      │
└──────────┬─────────────────┘
           │ assets
           ▼
┌────────────────────────────┐        ┌──────────────────────────┐
│  https://github.com/.../   │  ◀──── │  VMS (운영 PC)            │
│  releases/latest           │  HTTPS │  GitHubUpdateService     │
│  → tag_name / body / assets│        │  ── 시작 시 silent check │
└────────────────────────────┘        │  ── 사이드 패널 배지     │
                                      └──────────────────────────┘
```

---

## 2. 사전 요구

| 항목 | 값 |
|---|---|
| 레포지토리 가시성 | **public** (익명 API 호출 가능) |
| 단일 버전 소스 | `Directory.Build.props` 의 `<Version>` |
| MSI 버전 동기화 | `VMS.MasterSetup.wixproj` 가 `$(Version)` → WiX preprocessor `BuildVersion` 으로 전달 → `Package.wxs` `Version="$(var.BuildVersion)"` |
| 태그 형식 | `vMAJOR.MINOR.PATCH` (예: `v1.2.0`) |
| pre-release / build metadata | **미지원** (`v1.2.3-rc1`, `v1.2.3+build` 거부) — 안정 태그만 |
| CI runner | `windows-2025` (.NET 8 + WiX 6) |

---

## 3. 새 버전 발행 절차 (운영자 시나리오)

### 3.1 권장 흐름 — PR + 태그

다음은 v1.2.0 → v1.3.0 발행 예시.

**Step 1: 버전 bump PR**
```bash
git checkout -b chore/release-v1.3.0 origin/master
# Directory.Build.props 의 <Version>, <AssemblyVersion>, <FileVersion> 수정
# 예: 1.2.0 → 1.3.0 / 1.2.0.0 → 1.3.0.0
git add Directory.Build.props
git commit -m "chore(release): bump version to 1.3.0"
git push -u origin chore/release-v1.3.0
gh pr create --base master --title "chore(release): bump version to 1.3.0" --body "..."
```

**Step 2: PR 머지 후 태그 발행**
```bash
git checkout master
git pull --ff-only origin master

git tag -a v1.3.0 -m "Release v1.3.0 - <주요 변경 요약>"
git push origin v1.3.0
```

**Step 3: 워크플로 모니터링**
```bash
gh run watch $(gh run list --workflow=release.yml --limit 1 --json databaseId -q '.[0].databaseId') --exit-status
gh release view v1.3.0 --json url,assets
```

소요 시간: 약 5~6 분. 완료 시 다음 URL 들이 활성화:
- Release 페이지: `https://github.com/j2ase1862/VMS/releases/tag/v1.3.0`
- MSI 직접 다운로드: `https://github.com/j2ase1862/VMS/releases/download/v1.3.0/VMS-1.3.0.msi`

### 3.2 semver 가이드

| 변경 유형 | bump 종류 | 예 |
|---|---|---|
| 호환 안 되는 API/동작 변경 | MAJOR | 1.2.0 → 2.0.0 |
| 새 기능 추가(호환) | MINOR | 1.2.0 → 1.3.0 |
| 버그 픽스만 | PATCH | 1.2.0 → 1.2.1 |

산업용 환경 특성상 MAJOR 가 자주 발생하지는 않음. 대부분 MINOR/PATCH.

### 3.3 실수 / 롤백 시

- **태그를 잘못 발행했을 때**: GitHub Release 페이지에서 직접 삭제 후, 로컬에서 `git push --delete origin v1.x.y && git tag -d v1.x.y` 로 태그 제거. Phase B 가 캐시를 두지 않으므로 즉시 반영.
- **MSI 빌드 실패 시 자동 롤백**: `release.yml` 이 실패하면 Release 가 생성되지 않음(softprops/action-gh-release 단계에서 멈춤). 추가 정리 불필요.

---

## 4. 자동화 동작 원리

### 4.1 release.yml — Phase A

핵심 단계 (`.github/workflows/release.yml`):

```yaml
on:
  push:
    tags: ['v*']
  workflow_dispatch:

jobs:
  release:
    runs-on: windows-2025
    steps:
      - Resolve release tag        # 태그에서 버전 추출 (v 접두사 제거)
      - Setup .NET 8
      - Restore
      - Build MSI (Release)        # -p:Version=${tag_version} 오버라이드
      - Locate MSI & SHA-256       # VMS-<version>.msi 로 리네이밍 + 체크섬
      - Create GitHub Release      # softprops/action-gh-release@v2
```

> 결과 자산:
> - `VMS-<version>.msi` (538 MB 안팎 — NativeVision/DL 모델 포함)
> - `VMS-<version>.msi.sha256` (79 bytes)

### 4.2 버전 단일화 — Directory.Build.props → WiX

```xml
<!-- Directory.Build.props -->
<Version>1.2.0</Version>
<AssemblyVersion>1.2.0.0</AssemblyVersion>
<FileVersion>1.2.0.0</FileVersion>
```

```xml
<!-- VMS.MasterSetup.wixproj -->
<PropertyGroup>
  <BuildVersionFull Condition=" ... ">$(Version).0</BuildVersionFull>
  <DefineConstants>$(DefineConstants);BuildVersion=$(BuildVersionFull)</DefineConstants>
</PropertyGroup>
```

```xml
<!-- Package.wxs -->
<Package ... Version="$(var.BuildVersion)" ... />
```

CI 에서 `-p:Version=1.2.0` 으로 오버라이드하면 .NET Assembly + MSI ProductVersion 이 자동으로 같은 값. 운영자는 한 곳(`Directory.Build.props`)만 갱신.

### 4.3 GitHub Releases API 응답 → in-app 비교

VMS 가 호출하는 엔드포인트:
```
GET https://api.github.com/repos/j2ase1862/VMS/releases/latest
Accept: application/vnd.github+json
```

응답 핵심 필드:
- `tag_name` — `v1.2.0` 형식
- `html_url` — Release 페이지 URL (브라우저로 열어 사용자에게 보여줌)
- `body` — Markdown 릴리스 노트 (다이얼로그에 plain text 로 표시)
- `assets[]` — MSI / SHA-256 자산 메타데이터
- `published_at` — 릴리스 게시 UTC 시각

`GitHubUpdateService.CheckAsync()` 가 위 응답을 파싱해 `UpdateInfo` 로 변환 — 자세한 동작은 §5.

---

## 5. In-app 업데이트 알림 체커 — Phase B

### 5.1 동작 흐름

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

### 5.2 코드 / 파일 위치

| 책임 | 파일 |
|---|---|
| 인터페이스 | `VMS.Core/Interfaces/IUpdateService.cs` |
| 결과 모델 | `VMS.Core/Models/Updates/UpdateInfo.cs` |
| GitHub API 호출 + 비교 | `VMS.Core/Services/GitHubUpdateService.cs` |
| ViewModel 통합 | `VMS\ViewModels\MainViewModel.cs` (`#region Update Notifier`) |
| 사이드 패널 UI | `VMS\Views\MainWindow.xaml` (`Updates` 섹션) |
| DI + 시작 시 트리거 | `VMS\App.xaml.cs` (`GitHubUpdateService("j2ase1862", "VMS")` 생성 + `CheckForUpdatesSilentAsync()` fire-and-forget) |
| 단위 테스트 | `VMS.Core.Tests/Services/GitHubUpdateServiceTests.cs` (16 케이스) |

### 5.3 UI 노출 조건

| 요소 | 노출 조건 |
|---|---|
| "Updates" 섹션 헤더 | 항상 |
| **배지** ("새 버전 vX.Y.Z") | `MainViewModel.IsUpdateAvailable == true` (= `LatestUpdate.LatestVersion > CurrentVersion`) |
| **Check for updates** 버튼 | 항상 |

### 5.4 다이얼로그 문구

- **새 버전 감지**:
  ```
  새 버전 vX.Y.Z 이 출시되었습니다.
  현재 버전: vA.B.C
  최신 버전: vX.Y.Z

  <release notes (최대 600자, 초과 시 ... 트렁케이트)>

  다운로드 페이지를 열까요?
  ```
- **최신 상태**:
  ```
  이미 최신 버전입니다 (vA.B.C).
  ```
- **통신 실패**:
  ```
  업데이트 정보를 가져올 수 없습니다.
  네트워크 상태 또는 GitHub 접근 가능 여부를 확인해 주세요.
  ```

### 5.5 설계 결정 (왜 이렇게?)

| 결정 | 이유 |
|---|---|
| 통신/파싱 실패 시 **예외 무발산** + `null` 반환 | 시작 시 silent check 가 운영 라인을 멈추지 않도록 |
| **InformationalVersion** 우선 사용(어셈블리 버전 폴백) | `Directory.Build.props` 의 `<Version>` 이 자동 반영 |
| **pre-release / build metadata 거부** | semver 정렬 비교가 안전하지 않음. `release.yml` 도 안정 태그만 만들도록 운영 |
| **MajorUpgrade** 신뢰 — 별도 in-app 다운로드/설치 자동화 안 함 | 산업 환경에서 *명시적 사용자 동의* 가 안전. SmartScreen 노출도 사용자 인지 1회로 끝 |
| API rate limit: **익명 60 req/h/IP** 충분 | VMS 한 대당 매 시작 1회 호출 |

---

## 6. 정책 / 운영 가이드

### 6.1 SmartScreen / 코드 사이닝

- 현재 MSI 는 **미서명 상태** → 사용자가 매번 "알 수 없는 게시자" 경고를 *추가 정보 → 실행* 으로 통과해야 함.
- Authenticode 인증서를 적용하면 경고가 사라지고 사용자 신뢰도가 올라감 — 별도 가이드: [gs_msi_code_signing_guide.md](gs/guides/gs_msi_code_signing_guide.md).
- 자동 업데이트 알림은 코드 사이닝과 독립적으로 동작 — 다만 사이닝 없으면 매 업데이트마다 사용자가 SmartScreen 무시 클릭 필요.

### 6.2 MSI MajorUpgrade

- `Package.wxs` 의 `<MajorUpgrade DowngradeErrorMessage="최신 버전이 이미 설치되어 있습니다." />` 가 활성.
- 새 MSI 실행 시 기존 버전을 **자동 제거 후 재설치**. 사용자 별도 액션 불필요.
- `UpgradeCode` 는 **절대 변경 금지** (현재 `A70E988A-739F-4CDC-BE08-EBFE03DACA32`). 변경하면 in-place 업그레이드가 깨지고 두 버전이 병존 설치됨.

### 6.3 사용자 데이터 보존

업그레이드 시 `%LocalAppData%\BODA VISION AI\` 의 다음 자산은 그대로 유지:
- `system_config.json`, `layout_config.json`, `plc_signals.json`
- `BodaVision.db` (사용자 / BCrypt 해시)
- `recipes/`, `audit/`, `upload_queue/`

MSI 는 `INSTALLFOLDER`(Program Files) 만 교체. `%LocalAppData%` 는 미터치.

### 6.4 운영 시점 권장

- **업그레이드 권장 시점**: 운영 종료 후 / 야간 / 라인 정지 시간.
- **운영 중 알림이 떠도 즉시 설치 강제 X** — 사용자가 Confirm 다이얼로그를 닫으면 다음 시작까지 무동작.
- 큰 변경(MAJOR 또는 사용자 영향 큰 MINOR)은 운영자에게 사전 공지 권장 — Release notes 에 명시.

---

## 7. 트러블슈팅

### 7.1 release.yml 빌드 실패

| 증상 | 원인 / 대응 |
|---|---|
| `WiX preprocessor variable not defined: BuildVersion` | wixproj 의 `DefineConstants` 가 비어있음. `Directory.Build.props` 의 `<Version>` 누락 또는 `-p:Version=` 미전달. |
| MSI 빌드는 성공했는데 Release 가 안 보임 | `permissions: contents: write` 누락(워크플로 파일에 명시되어 있어야 함). |
| Codepage / Language 관련 에러 | `Package.wxs` 의 `Codepage="949" Language="1042"` 가 유지되어야 한국어 UI 정상. |

### 7.2 알림이 안 뜸

| 체크 항목 | 확인 방법 |
|---|---|
| 인터넷 연결 | `curl -s https://api.github.com/repos/j2ase1862/VMS/releases/latest` |
| 현재 어셈블리 버전 | VMS 실행 후 About 페이지(없으면 `dumpbin /headers VMS.exe`) 또는 `(Get-Item VMS.exe).VersionInfo.FileVersion` |
| 새 release 가 실제 더 높은가 | `gh release view --json tagName` vs 위 버전 |
| API 응답 정상 여부 | 위 curl 응답에 `tag_name`, `html_url`, `assets` 필드 존재 |

체크해도 원인 불명이면 `%LocalAppData%\BODA VISION AI\audit\` 의 최근 로그에서 `[UpdateCheck]` 키워드 검색.

### 7.3 알림은 떴는데 브라우저가 안 열림

- `ProcessService.LaunchProcess(url)` 가 `UseShellExecute = true` 로 호출 — 시스템 기본 브라우저가 등록되어 있어야 함.
- 실패 시 다이얼로그에서 수동 URL 노출(`업데이트 페이지 열기 실패` 메시지 + 클립보드 복사 가능 URL).

### 7.4 MajorUpgrade 가 동작 안 함 (두 버전 병존)

- `Package.wxs` 의 `UpgradeCode` 가 변경됐을 가능성. 위 §6.2 참고.
- WiX log: `msiexec /i VMS-1.x.y.msi /L*v upgrade.log` 후 `RemoveExistingProducts` 시퀀스 확인.

---

## 8. 부록

### 8.1 참고 PR / 커밋

| 항목 | PR |
|---|---|
| AppSetup 경로 / first-run / 레이아웃 ID | #75 |
| 버전 단일화 + release.yml | #76 |
| In-app 알림 체커 (IUpdateService 등) | #77 |
| v1.2.0 버전 bump | #78 |

### 8.2 첫 발행된 릴리스

- **v1.1.0** — Phase A 첫 발행 (2026-06-01)
- **v1.2.0** — Phase B 포함 첫 발행 (2026-06-01)

### 8.3 GitHub Releases API 응답 예시 (v1.2.0)

```json
{
  "tag_name": "v1.2.0",
  "html_url": "https://github.com/j2ase1862/VMS/releases/tag/v1.2.0",
  "body": "## BODA Vision System v1.2.0 ...",
  "published_at": "2026-06-01T07:33:12Z",
  "assets": [
    {
      "name": "VMS-1.2.0.msi",
      "browser_download_url": "https://github.com/j2ase1862/VMS/releases/download/v1.2.0/VMS-1.2.0.msi"
    },
    {
      "name": "VMS-1.2.0.msi.sha256",
      "browser_download_url": "https://github.com/j2ase1862/VMS/releases/download/v1.2.0/VMS-1.2.0.msi.sha256"
    }
  ]
}
```

### 8.4 후속 작업 후보

- 코드 사이닝 인증서 적용 (`gs/guides/gs_msi_code_signing_guide.md` 참고)
- 릴리스 노트의 Markdown 렌더링 (현재 plain text 표시)
- "이 버전 건너뛰기" 옵션 (사용자별 sentinel 파일)
- 폐쇄망 환경용 매니페스트 미러링 (현재는 GitHub 직접 접근 가정)

---

## 9. 변경 이력

| 버전 | 일자 | 주요 변경 |
|---|---|---|
| v1.0 | 2026-06-01 | 최초 작성 — Phase A/B 통합 발행 절차 정리 |
