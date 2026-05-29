# 감사 로그 SIEM 외부 전송 통합 가이드 (BODA Vision AI)

문서 버전: v1.0
대상 빌드: master @ 2026-05-29 (PR1~24)
범위: `AuditLogger` 가 생성하는 일별 JSONL 파일을 외부 SIEM 으로 실시간/근실시간 전송

> 본 문서는 **통합 모니터링 도입 시점** 에 운영자가 즉시 적용할 수 있는 SIEM 연동 절차를 정리합니다.
> 현재 빌드는 로컬 JSONL 만 생성하며, 본 가이드는 GS 인증 §5.3 "후속 강화 후보" 의 *감사 로그 SIEM 외부 전송* 항목에 대한 운영 절차서입니다.

---

## 1. SIEM 통합 근거

### 1.1 사이트 통합 모니터링
- 단일 VMS 인스턴스는 로컬 JSONL + Audit Log Viewer 로 충분 — 다수 사이트는 중앙 집계 필요
- 사고 분석 — 한 사이트의 비정상 패턴을 다른 사이트 데이터와 교차 검증
- 운영 KPI — 모든 사이트 NG 율 / 로그인 실패율 / Sequence Stop 빈도 추적

### 1.2 침해 탐지 (SOC 워크플로우)
- AuditCategory.Security · Denied 이벤트 임계 초과 → SOC 알림 (예: 5분 내 10회)
- 비정상 시각 로그인 (업무외 시간 + Admin 등급) 패턴 탐지
- 단일 작업자 다중 사이트 동시 로그인 등 위치 이상 탐지

### 1.3 규제 / 컴플라이언스
- GS / ISO/IEC 27001 — 감사 로그 중앙 보존 + 변조 방지 보관
- 의약·반도체 fab — 운영자 활동 기록 외부 보관 (책임 분리)
- 일부 고객사는 사내 SIEM 으로 위탁 운영자 활동 모니터링 요구

### 1.4 GS 인증 관점
| GS 항목 | SIEM 기여 |
|---|---|
| 보안성 (8.2) | 위협 탐지 인프라 + 사고 대응 |
| 신뢰성 (8.4) | 다중 사이트 가용성 / 가시성 |
| 추적성 | 중앙 집계로 사후 감사 가능 |

---

## 2. 아키텍처 옵션

| 패턴 | 동작 | 장점 | 단점 |
|---|---|---|---|
| **Pull (Agent 기반)** | SIEM agent (Filebeat / Splunk UF / Fluent Bit) 가 VMS 머신의 JSONL 파일을 tail | VMS 코드 변경 0, agent 가 큐잉/재시도 담당 | agent 설치 운영, 머신당 1개 |
| **Push (HTTP)** | VMS 가 직접 SIEM HTTP API 로 이벤트 전송 (Splunk HEC / Elastic Bulk) | 외부 의존 적음 (VMS 자체 발신), 실시간 | VMS 코드 변경 필요, 큐잉/재시도 자체 구현 |
| **Syslog (UDP/TCP/TLS)** | VMS 가 RFC 5424 syslog 메시지로 전송 | 표준 프로토콜, 거의 모든 SIEM 호환 | UDP 는 손실, TCP/TLS 는 연결 관리 필요 |

### 2.1 권장 선택 가이드
- **단순/표준 운영**: Pull (Filebeat) — 변경 최소화, 가장 신뢰성 높음
- **VMS 가 인터넷 직접 연결** (다중 사이트, 클라우드 SIEM): Push (Splunk HEC / Sentinel)
- **기존 syslog 인프라 보유** (방화벽, 네트워크 장비와 통합): Syslog

본 가이드는 **Pull (Filebeat)** 을 기본 권장 — 운영 부담 최소, VMS 코드 변경 0.

---

## 3. 권장 패턴 — Filebeat → Elastic / Splunk / Sentinel

### 3.1 Filebeat 설치 (Windows)
1. [Filebeat 다운로드](https://www.elastic.co/downloads/beats/filebeat) — Windows zip
2. `C:\Program Files\Filebeat\` 에 압축 해제
3. 관리자 PowerShell:
   ```powershell
   cd "C:\Program Files\Filebeat"
   .\install-service-filebeat.ps1
   ```
4. 서비스로 등록되어 부팅 시 자동 시작.

### 3.2 Filebeat 설정 — JSONL 파싱 + 메타데이터
`C:\Program Files\Filebeat\filebeat.yml`:
```yaml
filebeat.inputs:
  - type: filestream
    id: bodavms-audit
    enabled: true
    paths:
      - 'C:\Users\*\AppData\Local\BODA VISION AI\audit\*.jsonl'
    parsers:
      - ndjson:
          target: ""
          add_error_key: true
          overwrite_keys: false
    fields:
      vms_site_id: "site-A1"           # 사이트 고유 ID — 운영자가 사이트별로 변경
      vms_client_index: "1"
      product: "BODA Vision AI"
    fields_under_root: true

processors:
  - add_host_metadata: ~
  - add_fields:
      target: ""
      fields:
        log_source: "vms_audit"

# 출력 — Elastic / Splunk / Sentinel 중 하나 활성
output.elasticsearch:
  hosts: ["https://es.internal:9200"]
  username: "filebeat_writer"
  password: "${ES_PASSWORD}"
  ssl.verification_mode: full
  ssl.certificate_authorities: ["C:/codesign/internal-ca.crt"]
  index: "vms-audit-%{+yyyy.MM.dd}"
```
`ES_PASSWORD` 는 Windows 사용자 환경변수 또는 keystore (`filebeat keystore add ES_PASSWORD`) 에 저장.

### 3.3 시작 / 검증
```powershell
Start-Service filebeat
Get-Service filebeat  # Running 확인
Get-Content "C:\ProgramData\filebeat\logs\filebeat" -Tail 50  # 전송 로그
```
Elastic Kibana / Splunk Search 에서 `product:"BODA Vision AI"` 쿼리로 도착 확인.

---

## 4. SIEM별 통합 구성

### 4.1 Splunk — HEC (HTTP Event Collector) 직접 Push (Option B 예시)
Filebeat 대신 VMS 가 직접 Splunk 로 전송하는 옵션. 향후 `AuditLogger` 확장 시 참고:
```yaml
output.splunk:
  hosts: ["https://splunk.internal:8088"]
  token: "${HEC_TOKEN}"
  source: "bodavms"
  sourcetype: "_json"
  index: "vms_audit"
  ssl.verification_mode: full
```
HEC 토큰: Splunk Web → Settings → Data Inputs → HTTP Event Collector → New Token.
스코프 — index `vms_audit`, sourcetype `_json` 제한 (다른 인덱스 쓰기 불가).

### 4.2 Elastic Stack — Index Template
사전에 `vms-audit-*` 인덱스 템플릿 생성:
```json
PUT _index_template/vms-audit
{
  "index_patterns": ["vms-audit-*"],
  "template": {
    "settings": { "number_of_shards": 1, "number_of_replicas": 1 },
    "mappings": {
      "properties": {
        "timestamp":  { "type": "date" },
        "category":   { "type": "keyword" },
        "action":     { "type": "keyword" },
        "outcome":    { "type": "keyword" },
        "user":       { "type": "keyword" },
        "source":     { "type": "keyword" },
        "details":    { "type": "text" },
        "vms_site_id":      { "type": "keyword" },
        "vms_client_index": { "type": "keyword" }
      }
    }
  }
}
```
ILM 정책 — 30일 hot → 60일 warm → 305일 cold → 365일 후 삭제 (VMS 로컬 보존 365일 과 정합).

### 4.3 Microsoft Sentinel — Log Analytics Workspace
Filebeat → Logstash → HTTP Data Collector API (또는 Azure Monitor Agent 직접 수집).
권장: Azure Monitor Agent 의 Custom Log feature
- 로그 파일 패턴: `C:\Users\*\AppData\Local\BODA VISION AI\audit\*.jsonl`
- 파싱: JSON
- 테이블: `BODAVMSAudit_CL`
KQL 쿼리 예 — 1시간 내 보안 거부 이벤트 ≥ 10 회:
```kusto
BODAVMSAudit_CL
| where category_s == "Security" and outcome_s == "Denied"
| where TimeGenerated > ago(1h)
| summarize cnt = count() by vms_site_id_s
| where cnt >= 10
```

---

## 5. 보안 / 안정성 정책

### 5.1 전송 보안
| 항목 | 정책 |
|---|---|
| 전송 채널 | TLS 1.2 / 1.3 강제 — `ssl.verification_mode: full` |
| 인증 | API 토큰 (HEC) 또는 mTLS — basic auth 는 비권장 |
| 발신지 IP 화이트리스트 | SIEM 측에서 VMS 사이트 대역만 허용 |
| 토큰 회전 | 분기별 1회, 토큰 손상 의심 시 즉시 |

### 5.2 큐잉 / 백프레셔
- Filebeat: `queue.disk` 사용 — 네트워크 끊겨도 디스크 큐에 보존, 복구 시 재전송
  ```yaml
  queue.disk:
    max_size: 1GB
    flush.timeout: 10s
  ```
- VMS 자체 push 구현 시: `%LocalAppData%\BODA VISION AI\upload_queue\` 패턴 재활용 (C6 의 결과 업로드 큐와 동일 정책)
- SIEM 측 rate limit 도달 시 backoff — 지수 백오프 30s/60s/120s

### 5.3 데이터 무결성
- VMS 의 로컬 JSONL 은 SIEM 전송 후에도 **항상 유지** (PR22 보존 정책 365일)
- SIEM 도달 실패 / 손상 시 로컬 JSONL 이 진실의 원천 (single source of truth)
- 손상 의심 시 비교: 로컬 vs SIEM 의 같은 timestamp 라인 일치 확인

### 5.4 개인정보 보호
- 감사 로그에 포함된 작업자 사번 / OperatorName 은 운영 PII
- SIEM 인덱스 권한 — SOC 운영자 외 접근 차단
- 필요시 사번을 hash 로 의사익명화 (Filebeat `script` processor)

---

## 6. 운영 절차

### 6.1 헬스 체크 — 매일
- VMS 머신: Filebeat 서비스 Running 확인
- SIEM: 사이트별 마지막 도달 시각 확인 — 1시간 이상 지연 시 알람
- 로컬 JSONL 줄 수 vs SIEM 인덱스 카운트 일치 (일일 시작 시)

### 6.2 알람 / 알림 (SOC 워크플로우)
| 트리거 | 알림 채널 | 우선순위 |
|---|---|---|
| Authentication · Denied 5회/5분 (단일 사번) | Slack #soc-alerts | P3 |
| Security · DtoFieldRejected 10회/1시간 | Slack #soc-alerts | P3 |
| Configuration 변경 (업무 외 시간) | 이메일 + 사이트 책임자 | P2 |
| SIEM 미도달 ≥1시간 (사이트 단위) | PagerDuty | P2 |

### 6.3 사고 대응 절차
1. SIEM 알람 발생 → SOC 1차 분석 (5분 내)
2. 사이트별 로컬 JSONL 동일 시각 라인 확인 — 변조 가능성 배제
3. 운영자 인터뷰 (정당한 활동 여부)
4. 침해 확정 시: 계정 잠금 → 비밀번호 강제 변경 → 모든 활성 세션 종료
5. 사후 보고서 작성 → 보안 책임자 / 감사관

### 6.4 정기 점검
- 분기 1회 — 인덱스 템플릿 / ILM 정책 재검토 (필드 추가 등)
- 반기 1회 — TLS 인증서 / API 토큰 갱신
- 연 1회 — SIEM 도구 자체 보안 패치 / 라이선스 갱신

---

## 7. 알려진 함정

| 증상 | 원인 | 해결 |
|---|---|---|
| Filebeat 가 일부 줄 누락 | JSONL 마지막 줄 미완성 (append-only 특성) | `parsers.ndjson.add_error_key: true` + JSON 오류 로그 모니터링 |
| 사이트 ID 누락 | Filebeat config 의 `fields.vms_site_id` 빠뜨림 | 사이트 배포 체크리스트에 추가 |
| 대량 NG 발생 시 Filebeat lag | InspectionService 가 NG 만 기록(빈도 낮음) 이라 보통 문제 X — 그래도 1초당 100건 이상 시 batch size 조정 | `queue.mem.events: 4096`, `output.bulk_max_size: 200` |
| TLS 핸드셰이크 실패 | 사내 CA 미설치 | `ssl.certificate_authorities` 에 사내 CA 경로 명시 |
| 토큰 유효기간 만료 | 정기 회전 미수행 | 만료 7일 전 SIEM 측 알람 + Filebeat keystore 업데이트 |
| 시각 동기화 어긋남 | NTP 미설정 | Windows Time 서비스 + 사내 NTP 서버 강제 |

---

## 8. 작업 체크리스트 (SIEM 도입 시점)

- [ ] SIEM 도구 / 클라우드 결정 (Elastic / Splunk / Sentinel)
- [ ] 사이트별 고유 ID 표준 결정 (`vms_site_id` 명명 규칙)
- [ ] 인덱스 / 테이블 사전 생성 + 필드 매핑 / KQL 스키마 준비
- [ ] ILM 정책 — VMS 로컬 보존 365일 과 정합 (필수)
- [ ] 사내 CA + TLS 인증서 배포 — 모든 VMS 머신에 신뢰 등록
- [ ] API 토큰 / 인증 정보 발급 + Filebeat keystore / 환경변수 저장
- [ ] 테스트 사이트 1곳에서 dry-run — 1주일 데이터 흐름 확인
- [ ] 알람 / 대시보드 / KQL 쿼리 작성
- [ ] SOC 운영자 핸드오버 — 알람 응답 절차서 + 사고 대응 플레이북
- [ ] 운영 매뉴얼 (manual_regression_v1.x) 에 "SIEM 도달 확인" 항목 추가
- [ ] `gs_compliance_overview_v1.0.md` §5.3 "후속 강화 후보" 항목 업데이트
- [ ] 분기 검토 일정 등록 (인덱스 템플릿 / 토큰 회전)

---

## 9. (선택) VMS 자체 Push 옵션 — 향후 구현 시 참고

Pull 방식이 운영 부담 최소이나, 일부 사이트가 SIEM agent 설치 불가 (보안 정책) 일 경우 VMS 자체 push 구현 검토. 권장 설계:

### 9.1 인터페이스 추가
```csharp
public interface IAuditExporter
{
    Task ExportAsync(AuditLogEntry entry, CancellationToken ct);
}
```
- `AuditLogger.Log` 가 JSONL 파일 쓰기 후 비동기로 `IAuditExporter.ExportAsync` 호출
- 실패는 best-effort + 로컬 디스크 큐 (`upload_queue/audit/`) 에 보존

### 9.2 구현체 — SplunkHecExporter / ElasticBulkExporter
- HttpClientPolicy.Build() 로 TLS / 응답 크기 정책 통일 적용
- 배치 단위 — 30초 또는 100건 중 먼저 도달 → 1회 HTTP 전송
- 토큰: `system_config.json` 의 `auditExporter.token` 키, 비밀저장은 Windows DPAPI 권장

### 9.3 보안
- 인증 토큰을 `system_config.json` 평문 저장하면 ACL 보호 (PR2) 만으로 부족 → DPAPI 로 사용자 고유 키 암호화 후 저장
- 전송 실패 시도 횟수 / 백오프 정책: 30s → 60s → 120s → 5min → 30min 까지

### 9.4 회귀 검증
- 단위: 큐 → 배치 → HTTP 전송 → 응답 처리 흐름
- 통합: SimulatedHttpServer (Test fixture) 가 HEC endpoint 흉내 → 100건 일괄 전송 + 일부 실패 응답 시 디스크 큐 보존 확인

---

## 10. GS 인증 매핑

| GS / ISO 25051 / 27001 항목 | 본 가이드 적용 |
|---|---|
| 보안성 (25051 8.2) — 감사 / 탐지 | SIEM 알람 + SOC 사고 대응 |
| 신뢰성 (25051 8.4) — 가용성 | Filebeat 디스크 큐 + 로컬 진실의 원천 보존 |
| 추적성 (27001 A.12.4) — 로그 보호 | 외부 SIEM 보관으로 단일 사이트 변조 방어 |
| 데이터 보존 (ISO 25051) | SIEM ILM 365일 + VMS 로컬 365일 정합 |

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-05-29 | 초안 — Pull (Filebeat) 권장 + Splunk / Elastic / Sentinel 통합 + 보안 정책 + 운영 / 사고 대응 + 선택 Push 설계 + GS 매핑 |
