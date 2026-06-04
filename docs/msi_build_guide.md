# MSI 빌드 가이드 (BODA Vision AI)

문서 버전: v1.0
대상 빌드: master @ 2026-06-01
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

## 9. 관련 문서

| 문서 | 내용 |
|---|---|
| [gs_msi_code_signing_guide.md](gs/gs_msi_code_signing_guide.md) | Authenticode 코드 서명 운영 절차 |
| [gs_distribution_policy.md](gs/gs_distribution_policy.md) | 라이선스 / 배포 채널 / EULA |
| [manual_regression_v1.2.md](manual_regression_v1.2.md) | MSI 다운로드 후 운영 환경 회귀 가이드 |
| [gs_compliance_overview_v1.0.md](gs/gs_compliance_overview_v1.0.md) | GS 인증 보안 정책 종합 |
| `.github/workflows/build.yml` | CI 빌드 / artifact 정의 |
| `VMS.MasterSetup/Package.wxs` | MSI 구조 정의 |

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-06-01 | 초안 — 로컬 / CI 빌드 절차, 프로젝트 구조, 커스터마이징, 트러블슈팅, 검증 |
