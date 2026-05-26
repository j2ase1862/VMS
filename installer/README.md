# BODA VMS — MSI 인스톨러

WiX Toolset v5 기반 MSI 인스톨러. `dotnet publish` self-contained 출력을
자동 harvest 하여 `BODA-VMS-{Version}.msi` 생성.

## 빌드

```powershell
# 솔루션 루트에서
dotnet build installer/VMS.Installer.wixproj -c Release
```

산출물:

```
installer/bin/Release/BODA-VMS-1.1.0.msi
```

첫 빌드 시 `WixToolset.Sdk` NuGet 패키지(~100MB) 가 자동 다운로드됩니다 — 인터넷 필요.

## 버전 변경

루트의 `Directory.Build.props` 의 `<Version>` 만 갱신하면 모든 .csproj + MSI
파일명에 자동 반영됩니다.

```xml
<Version>1.2.0</Version>
```

## 설치 / 제거

- **설치**: MSI 더블클릭 → 다음/다음/설치
- **제거**: 제어판 → 프로그램 및 기능 → BODA VMS 선택 → 제거
  - 또는 시작 메뉴의 "Uninstall BODA VMS" 바로가기

## 설치 폴더 / 바로가기

- 설치 위치: `C:\Program Files\BODA VMS\`
- 시작 메뉴: `프로그램 → BODA VMS → BODA VMS`
- 데스크톱 바로가기: 자동 생성

## UpgradeCode

`9F5D7B2A-3C8F-4E1B-9D6A-1B2C3D4E5F60` — **절대 변경 금지**.
v1.1 → v1.2 자동 업그레이드가 이 코드 기준으로 동작.

## 의존성

- **빌드 측**: .NET 8 SDK, WiX Toolset SDK (자동), 인터넷
- **대상 PC**: Windows 10/11 x64
  - .NET 8 런타임: self-contained 빌드라 불필요 (포함됨)
  - Visual C++ Redistributable: 일반 Windows 에 보통 설치되어 있음.
    OpenCvSharp 네이티브 DLL 의존성으로 미설치 시 별도 설치 필요.

## 트러블슈팅

**빌드 실패 — `dotnet publish` 단계**: 솔루션의 다른 프로젝트 빌드 오류 확인.

**MSI 실행 시 "이미 설치되어 있음"**: 이전 버전 제거 또는
`msiexec /x {UpgradeCode-GUID}` 강제 제거.

**대상 PC 에서 실행 시 DLL 오류**: VC++ Redistributable 설치 필요.
Microsoft 공식 사이트에서 `vc_redist.x64.exe` 다운로드 후 설치.
