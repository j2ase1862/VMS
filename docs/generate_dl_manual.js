// VMS 딥러닝 도구 기술 문서 생성 스크립트
// Usage: node generate_dl_manual.js

const path = require('path');
process.env.NODE_PATH = 'C:\\Users\\vinos\\AppData\\Roaming\\npm\\node_modules';
require('module').Module._initPaths();

const fs = require('fs');
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  Header, Footer, AlignmentType, PageOrientation, LevelFormat,
  TabStopType, TabStopPosition, HeadingLevel, BorderStyle, WidthType,
  ShadingType, VerticalAlign, PageNumber, PageBreak, TableOfContents
} = require('docx');

// ── 공통 스타일 / 색상 ──
const COLOR_ACCENT = "2E75B6";
const COLOR_HEADER_BG = "D5E8F0";
const COLOR_CODE_BG = "F4F4F4";
const COLOR_MUTED = "666666";

const BORDER_LIGHT = { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" };
const BORDERS_LIGHT = { top: BORDER_LIGHT, bottom: BORDER_LIGHT, left: BORDER_LIGHT, right: BORDER_LIGHT };

// 페이지: US Letter, 1인치 여백 → 내용 폭 9360 DXA
const CONTENT_WIDTH = 9360;

// ── 헬퍼 함수 ──
function p(text, opts = {}) {
  return new Paragraph({
    spacing: { after: opts.afterSpacing ?? 120 },
    alignment: opts.align,
    children: [new TextRun({ text, bold: opts.bold, italics: opts.italics,
      color: opts.color, size: opts.size, font: opts.font })]
  });
}

function h1(text) {
  return new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(text)] });
}
function h2(text) {
  return new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(text)] });
}
function h3(text) {
  return new Paragraph({ heading: HeadingLevel.HEADING_3, children: [new TextRun(text)] });
}

function bullet(text, level = 0) {
  return new Paragraph({
    numbering: { reference: "bullets", level },
    children: parseInline(text)
  });
}

function numbered(text) {
  return new Paragraph({
    numbering: { reference: "numbers", level: 0 },
    children: parseInline(text)
  });
}

// 간단한 인라인 마크업: **bold**, `code`
function parseInline(text) {
  const runs = [];
  const re = /(\*\*[^*]+\*\*|`[^`]+`)/g;
  let lastIndex = 0;
  let match;
  while ((match = re.exec(text)) !== null) {
    if (match.index > lastIndex) {
      runs.push(new TextRun(text.slice(lastIndex, match.index)));
    }
    const token = match[0];
    if (token.startsWith("**")) {
      runs.push(new TextRun({ text: token.slice(2, -2), bold: true }));
    } else if (token.startsWith("`")) {
      runs.push(new TextRun({ text: token.slice(1, -1), font: "Consolas", shading: { fill: COLOR_CODE_BG, type: ShadingType.CLEAR } }));
    }
    lastIndex = match.index + token.length;
  }
  if (lastIndex < text.length) {
    runs.push(new TextRun(text.slice(lastIndex)));
  }
  return runs;
}

function codeBlock(lines) {
  const arr = Array.isArray(lines) ? lines : lines.split('\n');
  return arr.map(line =>
    new Paragraph({
      spacing: { after: 0 },
      shading: { fill: COLOR_CODE_BG, type: ShadingType.CLEAR },
      children: [new TextRun({ text: line || " ", font: "Consolas", size: 20 })]
    })
  );
}

function cell(content, opts = {}) {
  const children = typeof content === "string"
    ? [new Paragraph({ children: parseInline(content), alignment: opts.align })]
    : [content];
  return new TableCell({
    borders: BORDERS_LIGHT,
    width: { size: opts.width, type: WidthType.DXA },
    shading: opts.shading ? { fill: opts.shading, type: ShadingType.CLEAR } : undefined,
    margins: { top: 80, bottom: 80, left: 120, right: 120 },
    verticalAlign: VerticalAlign.CENTER,
    children
  });
}

function headerCell(text, width) {
  return new TableCell({
    borders: BORDERS_LIGHT,
    width: { size: width, type: WidthType.DXA },
    shading: { fill: COLOR_HEADER_BG, type: ShadingType.CLEAR },
    margins: { top: 80, bottom: 80, left: 120, right: 120 },
    verticalAlign: VerticalAlign.CENTER,
    children: [new Paragraph({ children: [new TextRun({ text, bold: true })] })]
  });
}

function table(headers, rows, colWidths) {
  // colWidths: DXA 배열. 합 = CONTENT_WIDTH
  if (!colWidths) {
    const each = Math.floor(CONTENT_WIDTH / headers.length);
    colWidths = new Array(headers.length).fill(each);
    colWidths[colWidths.length - 1] = CONTENT_WIDTH - each * (headers.length - 1);
  }
  const totalWidth = colWidths.reduce((a, b) => a + b, 0);

  const rs = [];
  rs.push(new TableRow({
    tableHeader: true,
    children: headers.map((h, i) => headerCell(h, colWidths[i]))
  }));
  rows.forEach(r => {
    rs.push(new TableRow({
      children: r.map((c, i) => cell(c, { width: colWidths[i] }))
    }));
  });

  return new Table({
    width: { size: totalWidth, type: WidthType.DXA },
    columnWidths: colWidths,
    rows: rs
  });
}

function note(label, text) {
  return new Paragraph({
    spacing: { before: 120, after: 120 },
    shading: { fill: "FFF9E6", type: ShadingType.CLEAR },
    border: {
      left: { style: BorderStyle.SINGLE, size: 24, color: "F2B33A", space: 8 }
    },
    children: [
      new TextRun({ text: `${label} `, bold: true, color: "B07A0A" }),
      ...parseInline(text)
    ]
  });
}

function spacer() {
  return new Paragraph({ spacing: { after: 0 }, children: [new TextRun(" ")] });
}

// ──────────────────────────────────────────────────────
// 본문 조립
// ──────────────────────────────────────────────────────

const children = [];

// ── 표지 ──
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { before: 2400, after: 200 },
  children: [new TextRun({ text: "VMS 딥러닝 도구 기술 문서", bold: true, size: 56, color: COLOR_ACCENT })]
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { after: 200 },
  children: [new TextRun({ text: "Deep Learning Tools Technical Guide", size: 32, color: COLOR_MUTED })]
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { after: 200 },
  children: [new TextRun({ text: "초보자를 위한 통합 가이드", size: 26, italics: true, color: COLOR_MUTED })]
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { before: 1200, after: 120 },
  children: [new TextRun({ text: "Vision Management System (VMS)", size: 24 })]
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "Release 1.0 — 2026-04", size: 20, color: COLOR_MUTED })]
}));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ── 목차 ──
children.push(h1("목차"));
children.push(new TableOfContents("Table of Contents", {
  hyperlink: true,
  headingStyleRange: "1-2"
}));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 1. 문서 소개
// ══════════════════════════════════════════════════════
children.push(h1("1. 문서 소개"));

children.push(p("본 문서는 VMS(Vision Management System)에 포함된 딥러닝 비전 툴의 역할, 사용 방법, 학습 절차, 파라미터 의미를 처음 접하는 사용자가 이해할 수 있도록 정리한 기술 문서입니다."));
children.push(p("VMS는 공장 자동화 환경의 머신 비전 검사(객체 검출, 분류, 이상 탐지 등)를 위한 WPF 기반 플랫폼으로, OpenCvSharp와 ONNX Runtime을 기반으로 합니다. Cognex VisionPro의 ViDi 계열 도구에 대응하는 딥러닝 기능을 제공합니다."));

children.push(h2("1.1 문서 대상"));
children.push(bullet("딥러닝 배경 지식이 없거나 적은 현장 엔지니어"));
children.push(bullet("VMS를 처음 도입해 툴 선택에 고민이 있는 사용자"));
children.push(bullet("라벨링·학습·추론의 전체 플로우를 확인하고자 하는 운영자"));

children.push(h2("1.2 본 문서에서 다루는 네 가지 핵심 툴"));
children.push(table(
  ["툴 이름", "역할", "대응 Cognex ViDi"],
  [
    ["DetectionTool", "객체의 위치와 종류를 검출 (바운딩 박스)", "Blue Locate"],
    ["ClassifyTool", "이미지 전체를 클래스로 분류", "Green Classify"],
    ["AnomalyTool", "정상과 다른 부분 탐지 (불량 검출)", "Red Analyze"],
    ["EnsembleTool", "위 툴들을 조합해 최종 판정", "—"]
  ],
  [2400, 4600, 2360]
));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 2. 어떤 툴을 선택해야 하나 — 결정 가이드
// ══════════════════════════════════════════════════════
children.push(h1("2. 어떤 툴을 선택해야 하나 — 결정 가이드"));
children.push(p("검사 과제를 다음 질문에 따라 분류하면 적합한 툴이 결정됩니다."));

children.push(h2("2.1 결정 흐름"));
children.push(numbered("**객체의 위치가 중요한가?** — 예: 부품이 어디에 있는지 좌표가 필요한 경우 → **DetectionTool**"));
children.push(numbered("**이미지 전체가 어떤 종류인지만 궁금한가?** — 예: 양품/불량 여부만 판정 → **ClassifyTool**"));
children.push(numbered("**정상 이미지만 많고, 어떤 결함이 나올지 예측 불가한가?** — 예: 새로운 불량 유형이 계속 나타나는 환경 → **AnomalyTool**"));
children.push(numbered("**여러 툴을 조합해서 최종 판정하고 싶은가?** → **EnsembleTool** (Detection + Anomaly 결합 등)"));

children.push(h2("2.2 사용 사례별 권장 툴"));
children.push(table(
  ["사용 사례", "권장 툴", "이유"],
  [
    ["부품 유무 확인 + 개수 세기", "DetectionTool", "위치와 개수 모두 필요"],
    ["OK/NG 이진 판정", "ClassifyTool", "위치 불필요, 전체 이미지 기반 판정"],
    ["다품종 양/불량", "ClassifyTool", "여러 클래스로 분류"],
    ["새로운 결함 유형 계속 등장", "AnomalyTool", "정상만 학습, 이상은 자동 탐지"],
    ["스크래치/얼룩 같은 미세 결함", "AnomalyTool", "정상 분포 벗어난 픽셀 감지"],
    ["부품 검출 + 결함 확인 동시", "EnsembleTool", "Detection 결과를 ROI로 Anomaly 연결"]
  ],
  [3200, 2000, 4160]
));

children.push(note("팁:", "처음에는 **ClassifyTool**이 가장 진입 장벽이 낮습니다. 라벨링이 폴더 구조만으로 끝나고 학습·추론도 단순합니다. 위치·개수가 중요해지면 DetectionTool로, 결함 유형이 다양해지면 AnomalyTool로 확장하는 순서를 권장합니다."));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 3. DetectionTool
// ══════════════════════════════════════════════════════
children.push(h1("3. DetectionTool — 객체 검출 (YOLO)"));
children.push(p("이미지 안에서 미리 학습한 객체가 **어디에** **몇 개** 있는지를 바운딩 박스로 찾아 줍니다. YOLOv8 계열 ONNX 모델을 사용합니다."));

children.push(h2("3.1 언제 쓰는가"));
children.push(bullet("부품이 트레이 안에 제대로 놓여 있는지 위치·개수 검사"));
children.push(bullet("여러 종류의 객체가 한 이미지에 혼재해 있을 때 (각각 다른 클래스로 구별)"));
children.push(bullet("작은 특징점(dot, 마커)의 위치를 정확히 잡아야 하는 케이스"));

children.push(h2("3.2 파라미터 상세"));
children.push(p("Tool Settings 패널에 표시되는 항목을 그룹별로 설명합니다.", { color: COLOR_MUTED }));

children.push(h3("Model"));
children.push(table(
  ["파라미터", "기본값", "설명", "조정 가이드"],
  [
    ["Model Path (.onnx)", "(필수)", "학습 결과로 얻은 ONNX 파일 경로", "`.pt`가 아닌 `.onnx`만 지원. 학습 시 `--export_onnx` 필수"],
    ["Inference Size", "640", "모델 입력 해상도 (정사각형 px)", "학습 시 사용한 `imgsz`와 반드시 일치. 보통 640"]
  ],
  [2400, 1400, 3100, 2460]
));

children.push(h3("Detection"));
children.push(table(
  ["파라미터", "기본값", "설명", "조정 가이드"],
  [
    ["Confidence Threshold", "0.25", "이 값 이상의 신뢰도를 가진 박스만 결과에 포함 (0~1)", "미검이 많으면 0.1로 낮추기, 오검이 많으면 0.5로 높이기"],
    ["IoU Threshold", "0.45", "겹치는 박스 중복 제거 기준 (NMS)", "같은 객체에 박스가 여러 개 나오면 0.3으로 낮추기"],
    ["Use Per-Class Thresholds", "off", "클래스별로 다른 Confidence 기준 적용", "클래스 간 크기/난이도 차이가 클 때만 on"],
    ["Per-Class Confidence", "0.25", "각 클래스의 개별 임계값", "on일 때 이 값이 전역 값을 **덮어씀** — 주의"],
    ["Class Names", "(자동)", "ONNX 메타데이터에서 자동 로드된 클래스 이름 목록", "모델이 출력하는 순서와 일치해야 라벨이 맞음"]
  ],
  [2200, 1000, 3400, 2760]
));

children.push(note("주의:", "`Use Per-Class Thresholds`가 켜져 있으면 전역 Confidence Threshold 값이 무시되고 Per-Class 값이 사용됩니다. 검출이 0개로 나올 때 가장 흔한 원인 중 하나입니다."));

children.push(h3("Preprocessing (CLAHE)"));
children.push(p("CLAHE(Contrast Limited Adaptive Histogram Equalization)는 이미지의 국소 대비를 강화하는 전처리입니다. 공장 조명이 불균일한 환경에서 도움이 됩니다."));
children.push(table(
  ["파라미터", "기본값", "설명", "조정 가이드"],
  [
    ["Use CLAHE", "off", "CLAHE 전처리 활성화", "학습 시 같은 전처리를 적용했을 때만 on (아니면 오히려 정확도 하락)"],
    ["Clip Limit", "2.0", "대비 강화 정도", "값↑ = 대비 강화 + 노이즈 증폭. 2.0이 일반적"],
    ["Tile Grid Size", "8", "타일 크기 (N×N)", "작은 결함은 4, 큰 영역은 16"]
  ],
  [2200, 1000, 3400, 2760]
));

children.push(h3("SAHI (Tiled Inference)"));
children.push(p("SAHI(Slicing Aided Hyper Inference)는 고해상도 이미지를 작은 타일로 나눠 각각 추론한 뒤 결과를 합칩니다. 원본을 640으로 줄이면 사라지는 작은 객체를 검출할 때 사용합니다."));
children.push(table(
  ["파라미터", "기본값", "설명", "조정 가이드"],
  [
    ["Use SAHI", "off", "타일 추론 활성화", "이미지가 640보다 크고 작은 객체를 찾아야 할 때만 on"],
    ["Tile Size", "640", "타일 한 변의 크기 (px)", "보통 Inference Size와 동일"],
    ["Overlap Ratio", "0.2", "타일 간 겹침 비율 (0.0~0.5)", "타일 경계의 객체를 놓치지 않도록 0.2 권장"]
  ],
  [2200, 1000, 3400, 2760]
));

children.push(h2("3.3 출력 결과"));
children.push(p("RunTool 이후 VisionResult.Data 딕셔너리에 저장되는 키:"));
children.push(table(
  ["키", "타입", "의미"],
  [
    ["DetectionCount", "int", "검출된 객체 수"],
    ["Detections", "List<DetectionResult>", "검출 객체 배열 (ClassId, Confidence, X, Y, Width, Height)"],
    ["ExecutionProvider", "string", "실제 사용된 추론 엔진 (CPU/CUDA/TensorRT 등)"],
    ["Det{i}_Class / _Confidence / _X / _Y / _Width / _Height", "int/float", "i번째 검출 결과의 개별 필드 (PLC 매핑용)"]
  ],
  [3200, 2000, 4160]
));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 4. ClassifyTool
// ══════════════════════════════════════════════════════
children.push(h1("4. ClassifyTool — 이미지 분류"));
children.push(p("이미지 **전체**가 어떤 클래스에 속하는지 하나의 라벨로 판정합니다. 위치 정보는 제공하지 않습니다. ResNet, MobileNet 등 CNN 기반 모델을 ONNX로 사용합니다."));

children.push(h2("4.1 언제 쓰는가"));
children.push(bullet("OK/NG 이진 판정"));
children.push(bullet("제품 등급(A/B/C)이나 색상(RED/GREEN/BLUE) 분류"));
children.push(bullet("위치가 고정돼 있어 ROI 안의 이미지 전체가 하나의 상태를 대표할 때"));

children.push(h2("4.2 파라미터 상세"));
children.push(table(
  ["파라미터", "기본값", "설명", "조정 가이드"],
  [
    ["Model Path (.onnx)", "(필수)", "분류 모델 ONNX 경로", "—"],
    ["Input Width / Height", "224 / 224", "모델 입력 해상도", "학습 시 imgsz와 일치. ResNet 계열 기본 224"],
    ["Confidence Threshold", "0.5", "이 값 이상일 때만 Success=true", "엄격한 판정은 0.7~0.9"],
    ["Class Names", "(자동)", "ONNX 메타데이터 기반 클래스명", "자동 로드되지 않으면 쉼표 구분으로 수기 입력"],
    ["Use ImageNet Normalization", "on", "ImageNet 평균/표준편차로 정규화", "학습 시 사용한 정규화 방식과 일치"]
  ],
  [2200, 1400, 3200, 2560]
));

children.push(h2("4.3 출력 결과"));
children.push(table(
  ["키", "타입", "의미"],
  [
    ["ClassName", "string", "예측된 클래스 이름"],
    ["ClassId", "int", "예측된 클래스 인덱스"],
    ["Confidence", "float", "예측 신뢰도 (Top-1 확률)"],
    ["TopN", "List<(ClassId, Score)>", "상위 5개 후보 클래스"]
  ],
  [2800, 2600, 3960]
));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 5. AnomalyTool
// ══════════════════════════════════════════════════════
children.push(h1("5. AnomalyTool — 이상 탐지"));
children.push(p("**정상 이미지만으로** 학습해 정상 분포에서 벗어난 부분을 감지합니다. 학습 시 불량 샘플이 없어도 되는 점이 Detection/Classification과 가장 다른 부분입니다. PatchCore, FastFlow, EfficientAD 등의 알고리즘을 지원합니다."));

children.push(h2("5.1 언제 쓰는가"));
children.push(bullet("결함 유형이 다양하고 앞으로 새로운 형태의 불량이 나타날 가능성이 큰 경우"));
children.push(bullet("불량 샘플 수집이 어렵고 정상 이미지는 풍부한 경우"));
children.push(bullet("스크래치, 얼룩, 오염 같은 **픽셀 수준** 미세 결함"));

children.push(h2("5.2 파라미터 상세"));
children.push(table(
  ["파라미터", "기본값", "설명", "조정 가이드"],
  [
    ["Model Path (.onnx)", "(필수)", "anomalib 등으로 학습된 ONNX", "—"],
    ["Input Size", "224", "입력 해상도 (정사각형)", "미세 결함은 256~384로 높이기"],
    ["Anomaly Threshold", "0.5", "이 값 이상이면 이상(NG) 판정", "자동 캘리브레이션으로 설정 권장"],
    ["Show Heatmap", "on", "이상 위치 히트맵 오버레이", "디버깅 시 on, 운영 시 off 가능"],
    ["Heatmap Opacity", "0.4", "히트맵 투명도 (0.0~1.0)", "원본이 잘 보이는 0.3~0.5 권장"],
    ["Calibration Folder", "(선택)", "자동 임계값 계산용 정상 이미지 폴더", "20~100장 정도 권장"],
    ["Calibration Sigma", "3.0", "threshold = mean + σ·std의 σ", "보수적 3.0(오검 0.27%), 민감 2.0"]
  ],
  [2400, 1200, 3200, 2560]
));

children.push(h2("5.3 자동 임계값 캘리브레이션"));
children.push(p("정상 이미지 폴더를 지정한 뒤 Tool Settings의 캘리브레이션 버튼을 누르면, 해당 이미지들을 모두 추론해 이상 점수 분포를 분석하고 threshold를 자동으로 설정합니다."));
children.push(p("공식: threshold = mean + sigma × std", { color: COLOR_MUTED }));
children.push(p("예: 정상 이미지 100장의 mean=0.15, std=0.04, sigma=3.0 → threshold=0.27로 자동 설정"));

children.push(h2("5.4 출력 결과"));
children.push(table(
  ["키", "타입", "의미"],
  [
    ["AnomalyScore", "float", "이상 점수 (0~1, 높을수록 이상)"],
    ["IsNormal", "bool", "정상(true) / 이상(false)"],
    ["Threshold", "double", "판정에 사용된 임계값"]
  ],
  [2800, 2000, 4560]
));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 6. EnsembleTool
// ══════════════════════════════════════════════════════
children.push(h1("6. EnsembleTool — 앙상블 판정"));
children.push(p("여러 툴의 결과를 조합해 최종 판정을 만듭니다. 예: DetectionTool(부품 있음) **AND** AnomalyTool(이상 없음) → 양품."));

children.push(h2("6.1 판정 모드"));
children.push(table(
  ["모드", "동작", "사용 예"],
  [
    ["AND", "모든 소스 툴이 Success=true여야 통과", "과검 최소화, 확실한 양품만"],
    ["OR", "한 소스라도 통과하면 Success", "미검 최소화, 의심되면 양품"],
    ["Weighted", "가중치 합이 임계값 이상이면 NG", "소스별 중요도 세밀 조정"],
    ["Consensus", "모든 소스가 일치해야 신뢰", "불일치 시 검토 필요 플래그"]
  ],
  [1600, 3500, 4260]
));

children.push(h2("6.2 주요 파라미터 (Weighted 모드)"));
children.push(table(
  ["파라미터", "기본값", "설명"],
  [
    ["DetectionWeight", "0.5", "Detection 실패가 최종 점수에 기여하는 비중"],
    ["AnomalyWeight", "0.5", "Anomaly 점수가 최종 점수에 기여하는 비중"],
    ["WeightedThreshold", "0.5", "이 값 초과 시 NG 판정"]
  ],
  [2800, 1500, 5060]
));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 7. 학습 데이터 준비
// ══════════════════════════════════════════════════════
children.push(h1("7. 학습 데이터 준비"));
children.push(p("각 툴에 맞는 라벨링·Export 과정을 VMS.DeepLearning(라벨링 앱)에서 수행합니다."));

children.push(h2("7.1 Dataset Task Type별 라벨링 방식"));
children.push(table(
  ["Task Type", "라벨링 방식", "Export 포맷"],
  [
    ["Detection", "바운딩 박스 + 클래스 지정", "YOLO (정규화 좌표 + data.yaml)"],
    ["Classification", "이미지 단위 클래스 지정", "ImageFolder (클래스별 폴더)"],
    ["AnomalyDetection", "good / defect 지정", "MVTec (train/good, test/good, test/defect)"],
    ["OCR", "텍스트 박스 + Transcription", "PaddleOCR (Det + Rec 두 포맷)"],
    ["Segmentation", "SAM 폴리곤 마스크", "YOLO-Seg (정규화 폴리곤)"]
  ],
  [2200, 3000, 4160]
));

children.push(h2("7.2 권장 이미지 수"));
children.push(p("**모든 Task에서 최소치는 동작을 보기 위한 하한**입니다. 실제 운영 정확도를 원한다면 권장치 이상을 수집하세요."));

children.push(h3("Detection"));
children.push(table(
  ["클래스 수", "최소", "권장", "최적"],
  [
    ["1", "100", "500", "1000+"],
    ["2~5", "200", "1000", "2000+"],
    ["5~10", "300", "2000", "5000+"],
    ["10+", "500", "5000", "10000+"]
  ],
  [2400, 2320, 2320, 2320]
));
children.push(p("작은 객체(dot, marker 등)일수록 더 많은 샘플이 필요합니다. 가능하면 **클래스당 100장 이상**을 확보하세요.", { italics: true, color: COLOR_MUTED }));

children.push(h3("Classification"));
children.push(table(
  ["클래스 수", "최소", "권장"],
  [
    ["2 (OK/NG)", "100", "500+"],
    ["3~5", "200", "1000+"],
    ["5+", "300", "2000+"]
  ],
  [3000, 3180, 3180]
));
children.push(p("클래스 간 샘플 수가 크게 차이나면 소수 클래스가 학습되지 않을 수 있습니다. **균형 유지 권장**.", { italics: true, color: COLOR_MUTED }));

children.push(h3("Anomaly Detection"));
children.push(table(
  ["정상 이미지", "평가"],
  [
    ["10~50", "MVTec 표준 수준, 시작 가능"],
    ["50~200", "최적 범위"],
    ["200+", "과도 가능 (PatchCore는 메모리 압박)"]
  ],
  [3000, 6360]
));
children.push(p("Anomaly는 불량 샘플 없이 학습되지만, **평가용**으로 소량(10~30장)의 실제 불량 이미지가 있으면 임계값 튜닝에 도움이 됩니다.", { italics: true, color: COLOR_MUTED }));

children.push(h2("7.3 데이터 분할 (Train / Validation)"));
children.push(p("라벨링 앱의 `Auto Split` 기능은 기본 80% Train / 20% Val로 분할합니다. 분할 후 Export하면 `images/train/`과 `images/val/` (및 대응 labels)이 자동 생성됩니다."));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 8. 학습 실행
// ══════════════════════════════════════════════════════
children.push(h1("8. 학습 실행"));

children.push(h2("8.1 전체 흐름"));
children.push(numbered("VMS.DeepLearning(라벨링 앱) 실행"));
children.push(numbered("Dataset 생성 후 이미지 추가 + 라벨링"));
children.push(numbered("Auto Split으로 Train/Val 나누기"));
children.push(numbered("Export for Training 클릭 → 포맷별 폴더 생성됨"));
children.push(numbered("Training 패널에서 Python/Script/Output 경로 지정"));
children.push(numbered("Epochs/Batch Size 등 설정 후 Start Training"));
children.push(numbered("완료되면 Output 폴더에 `best.onnx`가 생성됨"));
children.push(numbered("VMS.VisionSetup의 해당 Tool의 Model Path에 그 경로 지정"));

children.push(h2("8.2 학습 스크립트별 주요 파라미터"));

children.push(h3("train_yolo.py (Detection)"));
children.push(table(
  ["인자", "기본값", "설명"],
  [
    ["--pretrained", "yolov8n.pt", "사전학습 모델. n/s/m/l/x 중 선택 (오른쪽일수록 정확도↑ 느림)"],
    ["--epochs", "100", "학습 반복. **최소 100 권장**. 10은 완전 부족"],
    ["--batch_size", "16", "GPU 메모리에 따라 8~32. 크면 학습 안정, 메모리 필요"],
    ["--imgsz", "640", "입력 크기. 작은 객체면 832~1280으로 올리기"],
    ["--lr", "0.01", "학습률. 수렴이 불안정하면 0.001로 낮추기"],
    ["--mosaic", "1.0", "Mosaic 증강 확률. 작은 객체 검출에 효과적"],
    ["--hsv_v", "0.4", "밝기 증강. 조명 변동이 큰 공장은 0.5~0.7로 올리기"],
    ["--export_onnx", "—", "학습 후 best.pt → best.onnx 자동 변환"]
  ],
  [2200, 1400, 5760]
));

children.push(h3("train_classifier.py (Classification)"));
children.push(table(
  ["인자", "기본값", "설명"],
  [
    ["--pretrained", "resnet18", "resnet18/resnet50/mobilenet_v2/vgg16"],
    ["--epochs", "50", "분류는 수렴이 빠름. 20~100"],
    ["--batch_size", "32", "—"],
    ["--imgsz", "224", "ResNet 계열 표준은 224"],
    ["--export_onnx", "—", "학습 후 ONNX 변환"]
  ],
  [2200, 1400, 5760]
));

children.push(h3("train_anomaly.py (Anomaly Detection)"));
children.push(table(
  ["인자", "기본값", "설명"],
  [
    ["--method", "patchcore", "patchcore / fastflow / efficient_ad"],
    ["--backbone", "resnet18", "resnet18/resnet50/wide_resnet50_2. 미세 결함은 resnet50"],
    ["--coreset_ratio", "0.1", "코어셋 비율. 0.1=10%(빠름), 0.3=정확, 1.0=느림·메모리↑"],
    ["--imgsz", "224", "미세 결함은 256+"],
    ["--export_onnx", "—", "—"]
  ],
  [2200, 1400, 5760]
));

children.push(h2("8.3 학습 진행 중 확인할 지표"));
children.push(bullet("**box_loss / cls_loss**: 에폭이 지나며 꾸준히 감소해야 정상"));
children.push(bullet("**mAP50**: 0.7 이상이면 양호 (Detection의 경우)"));
children.push(bullet("**val accuracy**: Train보다 너무 낮으면 과적합 가능성"));
children.push(bullet("**최초 10 epoch 시점 mAP50이 0.1 미만**이면 아직 학습이 시작 단계 — 더 기다리기"));

children.push(note("자주 하는 실수:", "10 epoch 정도만 돌리고 \"모델이 안 나온다\"고 판단하는 경우가 많습니다. YOLO는 보통 100 epoch 이상, 작은 객체 검출은 200~500 epoch까지도 필요합니다."));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 9. 추론 엔진 설정 (ONNX Runtime + TensorRT)
// ══════════════════════════════════════════════════════
children.push(h1("9. 추론 엔진 (Execution Provider) 설정"));
children.push(p("VMS.VisionSetup 메인 메뉴의 `Inference Settings...`에서 설정합니다."));

children.push(h2("9.1 Execution Provider 선택"));
children.push(table(
  ["EP", "하드웨어", "속도", "비고"],
  [
    ["CPU", "모든 PC", "느림 (기준)", "GPU가 없거나 테스트 용도"],
    ["CUDA", "NVIDIA GPU", "10배 빠름", "GeForce/Tesla 등. CUDA 드라이버 필요"],
    ["DirectML", "Intel/AMD GPU", "5~8배 빠름", "Windows 내장 GPU 가속"],
    ["TensorRT", "NVIDIA GPU 전용", "CUDA 대비 1.5~3배 더 빠름", "엔진 최초 빌드 30~60초 (이후 캐시)"]
  ],
  [1400, 2200, 2560, 3200]
));

children.push(h2("9.2 TensorRT 사용 팁"));
children.push(bullet("최초 실행 시 엔진을 **빌드**하느라 30초~3분 정도 소요됩니다. 이는 정상입니다."));
children.push(bullet("캐시 폴더 기본값: `%LocalAppData%\\BODA VISION AI\\trt_cache`"));
children.push(bullet("두 번째 실행부터는 캐시에서 로드되어 **2~5초**로 줄어듭니다."));
children.push(bullet("FP16 옵션: RTX 20 시리즈 이상 GPU에서 추론 속도 1.5~3배 향상 (정확도 영향 <0.5%)"));
children.push(bullet("GPU/모델/입력 shape이 바뀌면 캐시가 무효화되어 재빌드됩니다."));

children.push(h2("9.3 Recipe Load 시 자동 프리페치"));
children.push(p("VMS는 Recipe를 Load할 때 모든 DL 툴의 ModelPath를 스캔해 **백그라운드에서 엔진을 미리 로드**합니다. 사용자가 카메라 선택·Step Load를 하는 동안 엔진이 준비되므로 첫 Run 지연이 최소화됩니다."));
children.push(p("디버그 출력에서 다음과 같은 로그로 확인할 수 있습니다:", { color: COLOR_MUTED }));
children.push(...codeBlock([
  "[OnnxCache] YOLO loaded best.onnx in 3124ms (EP: TensorRT)",
  "[OnnxCache] Classifier loaded classifier.onnx in 2041ms (EP: TensorRT)"
]));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 10. 문제 해결 FAQ
// ══════════════════════════════════════════════════════
children.push(h1("10. 자주 묻는 문제와 해결"));

children.push(h2("10.1 \"검출 결과가 0개입니다\""));
children.push(p("**진단 순서**:"));
children.push(numbered("Per-Class Thresholds가 켜져 있고 값이 전역 값보다 높진 않은지 확인 (가장 흔한 원인)"));
children.push(numbered("Confidence Threshold를 0.01로 낮춰 Run — 그래도 0개면 모델 자체 문제"));
children.push(numbered("디버그 로그의 `[YOLO-Diag] MaxScore=X.XXX` 확인: 0.5 이상이면 임계값 문제, 0.05 미만이면 모델 학습 부족"));
children.push(numbered("학습 Epoch이 충분했는지 확인 (YOLO는 100 이상 권장)"));
children.push(numbered("Input Size가 학습 시 imgsz와 일치하는지 확인"));

children.push(h2("10.2 \"첫 Run이 매우 느립니다\""));
children.push(p("TensorRT 첫 실행은 엔진 빌드로 30초~3분 소요됩니다. 이는 **한 번만** 발생하며 이후 캐시로 대체됩니다. 만약 매번 느리다면:"));
children.push(bullet("TensorRT 캐시 폴더 경로가 쓰기 가능한지 확인"));
children.push(bullet("`Inference Settings`에서 캐시 경로가 비어있지 않은지 확인"));
children.push(bullet("첫 실행 후 캐시 폴더에 `.engine` 파일이 쌓이는지 확인"));

children.push(h2("10.3 \"학습이 안 됩니다 (ultralytics 없음 오류)\""));
children.push(p("학습 스크립트가 사용하는 Python 환경에 필요한 패키지가 설치되지 않은 경우:"));
children.push(...codeBlock([
  'py -3.12 -m pip install ultralytics onnx',
  'py -3.12 -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124'
]));
children.push(p("VMS는 `py launcher`로 torch가 설치된 Python 3.12→3.11→3.10 순서로 찾아 사용합니다.", { color: COLOR_MUTED }));

children.push(h2("10.4 \"작은 객체가 검출되지 않습니다\""));
children.push(bullet("학습 시 `--imgsz 1280`으로 입력 해상도 증가"));
children.push(bullet("Tool Settings의 `Inference Size`도 동일하게 1280으로 맞추기"));
children.push(bullet("SAHI 활성화 + Tile Size를 Inference Size의 1~2배로"));
children.push(bullet("더 큰 백본(YOLOv8n → yolov8s 또는 yolov8m) 사용"));
children.push(bullet("학습 증강: `--mosaic 1.0 --mixup 0.15`"));

children.push(h2("10.5 \"정상인데 이상으로 판정됩니다 (AnomalyTool)\""));
children.push(bullet("Anomaly Threshold가 너무 낮음 — 자동 캘리브레이션 재실행"));
children.push(bullet("Calibration Sigma를 3.0 → 4.0으로 올려 관대화"));
children.push(bullet("조명 차이가 원인이면 학습 데이터에 조명 변동 샘플 포함 또는 CLAHE 적용"));

children.push(h2("10.6 \"이상인데 정상으로 판정됩니다 (AnomalyTool)\""));
children.push(bullet("Calibration Sigma를 3.0 → 2.0으로 내려 민감하게"));
children.push(bullet("Input Size를 224 → 256이나 384로 증가 (미세 결함 검출 능력 향상)"));
children.push(bullet("Backbone을 resnet18 → resnet50으로 변경"));
children.push(bullet("Coreset Ratio를 0.1 → 0.2로 증가 (더 많은 특징 보유)"));

children.push(new Paragraph({ children: [new PageBreak()] }));

// ══════════════════════════════════════════════════════
// 11. 전체 워크플로우 예시
// ══════════════════════════════════════════════════════
children.push(h1("11. 전체 워크플로우 예시"));

children.push(h2("11.1 예시: 양/불량 이진 분류 (Classification)"));
children.push(numbered("양품 이미지 200장 + 불량 이미지 200장 수집"));
children.push(numbered("VMS.DeepLearning 실행 → New Dataset → Task Type = Classification"));
children.push(numbered("Add Images로 이미지 추가, 각 이미지에 `good` 또는 `defect` 클래스 지정"));
children.push(numbered("Auto Split 후 Export for Training → Classification 폴더 생성"));
children.push(numbered("Training 패널: Epochs=50, Batch Size=32, Start Training"));
children.push(numbered("`best.onnx` 생성 확인"));
children.push(numbered("VMS.VisionSetup → ClassifyTool 추가 → Model Path 지정"));
children.push(numbered("Confidence Threshold 0.6, Inference Settings에서 CUDA 또는 TensorRT 선택"));
children.push(numbered("테스트 이미지로 Run → OK/NG 판정 확인"));

children.push(h2("11.2 예시: 부품 검출 + 결함 확인 (Ensemble)"));
children.push(numbered("Detection 학습: 부품 위치 라벨링 → YOLO Export → train_yolo.py 실행"));
children.push(numbered("Anomaly 학습: 정상 부품 이미지만 수집 → Anomaly Export → train_anomaly.py 실행"));
children.push(numbered("VisionSetup에서 DetectionTool + AnomalyTool 추가"));
children.push(numbered("DetectionTool의 각 검출 박스를 AnomalyTool의 ROI로 전달 (Tool Connection)"));
children.push(numbered("EnsembleTool 추가 → Mode: AND → Detection과 Anomaly 모두 연결"));
children.push(numbered("운영: Detection이 부품 존재 확인, Anomaly가 각 부품의 이상 확인, Ensemble이 최종 판정"));

children.push(h1("12. 맺음말"));
children.push(p("본 문서는 입문자가 VMS의 딥러닝 툴을 \"선택 → 데이터 준비 → 학습 → 추론 → 튜닝\" 순서로 따라갈 수 있도록 구성되었습니다. 실제 운영에서는 다음 사이클을 반복하며 정확도를 올려가게 됩니다."));
children.push(bullet("작은 샘플로 시작 → 문제가 많은 케이스 식별"));
children.push(bullet("실패 케이스를 라벨링에 추가 → 재학습"));
children.push(bullet("임계값·전처리 튜닝으로 오검/미검 균형 맞추기"));
children.push(bullet("필요시 더 큰 모델 또는 증강 강화"));
children.push(p("각 툴의 최신 코드 위치는 `VMS.VisionSetup/VisionTools/DeepLearning/` 폴더에서 확인할 수 있습니다.", { color: COLOR_MUTED, italics: true }));

// ══════════════════════════════════════════════════════
// 문서 객체 생성
// ══════════════════════════════════════════════════════

const doc = new Document({
  creator: "VMS",
  title: "VMS 딥러닝 도구 기술 문서",
  styles: {
    default: { document: { run: { font: "Malgun Gothic", size: 22 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 36, bold: true, font: "Malgun Gothic", color: COLOR_ACCENT },
        paragraph: { spacing: { before: 360, after: 180 }, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 28, bold: true, font: "Malgun Gothic" },
        paragraph: { spacing: { before: 280, after: 140 }, outlineLevel: 1 } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 24, bold: true, font: "Malgun Gothic", color: COLOR_MUTED },
        paragraph: { spacing: { before: 200, after: 100 }, outlineLevel: 2 } },
    ]
  },
  numbering: {
    config: [
      { reference: "bullets",
        levels: [
          { level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
          { level: 1, format: LevelFormat.BULLET, text: "–", alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 1440, hanging: 360 } } } },
        ]
      },
      { reference: "numbers",
        levels: [
          { level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 720, hanging: 360 } } } }
        ]
      }
    ]
  },
  sections: [{
    properties: {
      page: {
        size: { width: 12240, height: 15840 },   // US Letter
        margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 }
      }
    },
    headers: {
      default: new Header({
        children: [new Paragraph({
          alignment: AlignmentType.RIGHT,
          border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: COLOR_ACCENT, space: 1 } },
          children: [new TextRun({ text: "VMS 딥러닝 도구 기술 문서", size: 18, color: COLOR_MUTED })]
        })]
      })
    },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          alignment: AlignmentType.CENTER,
          children: [
            new TextRun({ text: "Page ", size: 18, color: COLOR_MUTED }),
            new TextRun({ children: [PageNumber.CURRENT], size: 18, color: COLOR_MUTED }),
            new TextRun({ text: " / ", size: 18, color: COLOR_MUTED }),
            new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 18, color: COLOR_MUTED })
          ]
        })]
      })
    },
    children
  }]
});

const outputPath = path.join(__dirname, "VMS_DeepLearning_Manual.docx");
Packer.toBuffer(doc).then(buffer => {
  fs.writeFileSync(outputPath, buffer);
  console.log("생성 완료:", outputPath);
  console.log("파일 크기:", (fs.statSync(outputPath).size / 1024).toFixed(1), "KB");
});
