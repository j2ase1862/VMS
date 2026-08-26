# SW 라이선스 운영 절차 (발급·키 관리)

문서 버전: v1.0 (2026-08-25) — 설계 근거는 [docs/design/license-spec.md](design/license-spec.md).
이 문서는 **본사 발급 담당자용 운영 절차**다. 고객/현장 절차(활성화)는 사용자 매뉴얼이 담당.

## 0. 한눈에 보기

```
[고객 주문] → 지문 확보 (사전 스테이징 or 현장 전화) → licgen issue → license.lic
             → USB/이메일 전달 → AppSetup [라이선스 파일 가져오기] → 완료
                                  (발급 대장 ledger.jsonl 자동 기록)
```

- 발급 도구: `tools/licgen` (사내 전용 CLI — **MSI 비동봉**, 빌드는 `dotnet build tools/licgen`)
- **발급 GUI: `tools/LicGen.App`** (비개발 담당자용 — 빌드는 `dotnet build tools/LicGen.App`,
  실행 파일 `LicGenApp.exe`). 발급 탭·발급 대장 탭(비고 편집)·상단 백업 상태 배너로 구성 —
  CLI 와 발급/대장/백업 로직 파일을 공유하므로 어느 쪽으로 발급해도 대장은 하나다.
  비고는 대장 옆 `ledger-notes.json` 에 저장된다 (대장 자체는 append-only 원칙 유지).
- 키 보관 위치(기본): 발급 PC 의 `%USERPROFILE%\.boda-licgen\`
- 온라인 활성화 서버는 **없다** — 모든 발급은 이 절차의 오프라인 파일 발급이다 (spec §2).

## 1. 서명 키 체계

| keyId | 용도 | 개인키 파일 | 제품 등록 위치 |
|---|---|---|---|
| `prod-2026` | **고객 발급** (Production / Trial) | `prod-2026.private.pem` | `LicenseKeyring.Default` (VMS 원본 + Web 복제본) |
| `dev-2026` | 사내(Internal) 발급 전용 | `dev-2026.private.pem` | 〃 |

- 공개키는 제품에 내장되어 배포된다(안전). **개인키가 전체 체계의 급소** — 유출 시
  누구나 정품 라이선스를 발급할 수 있다.
- 용도 분리 이유: dev 키가 개발 PC 를 떠돌다 유출돼도 고객 발급 체계(prod)는 무사하다.
  **고객 발급에 dev-2026 을 쓰지 말 것.**

### 1a. 개인키 보관·백업 (필수)

1. 발급 PC 는 지정 1대로 한정한다 (현재: 개발 PC — 조직 확정 시 변경).
2. `%USERPROFILE%\.boda-licgen\` 전체(개인키 2개 + `ledger.jsonl`)를
   **오프라인 매체(USB) 2부**에 백업해 서로 다른 장소에 보관한다.
   백업 갱신 주기: 발급이 있었던 주의 말일.

   ```powershell
   licgen backup --to E:\        # USB 1부째 — 폴더 전체를 <대상>\boda-licgen-backup 로 복사
   licgen backup --to F:\        # USB 2부째
   ```

   `backup` 은 키 폴더 옆 `backup-marker.json` 에 매체별 백업 시점·대장 건수를 기록하고,
   이후 `issue` 는 **마지막 백업에 없는 발급분이 있으면 경고를 출력**한다 — 경고가 보이면
   그 주 말일까지 2부 모두 갱신하면 된다 (담당자가 바뀌어도 도구가 규칙을 상기시킨다).
   매체 구분은 **볼륨 라벨+시리얼** 기준 — USB 를 바꿔 꽂아 같은 드라이브 문자가 되어도
   서로 다른 매체로 올바르게 집계된다. LicGen.App 상단 배너와 [백업 실행] 버튼도 같은
   마커를 쓴다.
3. 개인키를 **리포/클라우드/메신저/이메일에 절대 올리지 않는다.**
   (`.boda-licgen` 은 리포 밖 사용자 프로필 경로라 실수 커밋이 구조적으로 어렵다.)
4. 유실 시: 서명 능력만 잃고 기존 발급분은 계속 유효하다. §4 rotation 으로 새 keyId 를
   만들어 이후 발급을 이어간다 (기존 고객 재발급 불필요).

## 2. 발급 절차

### 2a. 사전 스테이징 (자사에서 PC 세팅 후 출하 — 권장, 대부분)

```powershell
# 대상 PC 에서 (VMS 설치 후)
licgen fingerprint            # → 예: 0X0Y9-MMS36-7540K  (AppSetup 7페이지에서도 확인 가능)

# 발급 PC 에서
licgen issue --key $env:USERPROFILE\.boda-licgen\prod-2026.private.pem --key-id prod-2026 `
  --customer "OO정공" --fingerprint 0X0Y9-MMS36-7540K --max-clients 5 `
  --maintenance-until 2027-08-25 --out license.lic

# 검증 후 대상 PC 의 C:\ProgramData\BODA\VMS\license.lic 로 복사 (AppSetup 가져오기 또는 직접)
licgen verify --file license.lic --fingerprint 0X0Y9-MMS36-7540K
```

### 2b. 현장 발급 (고객 준비 PC — 외부망 불가 전제)

1. 현장 엔지니어: AppSetup 7페이지(Security & License)에서 지문 코드 확인 → 전화/메신저로 본사에 전달.
2. 본사: 위 `issue` 실행 → `license.lic` 를 엔지니어 휴대폰(이메일/메신저)으로 전송.
3. 엔지니어: 파일을 USB 로 옮겨 현장 PC 에서 AppSetup **[라이선스 파일 가져오기]** 로 설치.
   - 파일은 지문 바인딩이라 USB/메신저 경유 유출이 무해하다 (spec §2).

### 2c. 서버-다수 클라이언트 구성 (spec §5b)

- **Web 서버 PC 1대에만** 발급·설치한다 (`--fingerprint` = 서버 지문, `--max-clients N` = 계약 좌석).
- 클라이언트 PC 는 발급 불필요 — Web 서버에서 좌석을 자동 임대한다.
- 좌석 현황 확인: Web 에 Admin 로그인 → `GET /api/license/status` (관리 화면 UI 는 후속).

### 2d. 사내(테스터/개발/데모) 발급

```powershell
licgen issue --key $env:USERPROFILE\.boda-licgen\dev-2026.private.pem --key-id dev-2026 `
  --customer "BODA 사내" --kind Internal --fingerprint "*" --expires 2026-11-20 --out license.lic
```

- Internal 은 지문 와일드카드(`"*"`) 허용 대신 **만료 90일 상한** (도구가 강제).
- 데모/평가는 `--kind Trial` + 고객 PC 지문 + `--expires` (기간은 영업 협의).

### 발급 옵션 요약

| 옵션 | 의미 | 비고 |
|---|---|---|
| `--maintenance-until` | 유지보수 만료 — 이후 **업데이트만 차단**, 실행 유지 | 영구+유지보수 모델의 기본 축 |
| `--expires` | 실행 만료 — 유예 14일 후 차단 대상 | 구독/Trial/Internal 에만 사용 |
| `--max-clients` | 좌석 수 | 단일 PC = 1 |
| licenseId | 자동 채번 `LIC-{연도}-{순번}` | 대장 기준 — 수동 지정 불가 |

## 3. 발급 대장 (ledger.jsonl)

- `issue` 실행 시 개인키 파일 옆 `ledger.jsonl` 에 자동 append (licenseId·고객·조건·발급자·UTC 시각).
- **라이선스의 목적이 계약 관리이므로 대장이 발급의 절반이다** — 수동 편집 금지, 백업은 §1a 에 포함.
- 재발급(PC 교체·지문 변경) 시에도 새 licenseId 로 발급하고, 사유를 대장 항목 뒤에 남기려면
  같은 고객명으로 재발급 후 비고를 별도 관리한다 (구조화 비고 필드는 LicGen WPF 후속에서).

## 4. 키 rotation (유출 의심 시)

1. `licgen keygen --key-id prod-2027` (새 keyId — 연도 갱신 관례).
2. `LicenseKeyring.Default` 에 새 공개키 **추가** (VMS 원본 수정 → Web 리포 `Licensing/LicenseKeyring.cs`
   재복사 — verbatim 복제 규칙은 Web `Licensing/README.md`).
3. 구 키 항목은 **제거하지 않는다** — 제거하면 기존 발급분 전체가 무효화된다.
   (키링 회귀 테스트 `LicenseKeyringTests` 가 실수 제거를 차단)
4. 이후 발급은 새 키로만. 구 개인키 파일은 백업만 남기고 발급 PC 에서 삭제.
5. 유출 키로 위조 발급이 확인된 경우에만: 구 키 제거 + 전 고객 재발급 (최후 수단).

## 5. 강제 모드 전환 시 함께 할 일 (spec §9 2단계 — 별도 결정 후)

- VMS `App.xaml.cs` 라이선스 블록의 `IsBlocking` 분기 활성화 (주석 위치 명시됨).
- Web `LicenseService.Lease` 의 `Granted=false` 분기 + `Program.cs` Jwt:Key 패턴 승격 (주석 위치 명시됨).
- 좌석 임대 캐시 서명 토큰화 검토 (spec §5b).
- 전환 전에 **기존 현장 전수 발급 완료**가 선행 조건 — 대장으로 확인.
