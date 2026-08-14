# MSI 코드 서명 운영 가이드 (BODA Vision AI)

문서 버전: v1.0
대상 빌드: master @ 2026-05-29 (PR1~23)
범위: `VMS.MasterSetup` 가 생성하는 MSI 인스톨러 + 내부 `VMS.exe` 의 Authenticode 서명

> 본 문서는 **인증서 도입 시점** 에 운영자가 즉시 적용할 수 있는 코드 서명 절차를 정리합니다.
> 현재 빌드는 미서명 상태이며, 본 가이드는 GS 인증 §5.3 "후속 강화 후보" 의 *MSI 코드 서명* 항목에 대한 운영 절차서입니다.

---

## 1. 왜 코드 서명인가

### 1.1 사용자 신뢰
- **Windows SmartScreen** — 미서명 MSI / EXE 는 "Windows protected your PC" 차단 화면 표시
- **EV 인증서** 즉시 신뢰 / **OV 인증서** 평판(reputation) 누적 후 신뢰
- **자체 서명** — 차단은 동일하지만, 인증서 체인 검증 가능 (내부 사이트 가능)

### 1.2 무결성 / 출처 증명
- 파일 변조 감지 — 빌드 후 누군가 바이트를 1개라도 바꾸면 서명 무효
- 발급자(Subject)·발급기관(Issuer) 영구 기록 — GS 추적성 요건과 정합

### 1.3 기업/현장 배포 요구
- 일부 SI / 대기업 사내 배포 정책: 모든 외부 실행 파일은 서명 필수
- 의약·반도체 fab 등 규제 환경에서 미서명 인스톨러 거부 가능

### 1.4 GS 인증 관점
| GS 항목 | 코드 서명 기여 |
|---|---|
| 신뢰성 (8.4) | 배포물 무결성 — 운영 중 임의 변조 탐지 |
| 보안성 (8.2) | 출처 인증 — 누가 빌드했는지 부인 불가 |
| 유지보수성 (8.6) | 빌드 산출물의 출시 시점 / 빌더 영구 기록 |

---

## 2. 인증서 옵션

| 종류 | 검증 강도 | SmartScreen | 비용 (연) | 권장 시나리오 |
|---|---|---|---|---|
| **EV Code Signing** | 사업자등록증 + HSM/USB 토큰 + 전화 검증 | 즉시 신뢰 | 350~700 USD | 외부 배포 / OEM |
| **OV (Standard) Code Signing** | 사업자등록증 검증 | 평판 누적 후 신뢰 | 200~400 USD | 사내 배포 / 검증된 고객사 |
| **자체 서명 (Self-signed)** | 없음 | 차단 유지 | 0 | 내부 테스트 / CI 검증 |

### 2.1 EV 인증서 보관
- **HSM (Hardware Security Module)** 또는 **USB 크립토토큰** 에 비공개 키 저장
- PIN 입력 시에만 서명 가능 — 사고 시 키 유출 위험 최소화
- 2023 년부터 발급기관(DigiCert, Sectigo 등) 대부분 EV 키를 토큰 강제

### 2.2 OV 인증서 보관
- `.pfx` 파일 + 패스워드 — 안전한 비밀저장소 (GitHub Encrypted Secrets, Azure Key Vault, 1Password 등)
- 빌드 머신 디스크에 직접 두지 말 것

### 2.3 자체 서명 — 테스트용 발급
운영 인증서 도입 전 절차 검증용:
```powershell
# 1) 자체 서명 인증서 생성 (CurrentUser\My 저장소에)
$cert = New-SelfSignedCertificate `
    -Subject "CN=VASIM Test Code Signing" `
    -Type CodeSigningCert `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -NotAfter (Get-Date).AddYears(3)

# 2) PFX 로 export (테스트 빌드 머신에만 보관)
$pwd = ConvertTo-SecureString -String "ChangeMe!" -Force -AsPlainText
Export-PfxCertificate -Cert $cert `
    -FilePath "vasim_test_codesign.pfx" -Password $pwd

# 3) (선택) 로컬 신뢰 — TrustedPublisher 저장소 + RootCA
Import-Certificate -FilePath "vasim_test_codesign.cer" `
    -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

---

## 3. 로컬 서명 절차 (signtool)

### 3.1 도구 위치
`signtool.exe` 는 **Windows SDK** 또는 **MSBuild** 와 함께 설치:
- 경로 예: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe`
- PATH 미등록 시 절대 경로 사용

### 3.2 EXE 서명
```powershell
& signtool.exe sign `
    /fd SHA256 `
    /td SHA256 `
    /tr "http://timestamp.digicert.com" `
    /f "C:\codesign\vasim_codesign.pfx" `
    /p $env:CODESIGN_PFX_PASSWORD `
    "VMS\bin\Release\net8.0-windows7.0\VMS.exe"
```
플래그 의미:
- `/fd SHA256` — 파일 해시 알고리즘 (SHA256 권장, SHA1 deprecated)
- `/td SHA256` — 타임스탬프 해시 알고리즘
- `/tr <URL>` — RFC 3161 타임스탬프 서버 (인증서 만료 후에도 서명 유효성 유지)
- `/f /p` — PFX + 패스워드 (EV 의 경우 `/n "VASIM"` + `/sha1 <thumbprint>` 사용)

### 3.3 MSI 서명
MSI 는 빌드 **후** 동일 명령으로 서명:
```powershell
& signtool.exe sign `
    /fd SHA256 /td SHA256 `
    /tr "http://timestamp.digicert.com" `
    /f "C:\codesign\vasim_codesign.pfx" /p $env:CODESIGN_PFX_PASSWORD `
    "VMS.MasterSetup\bin\Release\VMS-<버전>.msi"   # 산출물명은 VMS-<버전>.msi (v1.4.6+)
```

### 3.4 순서 — VMS.exe → MSI
MSI 가 VMS.exe 를 포함하므로 **EXE 를 먼저 서명한 뒤 MSI 빌드 → MSI 서명** 순서로 진행.
역순으로 하면 MSI 내부 EXE 가 미서명 → 설치 후 실행 시 SmartScreen 트리거.

---

## 4. WiX 빌드 통합

### 4.1 권장 — 빌드 후 PostBuild 서명 스크립트
WiX 6 (`VMS.MasterSetup.wixproj`) 의 PostBuild target 에 서명 단계 추가:

```xml
<Target Name="SignArtifacts" AfterTargets="Build" Condition="'$(EnableCodeSigning)'=='true'">
  <Exec Command="signtool.exe sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f &quot;$(CodeSignPfxPath)&quot; /p $(CodeSignPfxPassword) &quot;$(OutputPath)VMS-$(Version).msi&quot;"
        ContinueOnError="false" />
</Target>
```
호출:
```powershell
dotnet build VMS.MasterSetup\VMS.MasterSetup.wixproj -c Release `
    /p:EnableCodeSigning=true `
    /p:CodeSignPfxPath="C:\codesign\vasim.pfx" `
    /p:CodeSignPfxPassword=$env:CODESIGN_PFX_PASSWORD
```

### 4.2 EXE 자동 서명 — VMS.csproj PostBuild
`VMS/VMS.csproj` 에 같은 패턴 추가:
```xml
<Target Name="SignVmsExe" AfterTargets="Build" Condition="'$(EnableCodeSigning)'=='true'">
  <Exec Command="signtool.exe sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f &quot;$(CodeSignPfxPath)&quot; /p $(CodeSignPfxPassword) &quot;$(OutputPath)VMS.exe&quot;" />
</Target>
```
`dotnet build VMS.sln /p:EnableCodeSigning=true` 한 번에 모든 산출물 서명.

> 솔루션 순서: VMS.exe 가 먼저 빌드/서명 → 그 후 VMS.MasterSetup 이 서명된 EXE 를 패키징 → MSI 자체 서명.

---

## 5. GitHub Actions CI 통합

### 5.1 PFX 비밀 등록
1. PFX 를 base64 로 변환 (한 줄):
   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("vasim.pfx")) | Set-Clipboard
   ```
2. Repo Settings → Secrets → Actions:
   - `CODESIGN_PFX_BASE64` — 위 base64 문자열
   - `CODESIGN_PFX_PASSWORD` — PFX 패스워드

### 5.2 워크플로우 추가 단계 (`.github/workflows/build.yml`)
빌드 단계 **전** PFX 복원, 빌드는 `EnableCodeSigning=true` 로 호출:

```yaml
- name: Restore code signing certificate
  if: github.event_name != 'pull_request'  # PR 미서명 (포크 보안)
  shell: pwsh
  run: |
    $pfxBytes = [Convert]::FromBase64String('${{ secrets.CODESIGN_PFX_BASE64 }}')
    $pfxPath = Join-Path $env:RUNNER_TEMP 'codesign.pfx'
    [IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    "CODESIGN_PFX_PATH=$pfxPath" | Out-File -FilePath $env:GITHUB_ENV -Append

- name: Build (signed)
  if: github.event_name != 'pull_request'
  run: >
    dotnet build VMS.sln --configuration ${{ env.Configuration }} --no-restore
    /p:EnableCodeSigning=true
    /p:CodeSignPfxPath="${{ env.CODESIGN_PFX_PATH }}"
    /p:CodeSignPfxPassword=${{ secrets.CODESIGN_PFX_PASSWORD }}

- name: Build (unsigned, PR)
  if: github.event_name == 'pull_request'
  run: dotnet build VMS.sln --configuration ${{ env.Configuration }} --no-restore

- name: Wipe certificate
  if: always() && env.CODESIGN_PFX_PATH != ''
  shell: pwsh
  run: Remove-Item -Path $env:CODESIGN_PFX_PATH -Force -ErrorAction SilentlyContinue
```

핵심 원칙:
- **포크 PR 은 서명 금지** — secrets 노출 위험 (Fork 의 PR runner 는 secrets 접근 불가하므로 자연스럽게 차단되지만 명시적 if 조건 추가)
- 인증서 복원 → 빌드 → 즉시 wipe — runner 디스크 잔류 최소화
- 빌드 실패해도 wipe 는 실행 (`if: always()`)

### 5.3 EV 인증서 (HSM/토큰) — Azure Key Vault 방식
EV 인증서의 비공개 키는 HSM/토큰 강제라 PFX export 불가. Azure Key Vault + AzureSignTool 사용:
```yaml
- name: Sign with Azure Key Vault
  uses: azure/cli@v2
  with:
    inlineScript: |
      AzureSignTool sign --file-digest sha256 \
        --azure-key-vault-url "${{ secrets.AKV_URL }}" \
        --azure-key-vault-client-id "${{ secrets.AKV_CLIENT_ID }}" \
        --azure-key-vault-client-secret "${{ secrets.AKV_CLIENT_SECRET }}" \
        --azure-key-vault-tenant-id "${{ secrets.AKV_TENANT_ID }}" \
        --azure-key-vault-certificate "vasim-codesign" \
        --timestamp-rfc3161 "http://timestamp.digicert.com" \
        "VMS.MasterSetup/bin/Release/*.msi"
```
> EV 사용 시 PostBuild 의 signtool 호출은 제거하고 워크플로우 별도 step 으로 분리.

---

## 6. 서명 검증

### 6.1 명령행 검증
```powershell
& signtool.exe verify /pa /v "VMS-<버전>.msi"
```
출력 예 (성공):
```
File: VMS-<버전>.msi
Index  Algorithm  Timestamp
========================================
0      sha256     RFC3161

Successfully verified: VMS-<버전>.msi
```
실패 시 일반 원인:
- 타임스탬프 미부착 → 인증서 만료 후 검증 불가
- 체인 미신뢰 (자체 서명) → `/pa` 대신 `/r <RootCert>` 명시

### 6.2 Windows 탐색기 검증
1. MSI 우클릭 → 속성 → **디지털 서명** 탭
2. 서명 목록 → 자세히 → "이 디지털 서명에 문제가 없습니다" 확인
3. **타임스탬프** 필드 존재 확인

### 6.3 PowerShell 검증
```powershell
Get-AuthenticodeSignature "VMS-<버전>.msi" | Format-List *
```
`Status` 가 `Valid`, `SignerCertificate.Subject` 가 발급자 정보 확인.

---

## 7. 운영 절차

### 7.1 인증서 만료 모니터링
- 일반 OV/EV 유효기간: 1~3 년
- 만료 30 일 전 갱신 발주 — 신규 인증서 발급 → CI 비밀 갱신 → 다음 빌드 부터 신규 인증서로 서명
- 만료 후라도 **이미 발행된 MSI** 는 타임스탬프 덕에 무한정 유효 (`/tr` 옵션 필수 이유)

### 7.2 키 백업 / 분실 대응
- PFX: 2개 이상 오프라인 매체 (USB + 금고) 분산 보관, 패스워드 별도 보관
- EV 토큰: 발급기관에 재발급 신청 — 분실 시 재발급 비용 + 1주 소요
- 분실 / 유출 의심 → **즉시 발급기관에 revocation 요청** + 모든 미만료 서명물 재서명 검토

### 7.3 감사 로그 연동
서명 액션을 빌드 산출물 메타데이터로 추적:
- CI Build Summary 에 `Signed: true / Signer: VASIM / Timestamp: ...` 출력
- 운영 서버 배포 시 서명 검증 실패하면 배포 거부 (deployment gate)

### 7.4 인증서 분리 정책
| 환경 | 인증서 | 비밀 저장 |
|---|---|---|
| 운영 배포 (master 머지 → release) | OV/EV 운영 인증서 | GitHub Secret `CODESIGN_*` |
| 사내 RC 빌드 | 자체 서명 또는 OV 동일 | 동일 |
| 개발자 로컬 | 미서명 또는 자체 서명 | 로컬 디스크 (절대 commit X) |

`.gitignore` 에 `*.pfx`, `*.snk`, `codesign*` 패턴 사전 추가 권장.

---

## 8. 알려진 함정

| 증상 | 원인 | 해결 |
|---|---|---|
| 빌드 시 "SignTool Error: An unexpected internal error has occurred" | 32-bit signtool 로 64-bit MSI 서명 시도 | x64 폴더의 signtool 사용 |
| "The timestamp signature and/or certificate could not be verified" | 타임스탬프 서버 일시 장애 | `/tr` 백업 URL 추가, 재시도 |
| SmartScreen 여전히 차단 (OV) | 평판 미누적 | 사용자 수 / 다운로드 증가에 따라 자연 해소 (수 주~수 개월) |
| MSI 서명 후 설치 거부 | UAC prompt 의 publisher 가 "Unknown" | EXE 서명 누락 — VMS.exe 먼저 서명 후 MSI 재빌드 |
| 인증서 만료 + 타임스탬프 없음 | 발급 후 `/tr` 누락 | 만료 후 검증 불가 — 신규 인증서로 재서명 + 재배포 |

---

## 9. 작업 체크리스트 (인증서 도입 시점)

- [ ] 인증서 발급기관 / 종류 결정 (EV / OV / 자체)
- [ ] PFX (OV) 또는 HSM/토큰 (EV) 수령 + 비밀 저장
- [ ] 로컬 신뢰성 검증 — 자체 서명으로 절차 dry-run
- [ ] `VMS/VMS.csproj` + `VMS.MasterSetup/VMS.MasterSetup.wixproj` 에 PostBuild Sign target 추가
- [ ] GitHub Secrets 등록 (`CODESIGN_PFX_BASE64`, `CODESIGN_PFX_PASSWORD`)
- [ ] `.github/workflows/build.yml` 에 Restore / Build(signed) / Wipe 단계 추가
- [ ] `.gitignore` 에 `*.pfx` 패턴 확인
- [ ] PR 으로 변경 머지 → master CI 에서 첫 서명 빌드 검증
- [ ] `signtool verify /pa` + 탐색기 속성 → 디지털 서명 양쪽 검증
- [ ] 운영 매뉴얼 (manual_regression_v1.x) 에 "서명 검증" 항목 추가
- [ ] `gs_compliance_overview_v1.0.md` §5.3 "후속 강화 후보" 항목 업데이트

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-05-29 | 초안 — Authenticode 기본, OV/EV 옵션, WiX 통합, GH Actions CI, 운영 절차 |
