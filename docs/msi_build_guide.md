# MSI 빌드 가이드 (BODA Vision AI)

문서 버전: v1.3
대상 빌드: master @ 2026-06-04
범위: `VMS.MasterSetup` 프로젝트로 BODA Vision System MSI 인스톨러 생성

> 코드 서명 (Authenticode) 은 별도 문서 — [gs_msi_code_signing_guide.md](gs/gs_msi_code_signing_guide.md) (PR24) 참고.

---

## 1. 산출물 개요

| 항목 | 값 |
|---|---|
| 인스톨러 형식 | Windows MSI |
| 제품명 | BODA Vision System |
| 제조사 | VASIM |
| 기본 설치 경로 | `C:\Program Files\VASIM\BODA Vision System\` |
| UpgradeCode (고정) | `A70E988A-739F-4CDC-BE08-EBFE03DACA32` |
| 단축키 | 데스크탑 "VMS 실행" + 시작 메뉴 "VASIM\BODA Vision System" |
| 빌드 도구 | WiX Toolset 6.0.2 (NuGet) |
| Codepage / Language | 949 / 1042 (Korean) |

---

## 2. 사전 요구

| 도구 | 버전 | 비고 |
|---|---|---|
| .NET 8 SDK | 8.0.x | `dotnet --version` 확인 |
| WiX Toolset | 6.0.2 | `dotnet build` 가 NuGet 으로 자동 복원, 별도 MSI 설치 불필요 |
| Visual Studio 2022 또는 VS Build Tools | 2022 | C++ AVX2 (NativeVision) 빌드용 |
| Windows | 10 / 11 또는 windows-2025 | WiX 6 는 Windows 전용 |

CI runner 는 `windows-2025` 사용 (.github/workflows/build.yml).

---

## 3. 로컬 빌드 — 권장 절차

### 3.1 단일 명령 (가장 간단)
```bash
dotnet build VMS.sln -c Release
```
솔루션 전체를 Release 로 빌드하면 의존성 순서에 따라:
1. VMS.Core / VMS.PLC / VMS.Camera / VMS.DeepLearning / VMS.VisionSetup
2. VMS (메인 런처)
3. **VMS.MasterSetup → MSI 생성**

### 3.2 MSI 프로젝트만 빌드
```bash
dotnet build VMS.MasterSetup/VMS.MasterSetup.wixproj -c Release
```
ProjectReference 가 VMS 본체 빌드를 트리거하므로 사실상 (3.1) 과 동일한 결과.

### 3.3 빌드 산출물
```
VMS.MasterSetup/bin/Release/VMS.MasterSetup.msi
```

용량 확인:
```powershell
Get-Item "VMS.MasterSetup\bin\Release\*.msi" | Select Name, @{N='SizeMB'; E={[math]::Round($_.Length/1MB, 2)}}
```

### 3.4 빌드 후 정리 (필요 시)
```bash
dotnet clean VMS.sln -c Release
```

---

## 4. CI 빌드 — GitHub Actions

### 4.1 자동 실행 조건
`.github/workflows/build.yml` 이 매 push / PR 마다 실행:
- master / main / `feat/**` / `fix/**` 브랜치 push
- master / main 대상 PR
- `workflow_dispatch` 수동 트리거

### 4.2 artifact 다운로드
1. GitHub repo → Actions 탭
2. 해당 워크플로우 run 선택
3. 페이지 하단 **Artifacts** 섹션
4. `BODA-VMS-installer-{git-sha}` 다운로드 (30일 보관 — PR16)

artifact 파일명에 git SHA 가 포함되어 어느 커밋 빌드인지 추적 가능.

### 4.3 Build Summary
CI run 의 Summary 탭에 다음이 출력됩니다:
- Configuration: Release
- Runner: windows-2025
- .NET 버전
- MSI 파일명 + 크기 (MB)
- artifact 이름 + 보관 기간

---

## 5. 프로젝트 구조

### 5.1 `VMS.MasterSetup/VMS.MasterSetup.wixproj`
```xml
<Project Sdk="WixToolset.Sdk/6.0.2">
  <ItemGroup>
    <ProjectReference Include="..\VMS\VMS.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="WixToolset.UI.wixext" Version="6.0.2" />
  </ItemGroup>
</Project>
```
간결한 SDK-style 프로젝트 — WixToolset.Sdk 가 컴파일 / 링크를 자동.

### 5.2 `VMS.MasterSetup/Package.wxs`
WiX 정의 — 4 Fragment 구성:
| Fragment | 역할 |
|---|---|
| Package (root) | 제품명 / Manufacturer / UpgradeCode / MajorUpgrade / Feature / UI |
| Directory Structure | `ProgramFiles6432Folder\VASIM\BODA Vision System` |
| AppFiles ComponentGroup | `<Files Include="$(var.VMS.TargetDir)**" />` — VMS 빌드 출력 자동 수집 |
| DesktopShortcutComp | 데스크탑 "VMS 실행" 단축키 + HKCU 키 (KeyPath) |
| StartMenuShortcutComp | 시작 메뉴 "VASIM\BODA Vision System" 단축키 + RemoveFolder on uninstall |

### 5.3 파일 자동 수집
```xml
<Files Include="$(var.VMS.TargetDir)**" />
```
- `$(var.VMS.TargetDir)` = VMS 프로젝트의 `bin/Release/net8.0-windows7.0/` 경로
- ProjectReference 로 자동 정의됨
- 모든 .exe / .dll / 설정 / 네이티브 DLL (OpenCV / OnnxRuntime / NativeVision 등) 일괄 포함

> **주의**: VMS 본체 빌드 후 이 단계가 실행되므로 VMS.csproj 의 의존성 (VMS.VisionSetup / VMS.AppSetup / VMS.PLC / VMS.Camera / VMS.Core / VMS.DeepLearning) 이 모두 포함됨.

---

## 6. 커스터마이징

### 6.1 버전 변경
`VMS.MasterSetup/Package.wxs` line 6:
```xml
Version="1.0.0.0"
```
master 머지 직전 hotfix 시 수동 변경. (현재 자동화 안 됨 — 후속 작업 후보)

### 6.2 제조사 / 제품명
`Package.wxs` line 4-5:
```xml
<Package Name="BODA Vision System" Manufacturer="VASIM" ... />
```
OEM 배포 시 동시 변경 + NOTICE / LICENSE 표기 정책 확인 필요 (gs/gs_distribution_policy.md §4.2).

### 6.3 설치 경로
`Package.wxs` line 27:
```xml
<Directory Id="INSTALLFOLDER" Name="VASIM\BODA Vision System" />
```
설치 시 UI 의 InstallDir 다이얼로그로 사용자가 다른 경로 선택 가능 (WixUI_InstallDir).

### 6.4 단축키 추가 / 제거
새 단축키 추가는 별도 ComponentGroup 정의 후 Feature 에 ComponentRef 추가. 제거는 RegistryValue + Component 제거.

### 6.5 추가 파일 포함
일반 케이스는 VMS 프로젝트의 빌드 출력에 자동 포함. 빌드 출력 밖 파일 (예: 사전 학습 ONNX 가중치) 포함 시:
```xml
<ComponentGroup Id="ExtraFiles" Directory="INSTALLFOLDER">
  <Files Include="external_dir\**\*.onnx" />
</ComponentGroup>
```
+ Feature 에 `<ComponentGroupRef Id="ExtraFiles" />` 추가.

---

## 7. 트러블슈팅

| 증상 | 원인 | 해결 |
|---|---|---|
| `error WIX0103: Unable to load .wixproj` | WiX SDK 미설치 | `dotnet restore` 재실행 (NuGet 으로 자동 복원) |
| MSI 가 0KB / 빌드는 성공 | VMS 본체 빌드 실패 후 wixproj 가 빈 TargetDir 참조 | 솔루션 전체 빌드부터 다시 시도, 본체 오류 먼저 해결 |
| `error WIX5051: Files matched 0 files` | `$(var.VMS.TargetDir)` 가 비어있음 | VMS.csproj 가 net8.0-windows7.0 TargetFramework 로 출력했는지 확인 |
| `error 1316: A network error occurred while attempting to read from C:\\Users\\...` 설치 시점 | 동일 UpgradeCode 의 다른 버전 캐시 충돌 | `msiexec /x {UpgradeCode}` 로 기존 제품 제거 후 재시도 |
| 한글 설치 화면 글자 깨짐 | Codepage / Language 불일치 | Package.wxs 의 `Codepage="949" Language="1042"` 유지 확인 |
| SmartScreen 차단 (외부 배포) | 미서명 MSI | [gs_msi_code_signing_guide.md](gs/gs_msi_code_signing_guide.md) 절차 적용 |
| 단축키 생성 안 됨 | RegistryValue KeyPath 누락 | KeyPath="yes" 확인 |
| 업그레이드 시 이전 설치본 제거 안 됨 | UpgradeCode 가 변경되었거나 Version 미증가 | UpgradeCode 는 영구 고정, Version 만 증가 |

---

## 8. 검증

### 8.1 MSI 메타데이터 확인 (PowerShell)
```powershell
$wi = New-Object -ComObject WindowsInstaller.Installer
$db = $wi.GetType().InvokeMember("OpenDatabase", "InvokeMethod", $null, $wi,
    @("VMS.MasterSetup\bin\Release\VMS.MasterSetup.msi", 0))
$view = $db.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $db,
    @("SELECT * FROM Property WHERE Property='ProductVersion' OR Property='Manufacturer' OR Property='ProductName' OR Property='UpgradeCode'"))
$view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null)
while ($true) {
    $rec = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
    if ($rec -eq $null) { break }
    "{0} = {1}" -f
        $rec.GetType().InvokeMember("StringData", "GetProperty", $null, $rec, @(1)),
        $rec.GetType().InvokeMember("StringData", "GetProperty", $null, $rec, @(2))
}
```

### 8.2 설치 dry-run (실제 설치 X)
```powershell
msiexec /a "VMS.MasterSetup\bin\Release\VMS.MasterSetup.msi" TARGETDIR="C:\Temp\msi-extract" /qb
```
관리 설치 (administrative install) — MSI 내용을 추출만 하고 실제 시스템 변경 없음.

### 8.3 정식 설치 / 제거
```powershell
# 설치 (조용한 모드)
msiexec /i "VMS.MasterSetup.msi" /qb

# UI 표시 설치
msiexec /i "VMS.MasterSetup.msi"

# 제거 (UpgradeCode 기준)
$code = "{A70E988A-739F-4CDC-BE08-EBFE03DACA32}"
msiexec /x $code /qb
```

---

## 9. 운영 설치 후 보안 모드 설정 (필수)

**RELEASE 빌드 MSI 는 보안 모드가 명시되지 않으면 부팅 중단**합니다 (PR #123 의
`SecurityLoadPolicy.RequireExplicit` 정책, PR P5-A 에서 활성화). 운영 PC 설치 직후
다음 중 **반드시 한 가지** 설정 — 누락 시 VMS 시작 시 "보안 정책 오류" 메시지박스 표시 후 종료.

### 9.1 권장 — 시스템 환경변수 (관리자 PowerShell)
```powershell
# 운영 (Production): HTTPS 강제 + 엄격한 인증서 검증
[Environment]::SetEnvironmentVariable("BODA_VMS_SECURITY_MODE", "Production", "Machine")

# 또는 setx 명령 (재부팅 또는 새 세션부터 반영)
setx BODA_VMS_SECURITY_MODE Production /M
```

장점:
- system_config.json 손상 / 누락 시에도 보안 모드 유지 (자동 다운그레이드 차단)
- AppSetup wizard 재실행해도 보안 모드는 환경변수가 우선

### 9.2 대안 — system_config.json
AppSetup wizard 실행 시 자동 생성. 수동 편집 시:
```json
{
  "securityMode": "Production",
  ...
}
```
경로: `%LocalAppData%\BODA VISION AI\system_config.json`

### 9.3 QA / 디버깅 임시 우회
운영 빌드를 QA 환경에서 일시적으로 Development 모드로 띄우려면:
```powershell
$env:BODA_VMS_RELAX_SECURITY = "1"   # 현재 세션만
# 또는 setx BODA_VMS_RELAX_SECURITY 1
```
설정 시 `SecurityLoadPolicy.WarnOnFallback` 으로 강제 — system_config.json 누락도 허용.
**운영 PC 에는 절대 설정 금지** (인증 다운그레이드 사유).

### 9.4 결정 검증
설치 + 환경변수 설정 후 VMS 시작:
- 정상 시작 → 보안 모드 결정됨
- 시작 시 "보안 정책 오류" 메시지박스 표시 → 9.1 또는 9.2 설정 누락. 환경변수는 새 PowerShell 세션 또는 재부팅 후 반영됨.
- 시작 후 헬스 체크 UI 의 SecurityOptions 항목 메시지 확인:
  - `Mode=Production, Source=Environment` 또는 `Source=ConfigFile` → 정상
  - `Source=FallbackOnError` → Development 폴백 발생 (Warn) → 9.1 / 9.2 명시 권장

---

## 10. Web SSO 통합 운영 (선택 — 단일 사용자 계정)

VMS Admin/Manager 인증을 BODA.VMS.Web 으로 위임해 **단일 사용자 계정** 으로 운영. 비밀번호 동기화 부담 / 감사 추적 단절 해소. **설계 문서**: [`docs/gs/SSO_Migration_Plan.md`](gs/SSO_Migration_Plan.md).

### 10.1 활성 조건
- BODA.VMS.Web 이 운영 가동 중 (단일 PC 운영 시 Windows Service 자동 시작 권장)
- VMS 와 Web 간 통신 가능 (`http(s)://<host>:5292` — `system_config.json:webServerUrl`)
- Web 측 admin 계정에 운영자 등록 완료

### 10.2 활성 절차
1. **VMS.AppSetup wizard** Page 2 → "Web SSO (Single Sign-On)" 카드 → "Web SSO 활성" 체크
2. wizard 저장 → `system_config.json` 에 `"webSso": { "enabled": true, "webServerUrl": "..." }` 기록
3. VMS 재시작 → 다음부터 모든 username/password 로그인이 Web 으로 위임

### 10.3 비상 local-admin 운영
**Web 도달 불가 시 유일한 진입점**. SSO 활성 후에도 별도 시드 계정 `local-admin` 이 항상 존재.

| 항목 | 값 / 정책 |
|------|----------|
| 디폴트 비밀번호 | `fallback-change-me-9999` |
| 허용 권한 | `StartStop`, `ViewStatistics`, `RestartWebService` (키오스크 운영 + 진단 + Web 재시작) |
| 거부 권한 | `ManageUsers`, `EditRecipe`, `SystemConfiguration` 등 운영 데이터 변경 전체 |
| 사용 로그 | `AuditCategory.Authentication / Success` 에 `IsLocalFallback=true (restricted permissions apply)` 명시 |
| 거부 로그 | `AuditCategory.Authorization / Denied` + "Web SSO 로그인 후 수행하세요" 안내 |

### 10.4 운영 첫 가동 시 — local-admin 비밀번호 변경 (필수)
1. SSO 활성 + 운영 PC 재시작
2. **VMS 로그인 화면 → username `local-admin` + 디폴트 비밀번호** 로 1회 로그인
3. VMS 사용자 관리 UI → "비밀번호 변경" 으로 강한 비밀번호로 교체
4. 이후 일반 사용자는 Web admin 계정으로 로그인, local-admin 은 Web 도달 불가 시 응급 진입용

### 10.5 운영 검증
- AppSetup 저장 후 `system_config.json` 확인: `"webSso": { "enabled": true, "webServerUrl": "..." }`
- VMS 시작 → 로그인 화면에서 SSO 모드 안내 (PR4 의 `IsWebSsoEnabled` 표시 — UI 향후 보강)
- Web admin 자격으로 로그인 → 성공 → 감사 로그에 "Web SSO ok, Role=..., Grade=..." 기록
- Web Service 일시 중지 → 일반 admin 로그인 시 "local-admin 만 로그인 가능" 안내
- local-admin 로그인 → 운영 데이터 변경 시도 → 거부 + AuditLog 기록

### 10.6 비활성화 / 롤백
- AppSetup wizard 재실행 → "Web SSO 활성" 체크 해제 → VMS 재시작
- 기존 로컬 사용자 계정 그대로 사용 가능 (PR3 가 admin 시드 보존)
- 마이그레이션 PR 시퀀스 (#128 / #129 / #130 / #131 / 본 #132) 의 어떤 단계 후에도 SSO=false 로 즉시 복원 가능

---

## 11. 초기 admin 비밀번호 설정 (필수 — Option C, 2026-06-04)

**디폴트 admin 비밀번호 자동 시드가 제거**되었습니다 (VMS/Web 둘 다). 약한 디폴트 (admin/admin / admin/admin123) 가 GS 보안성 위반 + 양쪽 시스템 비밀번호 불일치 운영 부담 해소.

| 시스템 | 시드 동작 |
|--------|----------|
| VMS UserService | InitializeDatabase 가 local-admin 만 자동 시드. 정규 admin 은 AppSetup wizard 의 PasswordBox 또는 운영자 명시 호출 필요 |
| BODA.VMS.Web | Initial:AdminPassword 환경변수 미설정 + DB 에 admin 없으면 부팅 차단 (InvalidOperationException) |

### 11.1 신규 install 절차

#### VMS 데스크탑
**AppSetup wizard Page 2 → "Initial Admin Passwords" 카드** 에서 2 PasswordBox 입력:
- **VMS Admin 비밀번호**: 정규 admin 계정 — VMS 단독 운영 / SSO 비활성 환경에서 사용
- **Local Fallback Admin 비밀번호**: 비상 폴백 — Web 도달 불가 시에만 사용 (제한 권한)

**규칙**: 최소 8 자, **12 자 이상 권장**. 양쪽 비밀번호는 **다르게 설정** (한쪽 침해가 다른쪽까지 미치지 않도록).

저장 직후 BCrypt 해시로 DB 기록 + 평문 메모리에서 즉시 폐기.

#### BODA.VMS.Web 서버
**Windows Service 설치 직후 (또는 첫 가동 전)** 환경변수 설정:

```powershell
# 운영 (관리자 PowerShell)
setx Initial__AdminPassword "<12자+ 강한 비밀번호>" /M

# 선택 — username 변경시
setx Initial__AdminUsername "<username>" /M

# 또는 개발/QA
dotnet user-secrets set Initial:AdminPassword <비밀번호> --project BODA.VMS.Web/BODA.VMS.Web
```

미설정 시 Windows Service 가 InvalidOperationException 으로 부팅 차단 → 이벤트 뷰어 / Serilog 로그에 다음 메시지:
```
초기 admin 계정이 DB 에 없고 Initial:AdminPassword 가 미설정.
다음 중 하나로 명시:
  - 운영: setx Initial__AdminPassword "<강한 비밀번호>" /M
  ...
```

### 11.2 기존 install 호환 (마이그레이션)

이미 admin 계정이 DB 에 존재하는 환경은 본 변경의 영향 zero — 기존 비밀번호 그대로 사용. 운영자가 권장 시점에 비밀번호 변경:

| 시스템 | 변경 방법 |
|--------|----------|
| VMS admin | VMS 로그인 → 사용자 관리 UI → "비밀번호 변경" |
| VMS local-admin | 동일 (사용자 관리 UI) 또는 AppSetup wizard 재실행 후 LocalAdminPasswordBox 입력 |
| Web admin | Web UI → 사용자 관리 → "비밀번호 변경" |

### 11.3 비밀번호 정책 권장

- 최소 12 자 (운영 환경)
- 대/소문자 + 숫자 + 특수문자 3 종 이상
- 사전 단어 / 디폴트 패턴 (admin, password, 1234, qwerty 등) 금지
- 90 일마다 변경 권장 (GS 보안성 권고)
- 양쪽 시스템 별도 — **동기 금지** (역할/사용 빈도 다름)

### 11.4 검증

- VMS 시작 → 입력한 admin 비밀번호로 로그인 → 정상
- 옛 admin123 시도 → 실패 (기존 install 아닌 경우)
- Web 시작 → /health 200 → admin 으로 로그인 → 정상
- 환경변수 미설정 상태로 Web 재시작 → 부팅 차단 → 이벤트 로그 확인

---

## 12. 관련 문서

| 문서 | 내용 |
|---|---|
| [gs_msi_code_signing_guide.md](gs/gs_msi_code_signing_guide.md) | Authenticode 코드 서명 운영 절차 |
| [gs_distribution_policy.md](gs/gs_distribution_policy.md) | 라이선스 / 배포 채널 / EULA |
| [manual_regression_v1.2.md](manual_regression_v1.2.md) | MSI 다운로드 후 운영 환경 회귀 가이드 |
| [gs_compliance_overview_v1.0.md](gs/gs_compliance_overview_v1.0.md) | GS 인증 보안 정책 종합 |
| [SSO_Migration_Plan.md](gs/SSO_Migration_Plan.md) | VMS ↔ Web SSO 통합 마이그레이션 설계 (PR1~5) |
| `.github/workflows/build.yml` | CI 빌드 / artifact 정의 |
| `VMS.MasterSetup/Package.wxs` | MSI 구조 정의 |

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-06-01 | 초안 — 로컬 / CI 빌드 절차, 프로젝트 구조, 커스터마이징, 트러블슈팅, 검증 |
| v1.1 | 2026-06-04 | §9 운영 설치 후 보안 모드 설정 절차 추가 (PR P5-A 의 RequireExplicit 활성화 대응) |
| v1.2 | 2026-06-04 | §10 Web SSO 통합 운영 절차 추가 (SSO PR1~5: 단일 사용자 계정 + local-admin 폴백) |
| v1.3 | 2026-06-04 | §11 초기 admin 비밀번호 설정 절차 추가 (Option C: VMS/Web 양쪽 디폴트 시드 제거, 운영자 명시 입력 필수) |
