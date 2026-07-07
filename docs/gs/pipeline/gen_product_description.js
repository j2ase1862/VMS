// GS 제출용 제품설명서 (ISO/IEC 25051 기준) + 기능명세 생성기
// 출력: docs/gs/VMS_제품설명서_v1.0.docx
const path = require("path");
const fs = require("fs");
const GLOBAL = "C:/Users/vinos/AppData/Roaming/npm/node_modules";
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  VerticalAlign, PageNumber, PageBreak, Header, Footer, ImageRun, TableOfContents,
} = require(path.join(GLOBAL, "docx"));

const SHOT = path.join(__dirname, "..", "screenshots");
const OUT = path.join(__dirname, "..", "VMS_제품설명서_v1.0.docx");

// ---- A4 ----
const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1440;
const CONTENT_W = PAGE_W - 2 * MARGIN; // 9026

// ---- helpers ----
const border = { style: BorderStyle.SINGLE, size: 1, color: "BFBFBF" };
const borders = { top: border, bottom: border, left: border, right: border };
const cellMargins = { top: 60, bottom: 60, left: 110, right: 110 };

function P(text, opts = {}) {
  return new Paragraph({
    spacing: { after: 120, line: 276, ...(opts.spacing || {}) },
    alignment: opts.alignment,
    children: [new TextRun({ text, bold: opts.bold, italics: opts.italics, size: opts.size, color: opts.color })],
  });
}
function H1(text) { return new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(text)] }); }
function H2(text) { return new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(text)] }); }
function H3(text) { return new Paragraph({ heading: HeadingLevel.HEADING_3, children: [new TextRun(text)] }); }
function bullet(text, level = 0) {
  return new Paragraph({ numbering: { reference: "b", level }, spacing: { after: 60, line: 276 },
    children: [new TextRun({ text, size: 21 })] });
}

function hcell(text, w, fill = "1F3864") {
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: { fill, type: ShadingType.CLEAR }, verticalAlign: VerticalAlign.CENTER,
    children: [new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text, bold: true, color: "FFFFFF", size: 19 })] })] });
}
function cell(text, w, opts = {}) {
  const runs = Array.isArray(text) ? text : [new TextRun({ text: String(text), size: 19, bold: opts.bold, font: opts.mono ? "Consolas" : undefined })];
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: opts.fill ? { fill: opts.fill, type: ShadingType.CLEAR } : undefined,
    verticalAlign: VerticalAlign.CENTER,
    children: [new Paragraph({ spacing: { after: 0, line: 252 }, children: runs })] });
}

// 2-col info table (label/value)
function infoTable(rows) {
  const L = 2600, R = CONTENT_W - L;
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [L, R],
    rows: rows.map(([k, v]) => new TableRow({ children: [
      cell(k, L, { bold: true, fill: "EAEFF7" }), cell(v, R),
    ] })) });
}

// Function list table: ID | 기능명(UI) | 설명
function funcTable(items) {
  const c1 = 1150, c2 = 2700, c3 = CONTENT_W - c1 - c2;
  const head = new TableRow({ tableHeader: true, children: [
    hcell("ID", c1), hcell("기능명 (UI 라벨)", c2), hcell("설명", c3),
  ] });
  const rows = items.map((it, i) => new TableRow({ children: [
    cell(it[0], c1, { mono: true, fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
    cell(it[1], c2, { bold: true, fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
    cell(it[2], c3, { fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
  ] }));
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [c1, c2, c3], rows: [head, ...rows] });
}

function image(file, wPx) {
  const full = path.join(SHOT, file);
  if (!fs.existsSync(full)) return P(`[스크린샷 누락: ${file}]`, { italics: true, color: "C00000" });
  // intrinsic size
  const buf = fs.readFileSync(full);
  // PNG dims from IHDR
  const iw = buf.readUInt32BE(16), ih = buf.readUInt32BE(20);
  const hPx = Math.round(wPx * ih / iw);
  return new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 80, after: 80 },
    children: [new ImageRun({ type: "png", data: buf, transformation: { width: wPx, height: hPx },
      altText: { title: file, description: file, name: file } })] });
}
function caption(text) {
  return new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 200 },
    children: [new TextRun({ text, italics: true, size: 18, color: "595959" })] });
}

// ============================ CONTENT ============================
const children = [];

// ---- 표지 ----
children.push(new Paragraph({ spacing: { before: 2400, after: 0 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "제품설명서", bold: true, size: 60, color: "1F3864" })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 120 },
  children: [new TextRun({ text: "(Product Description)", size: 28, color: "595959" })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 240, after: 60 },
  children: [new TextRun({ text: "BODA Vision Management System (VMS)", bold: true, size: 34 })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 600 },
  children: [new TextRun({ text: "산업용 머신비전 검사 플랫폼", size: 24, color: "595959" })] }));
children.push(image("01_vms_main.png", 460));
children.push(new Paragraph({ spacing: { before: 600 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "ISO/IEC 25051 기준 / GS 인증 제출용", size: 22, color: "595959" })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 120 },
  children: [new TextRun({ text: "버전 1.0 · 작성일 2026-06-12", size: 22 })] }));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---- 문서 정보 ----
children.push(H1("문서 정보"));
children.push(infoTable([
  ["제품명", "BODA Vision Management System (VMS)"],
  ["제품 버전", "v1.0  ([정식 릴리스 버전 기입])"],
  ["문서 버전", "1.0"],
  ["작성일", "2026-06-12"],
  ["대상 운영체제", "Windows 10 / 11 (x64)"],
  ["개발 언어 / 프레임워크", "C# / .NET 8.0 (WPF)"],
  ["신청 기업", "[회사명·사업자등록번호 기입]"],
  ["작성자 / 연락처", "[작성자·연락처 기입]"],
]));
children.push(P("※ 대괄호 [ ] 항목은 신청 기관 제출 전 신청인 정보로 채워야 합니다.", { italics: true, size: 18, color: "C00000" }));

children.push(new TableOfContents("목차", { hyperlink: true, headingStyleRange: "1-2" }));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---- 1. 제품 개요 ----
children.push(H1("1. 제품 개요"));
children.push(P("BODA Vision Management System(이하 VMS)은 산업 현장의 자동 외관·치수 검사를 위한 머신비전 검사 플랫폼이다. 사용자는 카메라로 취득한 이미지에 비전 도구(전처리, 패턴 매칭, 블롭, 측정, OCR/코드 판독, 딥러닝 등)를 조합한 검사 레시피를 구성하고, 생산 라인에서 자동/수동으로 검사를 실행하여 양품(OK)·불량(NG) 판정과 통계를 관리한다."));
children.push(P("VMS는 .NET 8.0 WPF 기반 데스크톱 애플리케이션과, 선택적으로 연동되는 웹(MES/품질관리) 서버로 구성된다. 검사 결과·작업지시·추적성 정보를 웹 서버와 동기화하여 다중 라인 운영과 생산 이력 관리를 지원한다."));
children.push(H2("1.1 주요 특징"));
[
  "드래그앤드롭 방식의 비전 도구 워크스페이스(VMS.VisionSetup)로 코딩 없이 검사 레시피 구성",
  "12개 카테고리의 비전 도구 및 ROI(검사 영역) 지정(Rectangle/Circle/Ellipse/Polygon 등) 제공",
  "OpenCvSharp4(영상처리) · ONNX Runtime(딥러닝) · Tesseract(OCR) · ZXing(코드 판독) 통합",
  "카메라 미연결 환경을 위한 파일 불러오기(이미지 로드) 기반 검사 — 가상 검사 가능",
  "작업지시(Work Order)·Lot·Serial·작업자 4필드 추적성 및 검사 이미지 저장/업로드",
  "역할 기반 권한(UserGrade) 및 감사 로그(Audit Log) 등 GS 보안성 요구 대응",
].forEach((t) => children.push(bullet(t)));

// ---- 2. 목적 및 적용 범위 ----
children.push(H1("2. 목적 및 적용 범위"));
children.push(H2("2.1 목적"));
children.push(P("본 제품설명서는 VMS의 제품 개요, 동작 환경(하드웨어/소프트웨어 요구사항), 그리고 구현·검증된 기능 명세(Function List)를 정의한다. 본 문서에 기술된 기능 목록은 GS 인증 시험 시나리오의 기준이 된다."));
children.push(H2("2.2 적용 범위"));
children.push(P("적용 범위는 VMS 데스크톱 제품군(운영 클라이언트 VMS, 시스템 설정 VMS.AppSetup, 비전 설정 VMS.VisionSetup, 딥러닝 VMS.DeepLearning)과 이와 연동되는 BODA.VMS.Web(관리/MES) 서버를 포함한다."));
children.push(P("※ 본 문서에는 현재 정식 구현·동작이 확인된 기능만 기술하였다.", { italics: true, size: 18, color: "595959" }));

// ---- 3. 제품 구성 ----
children.push(H1("3. 제품 구성"));
children.push(P("VMS는 다음 모듈로 구성된다."));
{
  const c1 = 2600, c2 = CONTENT_W - c1;
  children.push(new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [c1, c2], rows: [
    new TableRow({ tableHeader: true, children: [hcell("모듈", c1), hcell("역할", c2)] }),
    ...[
      ["VMS (운영 클라이언트)", "생산 현장 운영 화면. 카메라 제어/검사 실행(AUTO RUN)·판정·통계·작업지시·이미지 저장·관리자 도구."],
      ["VMS.AppSetup (시스템 설정)", "최초 1회 시스템 구성 마법사. 애플리케이션·네트워크·카메라·PLC·로봇·초기 관리자 계정 설정."],
      ["VMS.VisionSetup (비전 설정)", "비전 도구 워크스페이스. 카메라 취득·ROI 지정·도구 조합·레시피/시퀀스 편집·배치 테스트·캘리브레이션."],
      ["VMS.DeepLearning (딥러닝)", "딥러닝 데이터셋 라벨링·학습·추론 보조 도구(선택)."],
      ["NativeVision (C++ 가속)", "AVX2/FMA 기반 영상처리 가속 네이티브 라이브러리."],
      ["BODA.VMS.Web (관리/MES)", "ASP.NET Core 8 + Blazor 웹. 대시보드·작업지시·생산이력·검사이미지 조회·알람·감사로그 등(선택 연동)."],
    ].map(([a, b], i) => new TableRow({ children: [
      cell(a, c1, { bold: true, fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
      cell(b, c2, { fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
    ] })),
  ] }));
}

// ---- 4. 동작 환경 ----
children.push(H1("4. 동작 환경 및 요구사항"));
children.push(H2("4.1 하드웨어 요구사항"));
{
  const c1 = 2600, c2 = CONTENT_W - c1;
  children.push(new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [c1, c2], rows: [
    new TableRow({ tableHeader: true, children: [hcell("항목", c1), hcell("권장 사양", c2)] }),
    ...[
      ["CPU", "Intel Core i5 이상 (AVX2 지원 권장)"],
      ["메모리", "8GB 이상 (16GB 권장)"],
      ["저장장치", "SSD 256GB 이상 (검사 이미지 저장 시 여유 용량 추가)"],
      ["GPU", "딥러닝/TensorRT 사용 시 NVIDIA GPU (CUDA 지원). 미사용 시 불필요"],
      ["카메라", "GigE Vision 등 산업용 카메라. 미연결 시 파일 불러오기 모드로 검사 가능"],
      ["디스플레이", "1920×1080 이상 권장"],
    ].map(([a, b], i) => new TableRow({ children: [
      cell(a, c1, { bold: true, fill: i % 2 ? "F4F7FB" : "FFFFFF" }), cell(b, c2, { fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
    ] })),
  ] }));
}
children.push(H2("4.2 소프트웨어 요구사항"));
[
  "운영체제: Windows 10 / 11 (64-bit)",
  ".NET 8.0 Desktop Runtime",
  "Visual C++ 재배포 패키지 (네이티브 영상처리 라이브러리용)",
].forEach((t) => children.push(bullet(t)));
children.push(H2("4.3 주요 사용 라이브러리"));
children.push(P("VMS가 사용하는 주요 오픈소스/서드파티 라이브러리는 다음과 같다. (라이선스 고지는 별도 'OSS 라이선스 확인서' 참조)"));
{
  const c1 = 3400, c2 = 1700, c3 = CONTENT_W - c1 - c2;
  children.push(new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [c1, c2, c3], rows: [
    new TableRow({ tableHeader: true, children: [hcell("라이브러리", c1), hcell("버전", c2), hcell("용도", c3)] }),
    ...[
      ["OpenCvSharp4 (+ Extensions/WpfExtensions/runtime.win)", "4.11.0", "영상처리"],
      ["CommunityToolkit.Mvvm", "8.4.0", "MVVM 인프라"],
      ["HelixToolkit.Wpf.SharpDX", "3.1.2", "3D 포인트클라우드 시각화"],
      ["Microsoft.ML.OnnxRuntime.Gpu", "1.21.0", "딥러닝 추론"],
      ["Tesseract", "5.2.0", "OCR 문자 인식"],
      ["ZXing.Net", "0.16.11", "바코드/QR 판독"],
      ["Microsoft.AspNetCore.SignalR.Client", "8.0.0", "Web 실시간 연동"],
      ["Microsoft.Data.Sqlite", "8.0.0", "로컬 데이터 저장"],
      ["BCrypt.Net-Next", "4.0.3", "비밀번호 해시"],
    ].map(([a, b, c], i) => new TableRow({ children: [
      cell(a, c1, { fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
      cell(b, c2, { mono: true, fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
      cell(c, c3, { fill: i % 2 ? "F4F7FB" : "FFFFFF" }),
    ] })),
  ] }));
}

// ---- 5. 설치 및 실행 개요 ----
children.push(H1("5. 설치 및 실행 개요"));
children.push(P("상세 절차 및 화면은 '사용자 취급 설명서(매뉴얼)'에서 스크린샷과 함께 다룬다. 본 절은 개요만 기술한다."));
[
  "1) MSI 설치 패키지로 VMS 클라이언트를 설치한다.",
  "2) 최초 실행 시 시스템 설정 마법사(VMS.AppSetup)가 자동 실행되어 애플리케이션·네트워크·카메라·PLC·초기 관리자 계정을 구성한다.",
  "3) 구성 완료 후 VMS를 실행하면 운영 화면이 표시된다.",
  "4) 검사 레시피는 비전 설정(VMS.VisionSetup)에서 작성하여 운영 클라이언트에서 로드한다.",
].forEach((t) => children.push(bullet(t)));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ---- 6. 기능 명세 ----
children.push(H1("6. 기능 명세 (Function List)"));
children.push(P("아래 기능명(UI 라벨)은 실제 화면의 버튼/메뉴 텍스트와 일치한다. 영문 라벨은 화면 표기를 그대로 표기하였다."));

children.push(H2("6.1 시스템 설정 — VMS.AppSetup"));
children.push(funcTable([
  ["AS-01", "Application / Network", "애플리케이션 기본 설정 및 네트워크(Web 서버 주소 등) 구성"],
  ["AS-02", "Camera", "카메라 제조사·IP 등 연결 설정"],
  ["AS-03", "PLC", "PLC 통신(벤더/IP/프로토콜) 설정"],
  ["AS-04", "Robot", "로봇 연동 설정 (선택)"],
  ["AS-05", "Admin 비밀번호", "초기 VMS Admin / Local Fallback Admin 계정 비밀번호 설정(기본 비밀번호 시드 없음)"],
]));

children.push(H2("6.2 운영 클라이언트 — VMS (메인 화면)"));
children.push(H3("6.2.1 카메라 제어 / 검사 실행"));
children.push(funcTable([
  ["OP-01", "Grab", "카메라에서 단일 이미지 1장 취득"],
  ["OP-02", "Live Start", "연속 라이브 그랩 시작"],
  ["OP-03", "Live Stop", "라이브 그랩 정지"],
  ["OP-04", "Roller", "롤러 검사 모드 토글"],
  ["OP-05", "AUTO RUN", "자동 검사 운영 시작(작업자 로그인·레시피 등 조건 충족 시)"],
  ["OP-06", "STOP", "자동 운영 정지"],
]));
children.push(H3("6.2.2 레시피 관리"));
children.push(funcTable([
  ["RC-01", "Load", "선택한 레시피 로드"],
  ["RC-02", "New", "새 레시피 생성"],
  ["RC-03", "Save", "현재 레시피 저장"],
  ["RC-04", "Export", "레시피 파일 내보내기"],
  ["RC-05", "Import", "레시피 파일 가져오기"],
  ["RC-06", "Available Recipes", "사용 가능한 레시피 목록 표시/선택"],
]));
children.push(H3("6.2.3 작업지시 / 추적성"));
children.push(funcTable([
  ["WO-01", "Work Orders", "작업지시 목록 조회·선택(Order No / Product / Recipe / Progress / Status / Planned Start)"],
  ["WO-02", "Operator (Login / Logout)", "작업자 로그인/로그아웃"],
  ["WO-03", "Ctx: WO / Lot / S/N", "작업지시·Lot·시리얼 컨텍스트 입력(추적성 첨부)"],
]));
children.push(H3("6.2.4 통계 / 결과 모니터링"));
children.push(funcTable([
  ["ST-01", "TACT TIME / YIELD / TOTAL / OK / NG", "실시간 검사 KPI 표시"],
  ["ST-02", "Recent Inspections", "최근 검사 이력(Total / Pass / Fail) 및 Clear"],
  ["ST-03", "NG History", "불량 이미지 히스토리 표시"],
]));
children.push(H3("6.2.5 이미지 저장 / Web 연동 / 업데이트"));
children.push(funcTable([
  ["IM-01", "Image Save Settings", "양품/불량 이미지 저장(저장 경로·포맷/품질·파일명 규칙·보존 기간·Web 전송) 설정"],
  ["SY-01", "Sync Parameters", "Web 서버와 레시피 파라미터 동기화"],
  ["UP-01", "Check for updates", "신규 버전 확인"],
]));
children.push(H3("6.2.6 외부 도구 실행"));
children.push(funcTable([
  ["EX-01", "Vision Tool Setup", "비전 설정(VMS.VisionSetup) 실행"],
  ["EX-02", "System Setup", "시스템 설정(VMS.AppSetup) 실행"],
]));
children.push(H3("6.2.7 관리자 도구 (Admin 전용)"));
children.push(funcTable([
  ["AD-01", "User Management", "사용자 계정·권한 관리"],
  ["AD-02", "Audit Log Viewer", "감사 로그 조회"],
  ["AD-03", "Health Check", "시스템 상태 점검"],
  ["AD-04", "Backup / Restore", "데이터 백업 및 복원"],
  ["AD-05", "Auto Backup Settings", "자동 백업 정책 설정"],
  ["AD-06", "Retention Settings", "데이터 보존 정책 설정"],
  ["AD-07", "Support Package", "원격 지원용 진단 패키지 생성"],
]));

children.push(H2("6.3 비전 설정 — VMS.VisionSetup"));
children.push(H3("6.3.1 워크스페이스 / 카메라 / 레시피"));
children.push(funcTable([
  ["VS-01", "Camera", "카메라 선택 및 이미지 취득(미연결 시 파일 불러오기)"],
  ["VS-02", "Steps", "검사 스텝 추가/삭제/순서 변경"],
  ["VS-03", "Recipe", "레시피 신규/열기/저장"],
  ["VS-04", "Run", "검사 파이프라인 1회/연속 실행"],
  ["VS-05", "Sequence", "시퀀스(디바이스 연동 흐름) 편집"],
  ["VS-06", "Batch Test", "다수 이미지 일괄 검사/검증"],
  ["VS-07", "Calibration", "카메라/핸드아이 캘리브레이션"],
]));
children.push(H3("6.3.2 ROI(검사 영역) 지정"));
children.push(funcTable([
  ["RoI-01", "Select", "ROI 선택/편집 모드"],
  ["RoI-02", "Rectangle", "사각형 ROI 지정"],
  ["RoI-03", "RectAffine", "회전 사각형(Affine) ROI 지정"],
  ["RoI-04", "Circle", "원형 ROI 지정"],
  ["RoI-05", "Ellipse", "타원 ROI 지정"],
  ["RoI-06", "Polygon", "다각형 ROI 지정"],
]));
children.push(H3("6.3.3 이미지 뷰"));
children.push(funcTable([
  ["IV-01", "2D Image / Depth Map / Point Cloud", "2D·깊이맵·포인트클라우드 뷰 전환"],
  ["IV-02", "Save Image", "현재 표시 이미지 저장"],
  ["IV-03", "Fit / + / − / Delete / Clear All", "화면 맞춤·확대/축소·삭제·전체 지우기"],
]));
children.push(H3("6.3.4 비전 도구 팔레트 (12 카테고리)"));
children.push(funcTable([
  ["TP-01", "Preprocessing (Color)", "컬러 전처리 도구"],
  ["TP-02", "Conversion (Gray)", "그레이 변환 도구"],
  ["TP-03", "Pattern Matching", "패턴 매칭"],
  ["TP-04", "Blob Analysis", "블롭 분석"],
  ["TP-05", "Measurement", "치수 측정(캘리퍼/라인핏/원핏 등)"],
  ["TP-06", "Identification", "OCR/OCV 문자 식별"],
  ["TP-07", "Code Reading", "바코드/QR 판독"],
  ["TP-08", "3D Analysis", "3D 분석"],
  ["TP-09", "Deep Learning", "딥러닝 추론 도구"],
  ["TP-10", "Judgment", "종합 판정"],
  ["TP-11", "Color", "색상 검사"],
  ["TP-12", "Calibration", "캘리브레이션 도구"],
]));

children.push(H2("6.4 관리/MES 웹 — BODA.VMS.Web (선택 연동)"));
children.push(funcTable([
  ["WB-01", "Dashboard", "라인 생산/품질 KPI 대시보드"],
  ["WB-02", "Production History", "검사 생산 이력 조회(검사 이미지 열람 포함)"],
  ["WB-03", "Work Orders", "작업지시 생성/진행/완료 관리"],
  ["WB-04", "Inspection Items", "레시피/파라미터 관리"],
  ["WB-05", "Alarms / Andon", "알람 및 안돈 보드"],
  ["WB-06", "Audit Logs", "감사 로그 조회"],
  ["WB-07", "Reports", "품질 분석 리포트"],
]));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ---- 7. 대표 화면 ----
children.push(H1("7. 대표 화면"));
children.push(P("아래는 제품의 대표 화면이다. 화면별 상세 조작법은 '사용자 취급 설명서(매뉴얼)'에서 다룬다."));
children.push(H2("7.1 운영 클라이언트 (VMS) 메인 화면"));
children.push(image("01_vms_main.png", 600));
children.push(caption("그림 7-1. VMS 운영 메인 화면 — 상단 운영 흐름(작업자/작업지시/AUTO RUN), KPI 스트립, 카메라 표시 영역, 사이드 패널(Settings)"));
children.push(H2("7.2 비전 설정 (VMS.VisionSetup) 워크스페이스"));
children.push(image("03_visionsetup.png", 600));
children.push(caption("그림 7-2. 비전 설정 — 카메라/Steps/Tool Palette(12 카테고리), Tool Workspace, ROI 도구(Select/Rectangle/RectAffine/Circle/Ellipse/Polygon) 및 이미지 뷰"));
children.push(H2("7.3 시스템 설정 마법사 (VMS.AppSetup)"));
children.push(image("02_appsetup_welcome.png", 420));
children.push(caption("그림 7-3. 시스템 설정 마법사 시작 화면"));

// ---- 문서 이력 ----
children.push(new Paragraph({ children: [new PageBreak()] }));
children.push(H1("문서 이력"));
{
  const c1 = 1600, c2 = 1600, c3 = CONTENT_W - c1 - c2;
  children.push(new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [c1, c2, c3], rows: [
    new TableRow({ tableHeader: true, children: [hcell("버전", c1), hcell("일자", c2), hcell("변경 내용", c3)] }),
    new TableRow({ children: [cell("1.0", c1), cell("2026-06-12", c2), cell("최초 작성 (제품 개요·동작환경·기능명세·대표화면)", c3)] }),
  ] }));
}

// ============================ DOC ============================
const doc = new Document({
  creator: "BODA VMS",
  title: "VMS 제품설명서",
  styles: {
    default: { document: { run: { font: "Malgun Gothic", size: 21 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 30, bold: true, color: "1F3864", font: "Malgun Gothic" },
        paragraph: { spacing: { before: 280, after: 160 }, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 25, bold: true, color: "2E5496", font: "Malgun Gothic" },
        paragraph: { spacing: { before: 220, after: 120 }, outlineLevel: 1 } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 22, bold: true, color: "404040", font: "Malgun Gothic" },
        paragraph: { spacing: { before: 160, after: 80 }, outlineLevel: 2 } },
    ],
  },
  numbering: { config: [
    { reference: "b", levels: [
      { level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 520, hanging: 260 } } } },
      { level: 1, format: LevelFormat.BULLET, text: "–", alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 980, hanging: 260 } } } },
    ] },
  ] },
  sections: [{
    properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      children: [new TextRun({ text: "BODA VMS 제품설명서 v1.0", size: 16, color: "808080" })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ text: "", size: 16, color: "808080" }),
        new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080" }),
        new TextRun({ text: " / ", size: 16, color: "808080" }),
        new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: "808080" })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then((buf) => { fs.writeFileSync(OUT, buf); console.log("WROTE " + OUT + " (" + buf.length + " bytes)"); });
