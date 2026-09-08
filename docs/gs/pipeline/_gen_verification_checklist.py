# -*- coding: utf-8 -*-
# BODA VMS·VMS.Web 자체 검증 테스트(GS 시험 리허설) 체크리스트 — 세부일정 보고서와 동일 스타일
import sys
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.formatting.rule import CellIsRule
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.utils import get_column_letter

OUT = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\vinos\Downloads\BODA_VMS_자체검증_체크리스트.xlsx"

NAVY = "1F3864"; BLUE = "2E5496"
FILL_LABEL = "D9E1F2"; FILL_NOTE = "FFF2CC"
GRP = ["E2E3E5", "D8EFEA", "DEEAF6", "FCEFD6", "E1D5E7", "E7E6F7", "F8DEDE", "FCE4D6", "E2EFDA", "F2ECF7", "DDEBF7", "DCE9E4", "EAF1DD", "FDE9D9"]
PASS_G = "C6EFCE"; FAIL_R = "FFC7CE"; NA_GY = "EDEDED"

thin = Side(style="thin", color="BFBFBF")
BORDER = Border(left=thin, right=thin, top=thin, bottom=thin)

def F(color=None, bold=False, size=11, white=False):
    return Font(name="맑은 고딕", size=size, bold=bold,
                color="FFFFFF" if white else (color or "1A1A1A"))

def fill(hexv): return PatternFill("solid", fgColor=hexv)

WRAP = Alignment(vertical="center", wrap_text=True)
CTR = Alignment(horizontal="center", vertical="center", wrap_text=True)

def put(ws, coord, value, font=None, fl=None, align=None, border=True, numfmt=None):
    c = ws[coord]
    c.value = value
    c.font = font or F()
    if fl: c.fill = fill(fl)
    c.alignment = align or WRAP
    if border: c.border = BORDER
    if numfmt: c.number_format = numfmt
    return c

wb = openpyxl.Workbook()
wb.calculation.fullCalcOnLoad = True

# ============================================================ 체크리스트 데이터
# (영역 제목, 색, [(품질특성, 점검 항목, 확인 방법, 기대 결과, 근거§)])
SECTIONS = [
    ("A. 설치 · 제거", GRP[1], [
        ("이식성", "VMS 설치", "설치 파일(MSI)을 새 PC 에서 더블 클릭해 기본 경로로 설치",
         "오류 없이 설치, 시작 메뉴·바탕화면 바로가기 생성", "§2.1"),
        ("이식성", "VMS 제거", "제어판 → 프로그램 제거로 삭제 후 잔여 확인",
         "폴더·바로가기 정리, 로컬 설정(%LocalAppData%)은 보존", "§2.1"),
        ("이식성", "VMS 업그레이드", "구버전 위에 새 MSI 실행",
         "자동 제거 후 설치, 설정·카메라 보정 데이터 보존", "§2.1"),
        ("이식성", "Web 서버 설치", "MSI 동봉 Web 서버를 AppSetup 구성 카드에서 활성화 (구방식 Install-Web.ps1 도 유효)",
         "Windows 서비스 등록(demand), 기동·방화벽 정상 — MSI ~1,130MB 가 동봉 정상 크기", "§2.2"),
        ("이식성", "Web 서버 제거/재설치", "Uninstall-Web.ps1 후 재설치",
         "DB(검사 이력) 보존 확인", "§2.2"),
        ("기능적합성", "첫 실행 체크리스트", "매뉴얼 §2.4 순서대로 Web 기동→회원가입→작업자 등록→WO 등록→VMS 연결",
         "각 단계 매뉴얼 문구와 화면 일치, 헤더에 연결 상태 표시", "§2.4"),
    ]),
    ("B. 최초 설정 (AppSetup 마법사)", GRP[2], [
        ("기능적합성", "마법사 6단계 진행", "첫 실행 시 마법사가 뜨는지, 6단계를 차례로 입력·완료",
         "각 페이지 항목이 매뉴얼 §2.5 표와 일치, [Finish] 로 저장", "§2.5"),
        ("사용성", "ⓘ 도움말 표시", "각 입력란 라벨의 ⓘ 에 마우스 올림 (2~6단계 임의 10개)",
         "말풍선 설명 표시, 내용이 매뉴얼 표 설명과 동일 기조", "§2.5"),
        ("기능적합성", "가상 카메라 구성", "3단계에서 Virtual Mode 로 카메라 수동 등록",
         "카메라 없이 설정 완료, VMS 정상 기동", "§2.5"),
        ("신뢰성", "초기 비밀번호 규칙", "2단계에서 8자 미만 비밀번호 입력 시도",
         "저장 거부 또는 경고 — 약한 비밀번호로 진행 불가", "§2.5"),
        ("기능적합성", "설정 저장·재실행", "마법사 완료 후 VMS 재시작",
         "마법사 재출현 없음, 저장된 설정으로 기동", "§2.5"),
    ]),
    ("C. VMS 운영 기능", GRP[0], [
        ("기능적합성", "작업자 로그인", "[Login...] 에서 등록 사번+PIN 입력 / 오입력 각 1회",
         "성공 시 이름·Role 뱃지 표시, 실패 시 명확한 오류 문구", "§3.2.1"),
        ("기능적합성", "작업지시 선택", "[Work Orders] → 상태 필터 전환 → 더블클릭 선택",
         "목록 항목이 Web 과 일치, 선택 시 레시피 자동 로드", "§3.2.2"),
        ("기능적합성", "AUTO RUN 활성 조건", "카메라/WO 조건을 하나씩 빼며 버튼 상태 확인 + 미로그인 상태에서 시작 시도",
         "조건 미충족 시 비활성 + 말풍선 사유. 시스템 로그인 없이도 시작 가능(#314), 로그인 시 등급 권한 적용", "§3.5"),
        ("기능적합성", "검사 실행·진행률", "[AUTO RUN] 시작 → 수 건 검사 → [STOP]",
         "WO 칩 진행수량 실시간 증가, Web 대시보드에 반영", "§3.2.3"),
        ("기능적합성", "작업지시 완료 처리", "계획 수량 도달까지 검사 진행",
         "완료 알림창+알림음, 검사 자동 정지, 다음 WO 선택 유도", "§3.4"),
        ("기능적합성", "사이드 패널 섹션", "⚙ 열기 → Camera Control/Recipe/Web Parameters 등 조작",
         "각 버튼 동작이 매뉴얼 §3.3 설명과 일치, 섹션 ⓘ 도움말 표시", "§3.3"),
        ("기능적합성", "Recent Inspections", "검사 후 목록 확인, [Clear], VMS 재시작",
         "최대 200건 보관, Clear 로 비움, 재시작 시 초기화(문서 명시와 일치)", "§3.3"),
        ("사용성", "Ctx(추적 4필드)", "WO 선택·로그인·S/N 입력 후 필드 자동 채움과 [✕] 확인",
         "WO/Lot/OpID 자동, S/N 수동, ✕ 로 전체 비움", "§3.2.5"),
    ]),
    ("D. VMS 관리자 기능", GRP[5], [
        ("보안성", "사용자 관리", "Admin 로그인 → 계정 추가/등급 변경/비밀번호 재설정/삭제",
         "각 작업 정상 + 감사 로그에 기록", "§3.9"),
        ("보안성", "감사 로그 뷰어", "기간·카테고리·결과 필터 조회, CSV 내보내기",
         "필터 정확, 거부/실패 이벤트 색상 구분, CSV 저장", "§3.9"),
        ("신뢰성", "백업/복원", "[Run Backup] → 설정 변경 → [Run Restore] 로 원복",
         "백업 파일 생성, 복원 후 값 원복, 감사 이벤트 기록", "§3.9"),
        ("신뢰성", "자동 백업·보존 정책", "주기/보존일 설정 저장, Retention Preview(dry-run) 실행",
         "설정 저장·재로드 일치, Preview 결과 표시(실삭제 없음)", "§3.9"),
        ("신뢰성", "Health Check", "[Refresh] 실행",
         "항목별 Pass/Warn/Fail 표시, 종합 배지 일치", "§3.9"),
        ("보안성", "지원 패키지", "Support Package 생성 후 ZIP 내용 확인",
         "사용자 DB·레시피 미포함(문서 명시), 감사 로그 포함", "§3.9"),
    ]),
    ("E. VisionSetup (비전 설정)", GRP[3], [
        ("기능적합성", "실행 진입", "VMS 사이드 패널 [Vision Tool Setup] (Supervisor 로그인)",
         "권한 없으면 섹션 숨김, 권한 있으면 정상 실행", "§3.3"),
        ("기능적합성", "레시피 생성·저장", "Recipe Manager 에서 새 레시피 → 스텝/툴 추가 → 저장·로드",
         "저장 후 재로드 시 구성 유지", "§3.3"),
        ("기능적합성", "도구 실행 파이프라인", "이미지 열기 → 팔레트에서 툴 2~3개 드래그 → Run All(F5)",
         "결과 이미지·Pass/Fail 표시, 개별 실행(F6) 동작", "§3.3"),
        ("기능적합성", "ROI 도구", "Rectangle/Circle/Polygon ROI 그리기·이동·삭제",
         "ROI 내에서만 툴 적용(Use ROI 체크 시)", "§3.3"),
        ("기능적합성", "Batch Test", "이미지 폴더 지정 후 일괄 검사 실행",
         "PROCESSED/PASS/FAIL 집계, CSV 리포트 생성", "§3.3"),
        ("사용성", "패널 ⓘ·툴 도움말", "Camera/Steps/Tool Palette/Tool Settings ⓘ 및 툴별 HelpIcon",
         "설명 말풍선 표시", "§3.3"),
    ]),
    # F. DeepLearning(라벨링·학습) 그룹은 GS 인증 범위 외로 제외 (2026-09-08, gs_scope_ai_tools.md).
    #    추론 도구(Detection 등)는 E 그룹(VisionSetup)에서 샘플 ONNX 로 검증한다.
    ("F. 딥러닝 추론 도구 (샘플 모델)", GRP[4], [
        ("기능적합성", "Detection 도구", "샘플 D-FINE ONNX 지정 → Class Names 자동 채움 → Run",
         "기대 박스(expected.csv)와 클래스·위치 일치, 인증 빌드에는 학습 도구 미포함", "§5"),
        ("신뢰성", "손상 모델 격리", "잘못된 .onnx 지정 후 Run",
         "OnnxLoadException 으로 도구만 실패, 시퀀스 엔진 계속 동작", "§5"),
    ]),
    ("G. VMS.Web 화면", GRP[6], [
        ("보안성", "회원가입·승인", "신규 가입 → 관리자 승인 전 로그인 시도 → 승인 후 재시도",
         "승인 전 차단, 승인 후 로그인 가능", "§4.1"),
        ("기능적합성", "Dashboard/Andon", "검사 진행 중 두 화면 표시",
         "새로고침 없이 실시간 갱신, 라인 카드 정보 정확", "§4.3~4.4"),
        ("기능적합성", "Alarms 처리", "NG 발생 → 알람 확인(Ack) → 사유 입력 해제(Resolve)",
         "알람 즉시 표시, 처리 이력 보존(삭제 아님)", "§4.5"),
        ("기능적합성", "Production History", "필터 조회 + 행 클릭 상세 + Excel 내보내기",
         "측정값·사진·NG 코드 표시, 읽기 전용", "§4.6"),
        ("기능적합성", "Work Orders 라이프사이클", "생성 → Start → 검사 → Complete → Close",
         "상태 전이가 §6.2 표와 일치, 자동 전이 동작", "§4.7·§6.2"),
        ("기능적합성", "기준 정보 CRUD", "Products/Operators/Defect Codes/Shifts 각 1건 생성·수정",
         "저장·목록 반영, Audit Logs 에 기록", "§4.8~4.15"),
        ("기능적합성", "Reports/통계", "OEE·파레토·SPC 화면 표시",
         "데이터 기반 차트 렌더", "§4.16"),
        ("사용성", "알림 종", "미확인 알람 상태에서 종 아이콘 → 개별 ✕ / [모두 확인]",
         "배지 수 갱신, 이력은 영구 보존", "§4.17"),
    ]),
    ("H. 연동 · 신뢰성", GRP[7], [
        ("신뢰성", "오프라인 업로드 큐", "Web 서비스 중지 → 검사 수 건 → 서비스 재시작",
         "결과 유실 없음 — 큐 보관 후 자동 재전송, 순서 유지", "§5.3"),
        ("신뢰성", "VMS 재시작 복구", "큐에 파일 있는 상태로 VMS 재시작",
         "보관 결과가 재기동 후 자동 전송", "§5.3"),
        ("기능적합성", "실시간 반영", "검사/알람 발생 시 다른 브라우저·다른 VMS 화면 확인",
         "새로고침 없이 즉시 갱신", "§5.4"),
        ("신뢰성", "비정상 종료 세션 정리", "VMS 강제 종료 → Web 재시작",
         "5분 무신호 세션 자동 정리(작업 중 표시 해소)", "§7.2"),
        ("신뢰성", "재연결", "네트워크 차단 후 해제",
         "하트비트·실시간 연결 자동 복구", "§7.3"),
        ("기능적합성", "다중 라인 분리", "ClientIndex 다른 VMS 2대(또는 재설정)로 WO 목록 확인",
         "라인별 작업지시·세션 분리", "§5.5"),
    ]),
    ("I. 보안성", GRP[8], [
        ("보안성", "Web Role 권한", "Operator/Lead/Supervisor 각 로그인으로 §3.6 표 검증",
         "레시피 편집(Lead+), External Tools(Supervisor) 등 표와 일치", "§3.6"),
        ("보안성", "로컬 UserGrade 권한", "Operator/Engineer/Admin 계정으로 §3.7 매트릭스 스팟 체크",
         "허용/거부가 표와 일치, 거부 시 감사 기록", "§3.7"),
        ("보안성", "local-admin 제한", "local-admin 으로 사용자 관리·레시피 편집 시도",
         "거부 + 감사 로그 기록 (시작/정지·통계·서비스 재시작만 허용)", "§3.7"),
        ("보안성", "API Key 검증", "VMS 의 Web Client API Key 를 틀리게 설정 후 통신",
         "서버가 요청 거부(또는 호환 모드 정책대로), 로그 확인", "§2.5"),
        ("보안성", "PIN 오입력 처리", "작업자 PIN 반복 오입력",
         "명확한 실패 메시지, 계정 정보 노출 없음", "§3.2.1"),
        ("보안성", "감사 추적성", "위 검증 중 수행한 관리 작업들을 Audit 에서 역추적",
         "사용자·시각·작업이 빠짐없이 기록", "§3.9·§4.15"),
    ]),
    ("J. 사용성 · 문서 일치", GRP[9], [
        ("사용성", "매뉴얼-화면 일치 (VMS)", "매뉴얼 §3 전체를 순서대로 따라 하기",
         "버튼 이름·화면 구성·동작이 문서와 일치 (불일치 = 결함 기록)", "§3"),
        ("사용성", "매뉴얼-화면 일치 (Web)", "매뉴얼 §4 화면별 설명 대조",
         "메뉴 구성·기능 설명 일치", "§4"),
        ("사용성", "트러블슈팅 재현", "§7 항목 중 3건 이상 증상 재현 → 문서 절차로 해결",
         "문서 절차만으로 해결 가능", "§7"),
        ("사용성", "오류 메시지 품질", "대표 오류 상황 5건에서 메시지 확인",
         "원인·조치를 알 수 있는 문구 (코드/스택 노출 없음)", "—"),
    ]),
    ("K. 성능 · 기타", GRP[10], [
        ("성능", "기동 시간", "VMS 콜드 스타트 3회 측정",
         "일관된 기동(비정상 지연 없음), 스플래시 후 메인 표시", "§3.1"),
        ("성능", "연속 검사 안정성", "AUTO RUN 30분 이상 연속 가동",
         "메모리 급증·끊김 없음, 진행률 정상", "—"),
        ("성능", "대량 이력 조회", "Production History 수천 건 상태에서 필터 조회",
         "수 초 내 응답, 페이지 동작 정상", "§4.6"),
    ]),
    # 현장(실카메라) 전용 — 개발 PC 스모크에서 검증 불가한 항목 포함 (2026-07-09 동시 실행
    # 스모크: 프로세스 공존/설정·DB·감사로그 격리/창 제목 분리는 확인 완료, 실카메라 분리
    # 연결·대역폭은 현장 하드웨어 필요)
    ("L. 다중 인스턴스 (한 PC 두 라인 — 현장 실카메라)", GRP[11], [
        ("기능적합성", "인스턴스 분리 실행", "기본 바로가기와 'VMS.exe --instance line2' 바로가기로 VMS 2개 동시 실행",
         "두 창이 각자 설정(창 제목·카메라 목록)으로 기동, 충돌·크래시 없음", "§5.5"),
        ("기능적합성", "실카메라 분리 연결", "실 GigE 카메라 2대를 인스턴스별 1대씩 등록(IP 중복 없이) 후 동시 기동",
         "각 인스턴스가 자기 카메라에만 연결(연결 표시 녹색), 상대 인스턴스 카메라를 잡지 않음", "§5.5"),
        ("신뢰성", "카메라 중복 등록 거동", "같은 카메라 IP 를 두 인스턴스에 등록하고 순차 기동 (오구성 시나리오)",
         "나중에 뜬 쪽만 해당 카메라 연결 실패로 표시 — 크래시·행 없음, 먼저 뜬 쪽 검사 영향 없음", "§5.5"),
        ("성능", "동시 검사 부하 (NIC 공유)", "두 인스턴스 AUTO RUN 동시 가동(각자 실카메라) 30분",
         "프레임 드랍·이미지 깨짐 없음(패킷 크기/인터패킷 딜레이 합산 튜닝), 검사 속도 유지", "§5.5"),
        ("기능적합성", "Web 두 라인 표시", "두 인스턴스를 서로 다른 ClientIndex 로 Web 연결 후 검사",
         "Andon/Dashboard 에 두 라인이 각각 표시, 작업지시 목록 라인별 분리", "§5.5"),
        ("기능적합성", "External Tools 인스턴스 상속", "line2 VMS 에서 Vision Tool Setup / System Setup 실행",
         "자식 앱이 line2 설정·레시피를 사용, AppSetup 헤더에 '인스턴스: line2' 배지 표시", "§5.5"),
        ("신뢰성", "데이터 격리", "두 인스턴스에서 각각 레시피 저장·검사 수행 후 폴더 대조",
         "설정·레시피·사용자 DB·감사 로그·업로드 큐가 instances\\<이름>\\ 아래 완전 분리", "§5.5"),
    ]),
    # v1.5.13~v1.5.16 (#307 #308 #311 #317) + v1.7.1 (#331) — PLC 없이 IO 보드 운전
    ("M. 시퀀스 운전 (PLC · IO 보드)", GRP[12], [
        ("기능적합성", "IO 보드 연결·상태 칩", "IO 보드 구성 후 VMS 기동 → 헤더 IO n/m 칩 확인 → 보드 분리 후 재기동",
         "정상 회색 n/m, 실패 시 적색 + 로그(IoBoard)에 드라이버·오류 번호·보드 번호", "§3.2*"),
        ("기능적합성", "시퀀스 수정 즉시 반영", "VisionSetup 시퀀스 편집기에서 수정·저장 → VMS 재시작 없이 운전 시작",
         "수정된 시퀀스로 운전 (VMS 재시작 불필요)", "§5*"),
        ("기능적합성", "무부하 테스트·모니터", "편집기에서 PLC 없이 IO 보드만으로 무부하 테스트 실행",
         "입력 인덱스=채널 번호로 동작, 노드별 조건 상태 표시", "§5*"),
        ("기능적합성", "트리거 에지 모드", "Wait Trigger 노드를 BitRisingEdge + 디바운스(20~50ms)로 설정 → 스위치를 3초 이상 홀드",
         "1 누름 = 1 사이클 (홀드 시간 무관). BitOn/BitOff 는 레벨 검사로 기존 동작 유지", "§5*"),
        ("신뢰성", "시퀀스 오류 표면화", "보드 노드 주소를 잘못(PLC 식 X0) 입력 후 운전 시작",
         "시퀀스 오류 로그 + 5초 간격 재시도 — 무한 고속 사이클·무증상 정지 없음", "§7*"),
        ("기능적합성", "레시피/스텝 전환 운전", "RecipeChange/StepChange 노드로 품종 전환하며 생산",
         "인덱스에 따른 레시피·스텝 전환 정상, 전환 후에도 외부 레시피 수정 감지 유지", "§5*"),
    ]),
    # v1.6.0~v1.8.0 (#319 #321 #322 #324 #325 #328 #330 #332 #333 #335 #336 #337 + Web #66~#74)
    ("N. 운전 안정화 · Web 연동 (v1.6~v1.8 신규)", GRP[13], [
        ("신뢰성", "카메라 끊김 감지·자동 재연결", "AUTO RUN 중 카메라 케이블 분리 → 3초 대기 → 재연결 → 다음 트리거",
         "'연결 끊김' 즉시 표시, 끊긴 사이클 NG(이전 이미지 재사용 금지), 재연결 시 자동 복구(재시작 불필요)", "§3.3*"),
        ("기능적합성", "판정 엄격화", "검출 0건(Blob/DL/코드판독)·무이미지 검사 케이스 재현",
         "합격 처리되지 않고 NG — 기존 OK 로 나오던 레시피는 현장 공지 필요", "§4*"),
        ("기능적합성", "1사이클 = 1개 집계", "트리거 1회 → 대시보드·Web 수량 대조",
         "Total/OK/NG 1씩 증가, WO 수량 1 증가 (2배 증가 회귀 없음)", "§3.4*"),
        ("기능적합성", "WO 완료 기준 (양품/총생산)", "Web 에서 완료 기준이 다른 WO 2건 생성 → 각각 생산",
         "기준에 맞는 진행률 칩·자동 완료 (기본 총생산, 양품 채우기는 명시적 선택)", "§3.8*"),
        ("기능적합성", "혼합 레시피 WO", "레시피 2개 라인 WO 생성 → 품종 전환하며 생산 + 미등록 레시피로도 검사",
         "라인별 귀속·전 라인 도달 시 완료, 미등록 레시피는 미집계 + 경고 로그", "§3.8*"),
        ("기능적합성", "Web 수동 완료 → VMS 전파", "AUTO RUN 생산 중 Web 화면에서 해당 WO 완료 처리",
         "VMS 수 초 내 자동 정지 + 완료 다이얼로그 + WO 선택 해제 (SignalR 미연결 시 다음 업로드 응답 폴백)", "§3.8*"),
        ("기능적합성", "레시피 수정 반영·변경 대기 배지", "AUTO RUN 중 VisionSetup 에서 저장 → 배지 확인 → STOP",
         "'변경 대기' 주황 배지 → 정지 시 자동 재로드, 레시피 목록에 중복 파일 미생성", "§5*"),
        ("사용성", "Web 연동 파라미터 배지", "링크된 파라미터의 로컬 값을 Web 값과 다르게 조작",
         "'Web 연동 중' 배지 상시 표시, 불일치 시 주황 경고 (운전은 Web 값 사용 안내)", "§4*"),
        ("기능적합성", "NG 이미지 Web 표시", "전달 모드 Auto + 'NG 이미지 Web 전송' ON → NG 발생 → Web 이력 상세",
         "NG 이미지 표시 ('없습니다' 미출현), 다중 카메라 사이클은 NG 카메라 이미지 우선", "§4.6*"),
        ("사용성", "시뮬레이션 모드 경고", "카메라 SDK 미탑재 빌드에서 카메라 연결",
         "경고 대화상자/SIMULATION 표기 — 시뮬레이션 영상을 실카메라로 오인하지 않게 함", "§3.3*"),
    ]),
]

# ============================================================ 1) 안내 시트
ws = wb.active; ws.title = "안내"
ws.sheet_view.showGridLines = False
for col, w in {"A": 2.5, "B": 20, "C": 30, "D": 30, "E": 18}.items():
    ws.column_dimensions[col].width = w
ws.merge_cells("B2:E3")
put(ws, "B2", "BODA VMS · VMS.Web 자체 검증 테스트 체크리스트", F(bold=True, size=14, white=True), NAVY, CTR)
ws.merge_cells("B4:E4")
put(ws, "B4", "GS 시험 리허설 — v1.8.0 기준 재정비 (2026-08-19, 인증 획득 목표 10/30) — 결과물: 검증 결과서", F(size=11, white=True), BLUE, CTR)

def kv(row, k, v, key_fill=FILL_LABEL):
    put(ws, f"B{row}", k, F(bold=True, size=10), key_fill)
    ws.merge_cells(f"C{row}:E{row}")
    put(ws, f"C{row}", v, F(size=10))

ws.merge_cells("B6:E6"); put(ws, "B6", "1. 사용 방법", F(bold=True, size=12, white=True), BLUE)
kv(7,  "진행", "[체크리스트] 시트를 위에서 아래로 진행 — '확인 방법'대로 조작하고 '기대 결과'와 대조")
kv(8,  "판정", "결과 열에서 선택: PASS(일치) / FAIL(불일치·오류) / N-A(해당 구성 없음) / 보류(환경 준비 후 재시도)")
kv(9,  "결함", "FAIL 은 [결함 기록] 시트에 번호를 만들어 증상·재현 절차 기록 → 수정 후 재확인 시 상태 갱신")
kv(10, "근거", "근거(§) 열은 사용자매뉴얼 v1.1 의 해당 절 — 시험관도 같은 문서로 확인하므로 문구 불일치도 결함임. "
               "§ 뒤 * 는 매뉴얼 반영 대기 항목(코드 선반영, docs/gs/README.md 표 참조) — 매뉴얼 갱신 후 * 제거")

ws.merge_cells("B12:E12"); put(ws, "B12", "2. 결과 요약 (자동 집계)", F(bold=True, size=12, white=True), BLUE)
put(ws, "B13", "전체 항목", F(bold=True, size=10), FILL_LABEL); put(ws, "C13", "=COUNTA(체크리스트!D:D)-1", F(size=10), align=CTR)
put(ws, "B14", "PASS", F(bold=True, color="2E7D32", size=10), PASS_G); put(ws, "C14", '=COUNTIF(체크리스트!H:H,"PASS")', F(size=10), align=CTR)
put(ws, "B15", "FAIL", F(bold=True, color="C00000", size=10), FAIL_R); put(ws, "C15", '=COUNTIF(체크리스트!H:H,"FAIL")', F(size=10), align=CTR)
put(ws, "B16", "N-A / 보류", F(bold=True, size=10), NA_GY); put(ws, "C16", '=COUNTIF(체크리스트!H:H,"N-A")+COUNTIF(체크리스트!H:H,"보류")', F(size=10), align=CTR)
put(ws, "B17", "진행률", F(bold=True, size=10), FILL_LABEL); put(ws, "C17", "=IFERROR((C14+C15+C16)/C13,0)", F(size=10), align=CTR, numfmt="0%")

ws.merge_cells("B19:E19"); put(ws, "B19", "3. 완료 기준", F(bold=True, size=12, white=True), BLUE)
kv(20, "완료 조건", "전 항목 판정 완료 + FAIL 0건(수정·재확인 반영 후) — 마일스톤 '자체 검증 테스트 완료(7/31)'", FILL_NOTE)
kv(21, "환경", "시험과 같은 구성 권장: 클린 PC 설치본 + Web 서버 + (가능 시) 실제/가상 카메라", FILL_NOTE)

# ============================================================ 2) 체크리스트 시트
ws = wb.create_sheet("체크리스트")
widths = {"A": 4, "B": 10, "C": 11, "D": 28, "E": 46, "F": 34, "G": 8, "H": 8, "I": 22}
for col, w in widths.items(): ws.column_dimensions[col].width = w
ws.freeze_panes = "D4"
ws.merge_cells("A1:I1")
put(ws, "A1", "자체 검증 체크리스트 — GS 시험 리허설", F(bold=True, size=14, white=True), NAVY, CTR)
ws.row_dimensions[1].height = 21
ws.merge_cells("A2:I2")
put(ws, "A2", "결과 열: PASS / FAIL / N-A / 보류 (드롭다운) · FAIL 은 [결함 기록] 시트에 상세 기록",
    F(color="595959", size=10), None, WRAP, border=False)
for i, h in enumerate(["No", "품질 특성", "영역", "점검 항목", "확인 방법", "기대 결과", "근거(§)", "결과", "결함 No/메모"]):
    put(ws, f"{get_column_letter(1+i)}3", h, F(bold=True, white=True), NAVY, CTR)
ws.row_dimensions[3].height = 34

dv = DataValidation(type="list", formula1='"PASS,FAIL,N-A,보류"', allow_blank=True, showDropDown=False)
ws.add_data_validation(dv)

row = 4; no = 1
for sec_title, sec_color, items in SECTIONS:
    ws.merge_cells(f"B{row}:I{row}")
    put(ws, f"B{row}", sec_title, F(bold=True), sec_color)
    put(ws, f"A{row}", None, fl=sec_color)
    area = sec_title.split(".")[1].strip().split(" ")[0].split("(")[0]
    row += 1
    for (charac, item, how, expect, ref) in items:
        put(ws, f"A{row}", no, align=CTR)
        put(ws, f"B{row}", charac, F(size=10), align=CTR)
        put(ws, f"C{row}", area, F(size=10), align=CTR)
        put(ws, f"D{row}", item, F(size=10))
        put(ws, f"E{row}", how, F(size=10))
        put(ws, f"F{row}", expect, F(size=10))
        put(ws, f"G{row}", ref, F(size=10), align=CTR)
        put(ws, f"H{row}", None, F(size=10), align=CTR)
        put(ws, f"I{row}", None, F(size=10))
        dv.add(f"H{row}")
        ws.row_dimensions[row].height = 34
        no += 1; row += 1
last = row - 1
res_range = f"H4:H{last}"
ws.conditional_formatting.add(res_range, CellIsRule(operator="equal", formula=['"PASS"'],
    fill=PatternFill(start_color=PASS_G, end_color=PASS_G, fill_type="solid"), font=Font(color="2E7D32", bold=True)))
ws.conditional_formatting.add(res_range, CellIsRule(operator="equal", formula=['"FAIL"'],
    fill=PatternFill(start_color=FAIL_R, end_color=FAIL_R, fill_type="solid"), font=Font(color="C00000", bold=True)))
ws.conditional_formatting.add(res_range, CellIsRule(operator="equal", formula=['"N-A"'],
    fill=PatternFill(start_color=NA_GY, end_color=NA_GY, fill_type="solid"), font=Font(color="808080")))

# ============================================================ 3) 결함 기록 시트
ws = wb.create_sheet("결함 기록")
for col, w in {"A": 3, "B": 8, "C": 11, "D": 10, "E": 34, "F": 40, "G": 10, "H": 30, "I": 10}.items():
    ws.column_dimensions[col].width = w
ws.merge_cells("B2:I2")
put(ws, "B2", "결함 기록 — 자체 검증에서 발견된 문제", F(bold=True, size=14, white=True), NAVY, CTR)
ws.row_dimensions[2].height = 21
for i, h in enumerate(["결함 No", "발견일", "관련 항목 No", "증상 (무엇이 잘못됐나)", "재현 절차", "심각도", "조치 내용", "상태"]):
    put(ws, f"{get_column_letter(2+i)}3", h, F(bold=True, white=True), BLUE, CTR)
dv2 = DataValidation(type="list", formula1='"치명,높음,보통,낮음"', allow_blank=True, showDropDown=False)
dv3 = DataValidation(type="list", formula1='"신규,수정중,수정완료,재확인완료"', allow_blank=True, showDropDown=False)
ws.add_data_validation(dv2); ws.add_data_validation(dv3)
for r in range(4, 24):
    for i in range(8):
        put(ws, f"{get_column_letter(2+i)}{r}", None, F(size=10))
    put(ws, f"B{r}", f"D-{r-3:02d}", F(color="595959", size=10), align=CTR)
    dv2.add(f"G{r}"); dv3.add(f"I{r}")
ws.conditional_formatting.add("I4:I23", CellIsRule(operator="equal", formula=['"재확인완료"'],
    fill=PatternFill(start_color=PASS_G, end_color=PASS_G, fill_type="solid"), font=Font(color="2E7D32", bold=True)))

wb.save(OUT)
total = sum(len(items) for _, _, items in SECTIONS)
print("WROTE", OUT, "| items:", total)
