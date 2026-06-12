// GS 제출용 사용자 취급 설명서(매뉴얼) 생성기
// 입력: _manual_blocks.json (parse_manual.py 출력) + screenshots/
// 출력: VMS_사용자매뉴얼_v1.0.docx
const path = require("path");
const fs = require("fs");
const GLOBAL = "C:/Users/vinos/AppData/Roaming/npm/node_modules";
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  VerticalAlign, PageNumber, PageBreak, Header, Footer, ImageRun, TableOfContents,
} = require(path.join(GLOBAL, "docx"));

const DIR = __dirname;
const SHOT = path.join(DIR, "screenshots");
const blocks = JSON.parse(fs.readFileSync(path.join(DIR, "_manual_blocks.json"), "utf-8"));
const tools = JSON.parse(fs.readFileSync(path.join(DIR, "_tool_params.json"), "utf-8"));
const OUT = path.join(DIR, "VMS_사용자매뉴얼_v1.0.docx");

const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1440;
const CONTENT_W = PAGE_W - 2 * MARGIN; // 9026

const border = { style: BorderStyle.SINGLE, size: 1, color: "BFBFBF" };
const borders = { top: border, bottom: border, left: border, right: border };
const cellMargins = { top: 50, bottom: 50, left: 100, right: 100 };

function runs(text, opts = {}) {
  // split on \n into separate breaks
  const parts = String(text).split("\n");
  const out = [];
  parts.forEach((seg, i) => {
    if (i) out.push(new TextRun({ break: 1 }));
    out.push(new TextRun({ text: seg, size: opts.size || 21, bold: opts.bold, color: opts.color, font: opts.mono ? "Consolas" : undefined }));
  });
  return out;
}
function P(text, opts = {}) {
  return new Paragraph({ spacing: { after: 100, line: 276 }, alignment: opts.alignment, children: runs(text, opts) });
}
function bullet(text) {
  return new Paragraph({ numbering: { reference: "b", level: 0 }, spacing: { after: 50, line: 264 },
    children: runs(text, { size: 21 }) });
}
function imgPara(file, wPx) {
  const full = path.join(SHOT, file);
  if (!fs.existsSync(full)) return P(`[스크린샷 자리: ${file}]`, { color: "C00000", bold: true });
  const buf = fs.readFileSync(full);
  const iw = buf.readUInt32BE(16), ih = buf.readUInt32BE(20);
  const hPx = Math.round(wPx * ih / iw);
  return new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 80, after: 40 },
    children: [new ImageRun({ type: "png", data: buf, transformation: { width: wPx, height: hPx },
      altText: { title: file, description: file, name: file } })] });
}
function caption(text) {
  return new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 160 },
    children: [new TextRun({ text, italics: true, size: 18, color: "595959" })] });
}
function placeholder(text) {
  return new Paragraph({ spacing: { before: 60, after: 160 }, shading: { fill: "FFF2CC", type: ShadingType.CLEAR },
    border: { top: { style: BorderStyle.SINGLE, size: 4, color: "E0B000" }, bottom: { style: BorderStyle.SINGLE, size: 4, color: "E0B000" },
      left: { style: BorderStyle.SINGLE, size: 4, color: "E0B000" }, right: { style: BorderStyle.SINGLE, size: 4, color: "E0B000" } },
    children: [new TextRun({ text: "📷 " + text, size: 18, color: "7F6000" })] });
}
function hcell(text, w) {
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: { fill: "1F3864", type: ShadingType.CLEAR }, verticalAlign: VerticalAlign.CENTER,
    children: [new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text, bold: true, color: "FFFFFF", size: 18 })] })] });
}
function dcell(text, w, fill) {
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: fill ? { fill, type: ShadingType.CLEAR } : undefined, verticalAlign: VerticalAlign.CENTER,
    children: [new Paragraph({ spacing: { after: 0, line: 252 }, children: runs(text, { size: 18 }) })] });
}
function tableBlock(b) {
  const rows = b.rows;
  const ncol = Math.max(...rows.map(r => r.length));
  const colW = Math.floor(CONTENT_W / ncol);
  const widths = Array(ncol).fill(colW); widths[ncol - 1] = CONTENT_W - colW * (ncol - 1);
  const firstHeader = rows[0].every(c => c.header);
  const trs = rows.map((r, ri) => new TableRow({ tableHeader: ri === 0 && firstHeader, children:
    Array.from({ length: ncol }, (_, ci) => {
      const c = r[ci] || { text: "" };
      if (ri === 0 && firstHeader) return hcell(c.text, widths[ci]);
      return dcell(c.text, widths[ci], ri % 2 ? "F4F7FB" : undefined);
    }) }));
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: widths, rows: trs });
}

// ---- screenshot injection anchors (normalized heading text) ----
const POST = {
  "3.1 첫 실행 화면": [
    () => imgPara("01_vms_main.png", 600), () => caption("그림. VMS 첫 실행 화면 (로그인 전) — 헤더 / KPI 스트립 / 카메라 표시 영역"),
  ],
  "3.2.2 Work Orders 버튼": [
    () => imgPara("20_dlg_workorders.png", 560),
    () => caption("그림. Work Orders 다이얼로그 — 상태 필터 / Refresh / 목록(Order No·Product·Recipe·Progress·Status·Planned Start) / Select·Cancel"),
  ],
  "3.3 사이드 패널 (Settings)": [
    () => imgPara("05_vms_sidepanel.png", 600),
    () => caption("그림. 사이드 패널(Settings) 펼친 상태 — Camera Control / Recipe / External Tools / Updates / Web Parameters / Image Saving"),
    () => P("[Image Save Settings] 버튼 — 검사 판정 이미지(양품/불량) 저장 설정 다이얼로그:"),
    () => imgPara("21_dlg_imagesave.png", 460),
    () => caption("그림. Image Save Settings — 저장(경로/보존) / 포맷·품질 / 파일명 규칙 / Web 연동"),
    () => P("[Sync Parameters] 버튼 — Web 서버와 레시피 파라미터 동기화 다이얼로그:"),
    () => imgPara("22_dlg_syncparams.png", 460),
    () => caption("그림. Sync Parameters — Web 파라미터 동기화"),
    () => P("외부 도구의 [Vision Tool Setup] 버튼으로 실행되는 비전 설정(VMS.VisionSetup) 화면은 다음과 같다.", {}),
    () => imgPara("03_visionsetup.png", 600),
    () => caption("그림. 비전 설정(VMS.VisionSetup) — 카메라 / Steps / Tool Palette(12 카테고리) / Tool Workspace / ROI 도구 / 이미지 뷰"),
    () => P("VisionSetup 상단 메뉴에서 열리는 주요 다이얼로그:"),
    () => imgPara("33_dlg_cameramgr.png", 520),
    () => caption("그림. Camera → Camera Manager — 카메라 등록/연결 관리"),
    () => imgPara("34_dlg_recipemgr.png", 540),
    () => caption("그림. Recipe → Recipe Manager — 레시피 목록/관리"),
    () => imgPara("35_dlg_sequence.png", 540),
    () => caption("그림. Sequence — 검사 시퀀스 편집"),
    () => imgPara("30_dlg_batchtest.png", 540),
    () => caption("그림. Batch Test… — 다수 이미지 일괄 검사/검증"),
    () => imgPara("31_dlg_synthdata.png", 540),
    () => caption("그림. OCR Synth Data… — OCR 합성 데이터 생성"),
    () => imgPara("32_dlg_inference.png", 460),
    () => caption("그림. Inference Settings… — 딥러닝 추론(ONNX) 설정"),
  ],
  "4. BODA.VMS.Web (관리자 / MES)": [
    () => P("아래는 BODA.VMS.Web(ASP.NET Core 8 + Blazor) 관리 화면이다(Admin 로그인 기준)."),
    () => imgPara("40_web_login.png", 360),
    () => caption("그림 4-0. Web 로그인"),
  ],
  "4.3 Dashboard": [
    () => imgPara("41_web_dashboard.png", 600),
    () => caption("그림. Dashboard — 현장 KPI(전체 클라이언트/금일 생산·합격/불량률) + 사이드 메뉴"),
  ],
  "4.5 Alarms": [() => imgPara("43_web_알람.png", 600), () => caption("그림. Alarms — 알람 목록")],
  "4.6 Production History": [() => imgPara("44_web_생산_이력.png", 600), () => caption("그림. Production History — 라인/기간/결과 필터, 검사 이력(이미지 포함), Excel 내보내기")],
  "4.7 Work Orders": [() => imgPara("45_web_작업_지시.png", 600), () => caption("그림. Work Orders — 작업지시 목록/관리")],
  "4.8 Products": [() => imgPara("46_web_제품.png", 600), () => caption("그림. Products — 제품 관리")],
  "4.9 Inspection Items (Recipes + Parameters)": [() => imgPara("58_web_검사_항목.png", 600), () => caption("그림. Inspection Items — 검사 항목(레시피/파라미터)")],
  "4.10 Operators": [() => imgPara("47_web_작업자.png", 600), () => caption("그림. Operators — 작업자 관리")],
  "4.12 Maintenance": [() => imgPara("56_web_예방_보전.png", 600), () => caption("그림. Maintenance — 예방 보전")],
  "4.13 Defect Codes": [() => imgPara("50_web_불량_코드.png", 600), () => caption("그림. Defect Codes — 불량 코드 관리")],
  "4.14 Shifts": [() => imgPara("59_web_교대_마스터.png", 600), () => caption("그림. Shifts — 교대 마스터")],
  "4.15 Audit Logs": [() => imgPara("61_web_감사_로그.png", 600), () => caption("그림. Audit Logs — 감사 로그")],
  "4.16 Reports / Quality Analysis": [
    () => imgPara("52_web_PDF_리포트.png", 600), () => caption("그림. PDF 리포트"),
    () => imgPara("48_web_파레토_분석.png", 560), () => caption("그림. 파레토 분석"),
    () => imgPara("49_web_SPC_관리도.png", 560), () => caption("그림. SPC 관리도"),
    () => imgPara("54_web_OEE_설비종합효율.png", 560), () => caption("그림. OEE 설비종합효율"),
    () => imgPara("55_web_설비_신뢰성_MTBF_MTTR.png", 560), () => caption("그림. 설비 신뢰성 (MTBF/MTTR)"),
    () => imgPara("57_web_불량률_예측.png", 560), () => caption("그림. 불량률 예측"),
  ],
};
// 챕터 3 말미(4장 직전)에 관리자 도구 다이얼로그 섹션 삽입
const ADMIN_BEFORE = "4. BODA.VMS.Web (관리자 / MES)";
function adminDialogsSection() {
  const out = [];
  out.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun("3.8 관리자 도구 다이얼로그 (Admin 전용)")] }));
  out.push(P("헤더의 User Management 아이콘과 Admin Tools(⋮) 드롭다운에서 실행되는 관리자 전용 다이얼로그다. (Admin 권한 로그인 시에만 표시)"));
  const dlgs = [
    ["23_dlg_usermgmt.png", "User Management — 사용자 계정·권한(UserGrade) 관리"],
    ["24_dlg_audit.png", "Audit Log Viewer — 감사 로그 조회(카테고리/기간 필터)"],
    ["25_dlg_health.png", "Health Check — 시스템 상태 점검"],
    ["26_dlg_backup.png", "Backup / Restore — 데이터 백업 및 복원"],
    ["27_dlg_autobackup.png", "Auto Backup Settings — 자동 백업 정책"],
    ["28_dlg_retention.png", "Retention Settings — 데이터 보존 정책(감사/백업/업로드 큐 + 카테고리별)"],
    ["29_dlg_support.png", "Support Package — 원격 지원용 진단 패키지 생성"],
  ];
  dlgs.forEach(([f, c]) => { out.push(imgPara(f, 540)); out.push(caption("그림. " + c)); });
  return out;
}
// 챕터 3 시작 전에 AppSetup 마법사 섹션을 끼워넣음 (= 설치 챕터 말미)
const WIZARD_BEFORE = "3. VMS 클라이언트 매뉴얼";
function wizardSection() {
  const out = [];
  out.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun("2.5 최초 실행 — 시스템 설정 마법사 (VMS.AppSetup)")] }));
  out.push(P("VMS 설치 후 최초 실행 시(또는 system_config.json 부재 시) 시스템 설정 마법사가 자동 실행된다. 총 6단계로 애플리케이션·네트워크·카메라·PLC·로봇/IO 및 초기 관리자 계정을 구성한 뒤 [Finish] 로 저장한다. 카메라가 없는 환경에서는 3단계에서 [Virtual Mode (Manual Setup)] 를 선택해 가상 구성으로 진행할 수 있다."));
  const steps = [
    ["10_appsetup_step1.png", "1단계 — 시작(Welcome)"],
    ["10_appsetup_step2.png", "2단계 — Application Settings: 애플리케이션명 / System IP / Web Server 연동(Client Index·Web Server URL·API Key) / SSO"],
    ["10_appsetup_step3.png", "3단계 — Camera Configuration: Live/Virtual 모드, [+ Add Camera], [Scan Network], 노출/게인/캡처모드"],
    ["10_appsetup_step4.png", "4단계 — PLC Communication: 벤더 / 통신 타입 / IP·Port / 폴링·하트비트 / Write·Endian"],
    ["10_appsetup_step5.png", "5단계 — Robot Configuration: 로봇 연동(Enable) / 벤더 / 연결 / 프로토콜"],
    ["10_appsetup_step6.png", "6단계 — Robot/IO Configuration: 등록된 IO 보드(벤더·모델·채널), [Finish] 로 저장"],
  ];
  steps.forEach(([f, c]) => { out.push(imgPara(f, 470)); out.push(caption("그림. " + c)); });
  return out;
}

function norm(s) { return s.replace(/\s+/g, " ").trim(); }

// ---- build body ----
const body = [];
// drop everything before H1 title
let start = blocks.findIndex(b => b.t === "h" && b.level === 1);
if (start < 0) start = 0;
for (let i = start + 1; i < blocks.length; i++) {
  const b = blocks[i];
  if (b.t === "h") {
    const txt = norm(b.text);
    if (txt === WIZARD_BEFORE) wizardSection().forEach(x => body.push(x));
    if (txt === ADMIN_BEFORE) adminDialogsSection().forEach(x => body.push(x));
    // html H2->docx H1, H3->H2, H4->H3
    const lvl = b.level === 2 ? HeadingLevel.HEADING_1 : b.level === 3 ? HeadingLevel.HEADING_2 : HeadingLevel.HEADING_3;
    body.push(new Paragraph({ heading: lvl, children: [new TextRun(b.text)] }));
    if (POST[txt]) POST[txt].forEach(fn => body.push(fn()));
  } else if (b.t === "p") {
    body.push(P(b.text));
  } else if (b.t === "li") {
    body.push(bullet(b.text));
  } else if (b.t === "table") {
    body.push(tableBlock(b));
    body.push(new Paragraph({ spacing: { after: 120 }, children: [] }));
  }
}

// ---- #4: 비전 도구 파라미터 (핵심 툴) — 코드(ToolSettings XAML)에서 전수 추출 ----
const DESC = {
  // Threshold
  ThresholdValue: "이진화 임계값. 픽셀값이 이 값보다 크면 전경(흰색)으로 처리.",
  MaxValue: "이진화 시 전경에 적용할 최대값(보통 255).",
  UseOtsu: "Otsu 자동 임계값 사용(체크 시 Threshold Value 무시).",
  UseAdaptive: "적응형 이진화 사용(국소 영역별 임계값 — 조명 불균일에 강함).",
  BlockSize: "적응형 이진화 국소 블록 크기(홀수).",
  CValue: "적응형 이진화 보정 상수(클수록 전경 감소).",
  // Blur
  BlurType: "블러 종류(Gaussian / Median / Box 등).",
  KernelSize: "커널 크기(홀수). 클수록 강한 평활화.",
  SigmaX: "가우시안 X 표준편차(0이면 커널에서 자동 산출).",
  SigmaY: "가우시안 Y 표준편차(0이면 SigmaX 사용).",
  // Morphology
  Operation: "연산 종류 선택.",
  KernelWidth: "구조요소(커널) 너비.",
  KernelHeight: "구조요소(커널) 높이.",
  Iterations: "연산 반복 횟수.",
  // Edge
  Method: "에지 검출 방법(Canny / Sobel / Laplacian 등).",
  CannyThreshold1: "Canny 하위(연결) 임계값.",
  CannyThreshold2: "Canny 상위(강한 에지) 임계값.",
  CannyApertureSize: "Sobel 연산 커널 크기(홀수).",
  L2Gradient: "정확한 L2 norm 그래디언트 사용(정밀도↑, 속도↓).",
  ClipLimit: "CLAHE 대비 제한값(클수록 대비 강조).",
  TileGridWidth: "CLAHE 타일 격자 가로 분할 수.",
  TileGridHeight: "CLAHE 타일 격자 세로 분할 수.",
  // Blob
  UseInternalThreshold: "툴 내부에서 이진화를 수행(외부 전처리 불필요).",
  SegmentationPolarity: "전경 극성(밝은 블롭 / 어두운 블롭).",
  MinArea: "검출 최소 면적(px) — 이보다 작은 블롭 제거.",
  MaxArea: "검출 최대 면적(px) — 이보다 큰 블롭 제거.",
  EnableJudgment: "이 도구의 OK/NG 판정 사용.",
  UseAreaJudgment: "면적 기준 판정 사용.",
  ExpectedArea: "기준(목표) 면적.",
  AreaTolerancePlus: "면적 상한 허용오차(+).",
  AreaToleranceMinus: "면적 하한 허용오차(−).",
  UseCountJudgment: "블롭 개수 기준 판정 사용.",
  CountMode: "개수 판정 모드(정확히/이상/범위 등).",
  ExpectedCount: "기준 개수.",
  ExpectedCountMax: "개수 상한(범위 모드).",
  DrawContours: "결과 윤곽선 오버레이 표시.",
  DrawBoundingBox: "외접 사각형 표시.",
  DrawCenterPoint: "블롭 중심점 표시.",
  DrawLabels: "블롭 번호/라벨 표시.",
  // Caliper / Fit 공통
  SearchWidth: "탐색(투영) 폭.",
  SearchAxis: "탐색 축 방향.",
  Polarity: "에지 극성(밝→어두 / 어두→밝 / 모두).",
  EdgeThreshold: "에지로 인정할 최소 강도.",
  FilterHalfWidth: "에지 검출 필터 반폭(노이즈 평활).",
  Mode: "동작 모드.",
  MaxEdges: "검출할 최대 에지 수.",
  ScorerMode: "에지 점수 산정 방식.",
  SelectionMode: "사용할 에지 선택 방식(첫/최강/마지막 등).",
  ProjectionMode: "프로파일 투영 방식.",
  UseGaussianFilter: "가우시안 필터 적용.",
  GaussianSigma: "가우시안 시그마.",
  UseNormalizedContrast: "대비 정규화 사용.",
  SubPixelMethod: "서브픽셀 보간 방식(정밀 위치).",
  NumCalipers: "사용할 캘리퍼 개수(많을수록 정밀/느림).",
  SearchLength: "각 캘리퍼 탐색 길이.",
  FitMethod: "피팅 방법(최소제곱 / RANSAC 등).",
  RansacThreshold: "RANSAC 인라이어 허용오차.",
  MinFoundCalipers: "유효로 인정할 최소 캘리퍼 수.",
  ExpectedRadius: "예상 반지름.",
  StartAngle: "탐색 시작 각도.",
  EndAngle: "탐색 종료 각도.",
  SearchDirection: "에지 탐색 방향(내→외 / 외→내).",
  "CenterPoint.X": "예상 중심 X 좌표.",
  "CenterPoint.Y": "예상 중심 Y 좌표.",
  // Shape / Feature Match
  AngleStep: "각도 탐색 간격(작을수록 정밀/느림).",
  MinScale: "최소 스케일(축소 한계).",
  MaxScale: "최대 스케일(확대 한계).",
  ScaleStep: "스케일 탐색 간격.",
  ScoreThreshold: "매칭 점수 임계값(이상이면 검출).",
  MaxInstances: "최대 검출 개수.",
  NmsDistanceFactor: "중복 검출 억제(NMS) 거리 계수.",
  NumPyramidLevels: "이미지 피라미드 레벨 수(속도↑).",
  TopCandidates: "정밀화할 상위 후보 수.",
  UseSearchRegion: "탐색 영역 제한 사용.",
  SearchRegionX: "탐색 영역 X.", SearchRegionY: "탐색 영역 Y.",
  SearchRegionWidth: "탐색 영역 너비.", SearchRegionHeight: "탐색 영역 높이.",
  UseContrastInvariant: "명암 반전에도 매칭(대비 불변).",
  IsAutoTuneEnabled: "파라미터 자동 튜닝 적용.",
  AngleStart: "각도 탐색 시작.",
  AngleExtent: "각도 탐색 범위.",
  Greediness: "탐욕도(높을수록 빠르나 정확도↓).",
  NumLevels: "피라미드 레벨 수.",
  CannyLow: "모델 에지 추출 하위 임계값.",
  CannyHigh: "모델 에지 추출 상위 임계값.",
  MaxModelPoints: "모델 특징점 최대 수.",
  CurvatureWeight: "곡률 가중치.",
  // Code Reader
  CodeReaderMode: "판독 모드(1D 바코드 / 2D / 자동).",
  MaxCodeCount: "한 화면에서 판독할 최대 코드 수.",
  TryHarder: "정밀(저품질 코드) 탐색 강화.",
  UseLocalization: "DataMatrix 후보 지역화 사용.",
  EnableVerification: "판독 결과 검증(기대값 비교) 사용.",
  ExpectedText: "기대 문자열(검증/비교용).",
  UseRegexMatch: "정규식으로 결과 검증.",
  ParseGs1: "GS1 Application Identifier 파싱.",
  EnableQualityGrading: "코드 인쇄 품질 등급화.",
  MinPassGrade: "합격으로 인정할 최소 품질 등급.",
  DrawOverlay: "결과 오버레이 표시.",
  // OCR
  OcrEngine: "OCR 엔진(Tesseract / PaddleOCR 등).",
  Language: "인식 언어.",
  PageSegMode: "페이지 분할 모드(단어/줄/블록 등).",
  EngineMode: "엔진 모드(legacy / LSTM 등).",
  CharacterWhitelist: "허용할 문자 집합(오인식 감소).",
  MaxSideLen: "입력 이미지 최대 변 길이(리사이즈).",
  CustomDetModelPath: "커스텀 검출 모델 경로.",
  CustomRecModelPath: "커스텀 인식 모델 경로.",
  CustomDictPath: "커스텀 문자 사전 경로.",
  ConfidenceThreshold: "인식 신뢰도 임계값.",
  AutoPreprocess: "자동 전처리(이진화/대비 보정 등).",
  InvertImage: "이미지 명암 반전 후 인식.",
  TargetTextHeight: "목표 글자 높이(0=자동).",
  DenoiseLevel: "노이즈 제거 강도.",
  DotMatrixMode: "도트매트릭스(각인) 문자 모드.",
  TessdataPath: "Tesseract tessdata 경로.",
};
{
  body.push(new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun("9. 비전 도구 파라미터 (핵심 툴)")] }));
  body.push(P("VisionSetup의 Tool Palette에서 도구를 Tool Workspace로 드래그한 뒤 선택하면, 우측 파라미터 패널에 아래 항목이 표시된다. 파라미터 라벨은 실제 UI 표기와 동일하며, 본 장은 Tool Palette의 전체 도구를 카테고리 순으로 다룬다. (딥러닝/3D 도구는 모델·프리셋 선택 기반이라 수치 파라미터가 적을 수 있다. 파라미터 패널 스크린샷은 후속 캡처 예정.)"));
  // 9.1 팔레트 카테고리 개요
  body.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun("9.1 툴 팔레트 카테고리 개요")] }));
  body.push(P("Tool Palette는 카테고리로 묶여 있으며, 각 카테고리의 주요 도구는 다음과 같다. (상세 파라미터는 9.2 이하 참조)"));
  const CATDESC = {
    "Conversion": "그레이 변환·이진화 — 후속 처리를 위한 기본 변환",
    "Preprocessing": "블러·모폴로지·에지·히스토그램·보정 등 영상 전처리",
    "Color": "색 추출·색 매칭 — 컬러 기반 검사",
    "Blob": "블롭(영역) 검출·계수·면적/형상 판정",
    "Measurement": "캘리퍼·직선/원 피팅·기하 — 치수 측정",
    "Pattern Matching": "형상/특징 기반 패턴 매칭·정렬",
    "Identification": "OCR/OCV — 문자 인식·검증",
    "Code Reading": "바코드/QR/DataMatrix 판독",
    "3D Analysis": "포인트클라우드·평면/높이·3D 기하",
    "Deep Learning": "검출·분할·이상·분류 (ONNX 추론)",
    "Judgment": "종합 판정·앙상블·결과 출력",
  };
  const catOrder = ["Conversion", "Preprocessing", "Color", "Blob", "Measurement", "Pattern Matching", "Identification", "Code Reading", "3D Analysis", "Deep Learning", "Judgment"];
  const groups = {}; for (const t of tools) { (groups[t.category] = groups[t.category] || []).push(t.tool); }
  {
    const c1 = 1700, c2 = 3700, c3 = CONTENT_W - 1700 - 3700;
    const rows = [new TableRow({ tableHeader: true, children: [hcell("카테고리", c1), hcell("포함 도구", c2), hcell("용도", c3)] })];
    let gi = 0;
    for (const cat of catOrder) {
      if (!groups[cat]) continue;
      const fill = gi % 2 ? "F4F7FB" : undefined; gi++;
      rows.push(new TableRow({ children: [dcell(cat, c1, fill), dcell(groups[cat].join(", "), c2, fill), dcell(CATDESC[cat] || "", c3, fill)] }));
    }
    body.push(new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [1700, 3700, CONTENT_W - 1700 - 3700], rows }));
  }
  body.push(P("※ Calibration 카테고리(카메라/핸드아이 보정)는 별도 캘리브레이션 도구로 제공된다.", { size: 18, color: "595959" }));
  body.push(new Paragraph({ spacing: { after: 120 }, children: [] }));
  let n = 1;
  for (const t of tools) {
    n++;
    body.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(`9.${n} ${t.tool}`)] }));
    body.push(P(`카테고리: ${t.category}`, { size: 18, color: "595959" }));
    body.push(placeholder(`${t.tool} 파라미터 패널 스크린샷 — Tool Palette에서 드래그 → 선택 시 우측 패널`));
    if (!t.params || !t.params.length) {
      body.push(P("이 도구는 별도 수치 파라미터 없이 ROI/입력 연결만으로 동작한다."));
      continue;
    }
    const c1 = 2500, c2 = 2100, c3 = CONTENT_W - c1 - c2;
    const rows = [new TableRow({ tableHeader: true, children: [hcell("파라미터 (UI 라벨)", c1), hcell("컨트롤 / 범위", c2), hcell("설명", c3)] })];
    t.params.forEach((pp, i) => {
      const ctrl = pp.enum ? `${pp.ctrl} (${pp.enum})` : ((pp.min || pp.max) ? `${pp.ctrl} [${pp.min}~${pp.max}]` : pp.ctrl);
      const desc = DESC[pp.param] || (pp.label + ".");
      const fill = i % 2 ? "F4F7FB" : undefined;
      rows.push(new TableRow({ children: [dcell(pp.label, c1, fill), dcell(ctrl, c2, fill), dcell(desc, c3, fill)] }));
    });
    body.push(new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: [c1, c2, c3], rows }));
    body.push(new Paragraph({ spacing: { after: 120 }, children: [] }));
  }
}

// ---- front matter ----
const front = [];
front.push(new Paragraph({ spacing: { before: 2600, after: 0 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "사용자 취급 설명서", bold: true, size: 56, color: "1F3864" })] }));
front.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 100 },
  children: [new TextRun({ text: "(User Manual)", size: 26, color: "595959" })] }));
front.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 240, after: 60 },
  children: [new TextRun({ text: "BODA Vision Management System (VMS)", bold: true, size: 32 })] }));
front.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 520 },
  children: [new TextRun({ text: "설치 · 환경 설정 · UI 화면별 조작법 (스크린샷 포함)", size: 22, color: "595959" })] }));
front.push(imgPara("05_vms_sidepanel.png", 460));
front.push(new Paragraph({ spacing: { before: 520 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "GS 인증 제출용 · 버전 1.0 · 2026-06-12", size: 22 })] }));
front.push(new Paragraph({ children: [new PageBreak()] }));
front.push(new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun("목차")] }));
front.push(new TableOfContents("목차", { hyperlink: true, headingStyleRange: "1-3" }));
front.push(P("※ 본 매뉴얼은 docs/manuals/BODA-VMS-User-Manual.html 본문을 기준으로 작성되었으며, 화면 스크린샷을 추가하였다. [📷 …] 표식은 추가 캡처가 필요한 화면이다.", { size: 18, color: "595959", italics: true }));
front.push(new Paragraph({ children: [new PageBreak()] }));

const children = [...front, ...body];

const doc = new Document({
  creator: "BODA VMS", title: "VMS 사용자 취급 설명서",
  styles: {
    default: { document: { run: { font: "Malgun Gothic", size: 21 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 30, bold: true, color: "1F3864", font: "Malgun Gothic" },
        paragraph: { spacing: { before: 300, after: 160 }, outlineLevel: 0, keepNext: true } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 25, bold: true, color: "2E5496", font: "Malgun Gothic" },
        paragraph: { spacing: { before: 220, after: 110 }, outlineLevel: 1, keepNext: true } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 22, bold: true, color: "404040", font: "Malgun Gothic" },
        paragraph: { spacing: { before: 160, after: 80 }, outlineLevel: 2, keepNext: true } },
    ],
  },
  numbering: { config: [
    { reference: "b", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
      style: { paragraph: { indent: { left: 500, hanging: 250 } } } }] },
  ] },
  sections: [{
    properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      children: [new TextRun({ text: "BODA VMS 사용자 취급 설명서 v1.0", size: 16, color: "808080" })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080" }),
        new TextRun({ text: " / ", size: 16, color: "808080" }),
        new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: "808080" })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then((buf) => { fs.writeFileSync(OUT, buf); console.log("WROTE " + OUT + " (" + buf.length + " bytes)"); });
