// GS 제출용 사용자 취급 설명서(매뉴얼) 생성기
// 입력: _manual_blocks.json (parse_manual.py 출력) + screenshots/
// 출력: VMS_사용자매뉴얼_v1.1.docx
const path = require("path");
const fs = require("fs");
const GLOBAL = "C:/Users/vinos/AppData/Roaming/npm/node_modules";
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  VerticalAlign, PageNumber, PageBreak, Header, Footer, ImageRun, TableOfContents,
} = require(path.join(GLOBAL, "docx"));

const DIR = __dirname;                    // docs/gs/pipeline — 스크립트·중간 산출물
const GS = path.join(DIR, "..");          // docs/gs — 제출물(docx)·screenshots
const SHOT = path.join(GS, "screenshots");
const blocks = JSON.parse(fs.readFileSync(path.join(DIR, "_manual_blocks.json"), "utf-8"));
const tools = JSON.parse(fs.readFileSync(path.join(DIR, "_tool_params.json"), "utf-8"));
const OUT = path.join(GS, "VMS_사용자매뉴얼_v1.1.docx");

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
// VMS 메인 화면 컨트롤 캡처(vms_ctl/, 2x DPI) — 1/2 스케일(원래 크기), capW 초과 시 축소.
function ctlImg(file, capW, dir = "vms_ctl") {
  const full = path.join(SHOT, dir, file);
  if (!fs.existsSync(full)) return P(`[스크린샷 자리: ${file}]`, { color: "C00000", bold: true });
  const buf = fs.readFileSync(full);
  const iw = buf.readUInt32BE(16), ih = buf.readUInt32BE(20);
  const wPx = Math.min(Math.round(iw / 2), capW);
  const hPx = Math.round(wPx * ih / iw);
  return new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 60, after: 40 },
    children: [new ImageRun({ type: "png", data: buf, transformation: { width: wPx, height: hPx },
      altText: { title: file, description: file, name: file } })] });
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
  "2.1 VMS 클라이언트 (MSI)": [
    () => imgPara("70_msi_welcome.png", 420),
    () => caption("그림. 설치 마법사 시작 화면 — BODA 브랜드 배너, [다음]/[취소] (라이선스 동의 단계 없음)"),
  ],
  "3.1 첫 실행 화면": [
    () => imgPara("01_vms_main.png", 600), () => caption("그림. VMS 첫 실행 화면 (로그인 전) — 헤더 / KPI 스트립 / 카메라 표시 영역"),
  ],
  "Roller Inspection 섹션": [
    () => ctlImg("sec_roller_inspection.png", 290),
    () => caption("그림. Roller Inspection 섹션 — [Roller] 토글 (작업지시 선택 + 라인스캔 카메라 구성 시 활성)"),
  ],
  "Recipe 섹션": [
    () => ctlImg("sec_recipe.png", 290),
    () => caption("그림. Recipe 섹션 — 현재 레시피 / Available Recipes 목록 / [Load]·[New] / 저장·내보내기·가져오기"),
  ],
  "External Tools 섹션 Supervisor 전용": [
    () => ctlImg("sec_external_tools.png", 290),
    () => caption("그림. External Tools 섹션 — [Vision Tool Setup] / [System Setup] (Supervisor·Admin 전용)"),
  ],
  "Web Parameters 섹션": [
    () => ctlImg("sec_web_parameters.png", 290),
    () => caption("그림. Web Parameters 섹션 — [Sync Parameters]"),
  ],
  "Recent Inspections 섹션": [
    () => ctlImg("sec_recent.png", 290),
    () => caption("그림. Recent Inspections 섹션 — Total / Pass / NG + Pass rate, [Clear]"),
  ],
  "Statistics 섹션": [
    () => ctlImg("sec_statistics.png", 290),
    () => caption("그림. Statistics 섹션 — 세션 누적 Total / Pass / Fail / Pass Rate"),
  ],
  "3.2.1 Operator 로그인": [
    () => ctlImg("hdr_operator_chip.png", 200),
    () => caption("그림. Operator 칩 — 로그인 전([Login...] 버튼)"),
    () => ctlImg("hdr_operator_chip_in.png", 300),
    () => caption("그림. Operator 칩 — 로그인 후(이름·사번 + Role 뱃지 + [Logout])"),
  ],
  "3.2.2 Work Orders 버튼": [
    () => ctlImg("hdr_workorders_btn.png", 180),
    () => caption("그림. [Work Orders] 버튼 — 작업자 로그인 후 활성"),
    () => imgPara("20_dlg_workorders.png", 560),
    () => caption("그림. Work Orders 다이얼로그 — 상태 필터 / Refresh / 목록(Order No·Product·Recipe·Progress·Status·Planned Start) / Select·Cancel"),
  ],
  "3.2.3 WO 칩 (진행률 표시)": [
    () => ctlImg("hdr_wo_chip.png", 340),
    () => caption("그림. WO 칩 — 작업지시 번호 · 제품 · 진행(생산/계획) + 실시간 진행률 바"),
  ],
  "3.2.4 Recipe 칩": [
    () => ctlImg("hdr_recipe_chip.png", 160),
    () => caption("그림. Recipe 칩 — 작업지시 선택 시 자동 로드된 레시피 이름"),
  ],
  "3.2.5 Ctx (Inspection Context)": [
    () => ctlImg("hdr_ctx_chip.png", 360),
    () => caption("그림. Ctx 칩 — WO / Lot 콤보(Open Lot 목록에서 선택, 멀티 Lot 병행) / S/N + [✕] 전체 비우기"),
  ],
  "3.2.6 AUTO RUN": [
    () => ctlImg("hdr_autorun_btn.png", 150),
    () => caption("그림. [AUTO RUN] — 운영 흐름의 단일 실행 버튼(활성화 조건은 §3.5)"),
  ],
  "3.2.7 Role 뱃지": [
    () => ctlImg("hdr_operator_chip_in.png", 300),
    () => caption("그림. Role 뱃지 — Supervisor(빨강) / Lead(보라) 일 때만 표시, Operator 는 뱃지 없음"),
  ],
  "3.3 사이드 패널 (Settings)": [
    () => ctlImg("hdr_panel_toggle.png", 36),
    () => caption("그림. 헤더 우측 설정(⚙) 토글 — 사이드 패널 열기/닫기 (시스템 사용자 로그인 필요)"),
    () => imgPara("05_vms_sidepanel.png", 600),
    () => caption("그림. 사이드 패널(Settings) 펼친 상태 — Roller Inspection / Recipe / External Tools / Updates / Web Parameters / Image Saving"),
    () => P("Updates 섹션 — 새 버전 감지 배지와 수동 체크 버튼(업그레이드 절차는 §6.5):"),
    () => ctlImg("sec_updates.png", 290),
    () => caption("그림. Updates 섹션 — [Check for updates], 새 버전 감지 시 배지 표시"),
    () => P("Image Saving 섹션(Admin 전용)의 [Image Save Settings] 버튼 — 검사 판정 이미지(양품/불량) 저장 설정 다이얼로그:"),
    () => ctlImg("sec_image_saving.png", 290),
    () => caption("그림. Image Saving 섹션 — [Image Save Settings] (Admin 전용)"),
    () => imgPara("21_dlg_imagesave.png", 460),
    () => caption("그림. Image Save Settings — 저장(경로/보존) / 포맷·품질 / 파일명 규칙 / Web 연동"),
    () => P("[Sync Parameters] 버튼 — Web 서버와 레시피 파라미터 동기화 다이얼로그:"),
    () => imgPara("22_dlg_syncparams.png", 460),
    () => caption("그림. Sync Parameters — Web 파라미터 동기화"),
    () => P("외부 도구의 [Vision Tool Setup] 버튼으로 실행되는 비전 설정(VMS.VisionSetup) 화면은 다음과 같다. 그림 아래에 영역별 주요 컨트롤과 그 역할을 표로 정리했다.", {}),
    () => imgPara("03_visionsetup_full.png", 620),
    () => caption("그림. 비전 설정(VMS.VisionSetup) — 카메라 / Steps / Tool Palette(12 카테고리) / Tool Workspace / ROI 도구 / 이미지 뷰 / Tool Settings  [전체 화면 — Tool Palette 펼침]"),
    () => P("■ 헤더 툴바", { bold: true }),
    () => visTable([
      ["Open Image (Ctrl+O)", ["hdr_open_image.png"], "검사할 이미지 파일을 연다"],
      ["Open Image Folder", ["hdr_open_folder.png"], "폴더를 열어 이미지 목록을 탐색한다(이전/다음 이미지 이동)"],
      ["Recipe Manager", ["hdr_recipe_manager.png"], "레시피 목록/스텝·툴 구성/속성 편집 다이얼로그를 연다"],
      ["Save Recipe (Ctrl+S)", ["hdr_save_recipe.png"], "현재 레시피를 저장한다"],
      ["Run All Tools (F5)", ["hdr_run_all.png"], "현재 스텝의 툴 파이프라인 전체를 실행한다"],
      ["Run Selected Tool (F6)", ["hdr_run_selected.png"], "선택한 툴만 실행한다"],
      ["Clear All Tools", ["hdr_clear_tools.png"], "Tool Workspace 의 툴을 모두 삭제한다"],
      ["Camera Manager", ["hdr_camera_manager.png"], "카메라 등록/연결 관리 다이얼로그를 연다"],
      ["Acquire Image", ["hdr_acquire.png"], "연결된 카메라에서 이미지를 취득한다"],
      ["SLM Recipe Bot (AI)", ["hdr_slm_bot.png"], "AI 챗봇으로 레시피 구성을 보조한다"],
    ]),
    () => P("■ 카메라 / Steps 패널", { bold: true }),
    () => visTable([
      ["Camera", ["cam_combo.png"], "활성 카메라를 선택한다"],
      ["Step 추가 / 삭제", ["step_add.png", "step_delete.png"], "검사 스텝을 추가 / 선택 스텝을 삭제한다"],
      ["Generate Robot Waypoint Steps", ["step_waypoint.png"], "로봇 웨이포인트 기반 스텝을 자동 생성한다(멀티뷰 3D 스캔)"],
      ["Move Up / Move Down", ["step_up.png", "step_down.png"], "스텝 실행 순서를 위/아래로 이동한다"],
    ]),
    () => P("■ Tool Palette / 이미지 뷰 · ROI 도구", { bold: true }),
    () => visTable([
      ["Expand All / Collapse All", ["palette_expand.png", "palette_collapse.png"], "팔레트 12개 카테고리를 전체 펼침/접힘. 툴은 Tool Workspace 로 드래그해 추가한다"],
      ["이미지 소스", ["img_source.png"], "표시할 이미지를 선택한다(Original Image / 각 툴의 결과 이미지)"],
      ["[Save Image]", ["img_save.png"], "현재 표시 중인 이미지(Original/Result)를 PNG/JPG/BMP/TIFF 파일로 저장한다"],
      ["ROI 도형", ["roi_select.png", "roi_rect.png", "roi_rectaffine.png", "roi_circle.png", "roi_ellipse.png", "roi_polygon.png"], "관심영역(ROI) 그리기 — Select(선택/이동) / Rectangle / RectAffine(회전 사각형) / Circle / Ellipse / Polygon"],
      ["줌", ["zoom_out.png", "zoom_in.png", "zoom_fit.png"], "배율 축소(−)/확대(+) / 화면 맞춤(Fit)"],
      ["[Delete] / [Clear All]", ["roi_delete.png", "roi_clear.png"], "선택한 ROI 삭제 / 모든 ROI 삭제"],
      ["3D 뷰 카메라", ["view3d_reset.png", "view3d_top.png", "view3d_front.png", "view3d_side.png"], "Point Cloud 뷰 시점 프리셋 — Reset / Top / Front / Side"],
    ]),
    () => P("■ Tool Settings 패널 (우측)", { bold: true }),
    () => visTable([
      ["Expert Mode", ["ts_expert.png"], "고급 파라미터를 노출하는 토글"],
      ["Enabled", ["ts_enabled.png"], "선택 툴의 실행 활성/비활성 (Common Settings)"],
      ["Use ROI", ["ts_useroi.png"], "체크 시 ROI 영역 내에서만 툴을 실행한다"],
      ["[+ Add PLC Mapping]", ["ts_addplc.png"], "툴 결과값을 PLC 주소에 매핑해 검사 결과를 전송한다 (PLC Output)"],
    ]),
    () => P("VisionSetup 상단 메뉴에서 열리는 주요 다이얼로그: (각 그림 아래에 주요 컨트롤 설명)"),
    () => imgPara("33_dlg_cameramgr.png", 520),
    () => caption("그림. Camera → Camera Manager — 카메라 등록/연결 관리"),
    () => bullet("Cameras 목록 — 등록된 카메라(이름·제조사)를 표시한다. 선택하면 우측 패널에 연결 상태와 파라미터 설정이 표시된다."),
    () => bullet("[Add] / [Remove] — 카메라를 등록 / 선택한 카메라를 제거한다."),
    () => imgPara("34_dlg_recipemgr.png", 540),
    () => caption("그림. Recipe → Recipe Manager — 레시피 목록/관리"),
    () => bullet("좌측 목록 — Search recipes 검색창 + [X](초기화), 레시피 카드(이름·버전·스텝/툴 수·수정일시·작성자)."),
    () => bullet("[New] / [Load] / [Delete] — 레시피 생성 / 선택 레시피를 VisionSetup 에 로드 / 삭제."),
    () => bullet("[Import] / [Export] / [Open Folder] — 레시피 파일 가져오기 / 내보내기 / 저장 폴더 열기."),
    () => bullet("중앙 트리 — 로드된 레시피의 Step/Tool 구조. [Add Step] / [Add Tool] / [Remove] 로 편집한다."),
    () => bullet("우측 Properties — 선택 항목의 속성 표시/편집. 상단 [Save] / [Discard] 로 변경을 저장/취소한다."),
    () => imgPara("35_dlg_sequence.png", 540),
    () => caption("그림. Sequence — 검사 시퀀스 편집"),
    () => bullet("상단 바 — 프로세스 시퀀스 선택, Reset 신호 설정, [디폴트 생성] / [저장] / [가져오기] / [내보내기] / [PLC Monitor] / [무부하 테스트]."),
    () => bullet("좌측 노드 팔레트 — Start / End / Input Check / Output Action / Inspection / Branch / Delay / Repeat / Recipe Change / Step Change 노드를 캔버스로 드래그한다."),
    () => bullet("중앙 캔버스 — 노드를 Next 링크로 연결해 검사 시퀀스를 구성한다."),
    () => bullet("우측 노드 속성 — 선택한 노드의 파라미터를 편집한다."),
    () => bullet("하단 상태바 — 로드된 시퀀스 이름과 노드/연결 수를 표시한다."),
    () => imgPara("30_dlg_batchtest.png", 540),
    () => caption("그림. Batch Test… — 다수 이미지 일괄 검사/검증"),
    () => bullet("ACTIVE PIPELINE — Recipe → Camera → Step 콤보로 적용할 파이프라인 선택. Tools 수 표시 + [Auto-tune…] / [Edit Thresholds…]."),
    () => bullet("INPUT — 이미지 폴더 + [Browse], [하위 폴더 재귀 탐색] 체크."),
    () => bullet("OUTPUT — CSV 리포트 저장 경로, [실패 케이스 오버레이 저장] + 저장 폴더."),
    () => bullet("통계 카드 — PROCESSED / PASS / FAIL / AVG TIME(ms) 실시간 집계."),
    () => bullet("ACTIVITY — 진행률, 골든셋([Save as Golden] / [Load Golden] / [Clear]), [Browse Failures] / [Open CSV] / [Failure Folder], Live Log·Results 탭."),
    () => bullet("[Run Batch] / [Cancel] / [Close] — 일괄 검사 실행 / 중단 / 닫기."),
    () => imgPara("31_dlg_synthdata.png", 540),
    () => caption("그림. OCR Synth Data… — OCR 합성 데이터 생성"),
    () => bullet("Font — 기본/추가 폰트, 폰트 크기 Min·Max(px), Bold/Italic 무작위."),
    () => bullet("Patterns — 생성할 텍스트 토큰 패턴(D/M/Y/H/S=날짜·시각, L=영문자, A=영숫자, ?=임의) + 프리셋 칩."),
    () => bullet("Dataset — 샘플 수, Val 비율(0~0.3), 출력 포맷(PaddleOCR rec)·출력 폴더."),
    () => bullet("Background / Augmentation — 배경 모드(Solid/FromFolder)·흑/백 무작위 반전, 회전·원근 jitter·Blur·노이즈·밝기/대비 jitter·도트 매트릭스 효과."),
    () => bullet("Training — PP-OCR Fine-tuning: Python 실행파일, train_ppocr.py 경로, 학습 출력 폴더, 사전학습 모델 prefix, Epochs/Batch Size/Learning Rate, ONNX export."),
    () => bullet("Training Progress — Epoch/Loss/Acc 진행, ONNX 출력 경로, 학습 로그. 하단 [Generate] / [Generate & Train] / [Cancel Train]."),
    () => imgPara("32_dlg_inference.png", 460),
    () => caption("그림. Inference Settings… — 딥러닝 추론(ONNX) 설정"),
    () => bullet("Execution Provider — ONNX Runtime 실행 백엔드 선택(Auto / CPU / CUDA / TensorRT)."),
    () => bullet("TensorRT Engine Cache Folder — 엔진 캐시 폴더. 설정 시 재실행에서 엔진 재빌드를 건너뛴다(비우면 캐시 없음)."),
    () => bullet("Enable TensorRT FP16 — RTX 계열에서 2~3배 속도 향상."),
    () => bullet("[Save] / [Cancel] — 저장(다음 모델 로드/세션 재생성 시점부터 적용) / 취소."),
  ],
  "3.8 딥러닝 라벨링·학습 (VMS.DeepLearning)": [
    () => imgPara("06_deeplearning_full.png", 620),
    () => caption("그림. VMS.DeepLearning — 데이터셋·이미지 목록(좌) / 라벨링 캔버스(중) / 클래스·라벨·학습(우)  [Detection 데이터셋 예시]"),
  ],
  "화면 구성": [
    () => ctlImg("bar_toolbar.png", 460, "deeplearning_ctl"),
    () => caption("그림. 상단 툴바 — [New Dataset] / [Save] / [Add Images] / [Auto Split] / [Export]"),
    () => ctlImg("sec_dataset.png", 250, "deeplearning_ctl"),
    () => caption("그림. Dataset 섹션 — 데이터셋 목록, 새 이름 + 작업 유형 선택, [Load]·[Save]·[Delete]"),
    () => ctlImg("sec_images.png", 250, "deeplearning_ctl"),
    () => caption("그림. Images 섹션 — [+ Add]·[- Del], 이미지 이동(◀/▶), 라벨·학습 현황 요약"),
    () => ctlImg("sec_classes.png", 250, "deeplearning_ctl"),
    () => caption("그림. Classes 섹션 — 클래스 목록과 추가([+])"),
    () => ctlImg("sec_labels.png", 250, "deeplearning_ctl"),
    () => caption("그림. Labels 섹션 — 현재 이미지의 라벨 목록, [Delete]"),
    () => ctlImg("sec_label_editor.png", 250, "deeplearning_ctl"),
    () => caption("그림. Label Editor — 선택한 라벨의 클래스 / 텍스트(Transcription) / 검증 여부 편집"),
    () => ctlImg("sec_export.png", 250, "deeplearning_ctl"),
    () => caption("그림. Export 섹션 — [Auto Split (Train/Val)] / [Export for Training] + 작업 유형별 내보내기 형식 안내"),
    () => ctlImg("sec_training.png", 250, "deeplearning_ctl"),
    () => caption("그림. Training 섹션 — Python·스크립트·출력 경로, Epochs/Batch Size, [Start Training]·[Stop], 진행률·로그"),
  ],
  "작업 유형 — 데이터셋을 만들 때 선택": [
    () => ctlImg("sec_classes_anomaly.png", 250, "deeplearning_ctl"),
    () => caption("그림. Anomaly(이상 탐지) 유형의 분류 버튼 — [GOOD (정상)] / [DEFECT (불량)]"),
  ],
  "학습 결과 바로 확인 (Inference Mode)": [
    () => ctlImg("sec_inference.png", 250, "deeplearning_ctl"),
    () => caption("그림. Inference Mode — 모델 선택, Conf(신뢰도)·IoU(중복 제거) 슬라이더"),
  ],
  "부족한 데이터 보강 (Active Learning)": [
    () => ctlImg("sec_active_learning.png", 250, "deeplearning_ctl"),
    () => caption("그림. Active Learning — 테스트 폴더 / 실패 기준 / [▶ 일괄 추론 실행] / [✚ 데이터셋에 추가]"),
  ],
  "클릭 한 번으로 윤곽 라벨링 (SAM)": [
    () => ctlImg("sec_sam_model.png", 250, "deeplearning_ctl"),
    () => caption("그림. SAM Model 섹션 — Encoder/Decoder 파일 지정, [Load SAM Model]"),
    () => ctlImg("bar_sam_toolbar.png", 340, "deeplearning_ctl"),
    () => caption("그림. SAM 라벨링 도구모음 — 좌클릭 전경 / 우클릭 배경, [Confirm (Enter)]·[Clear (Esc)]"),
  ],
  "4. BODA.VMS.Web (관리자 / MES)": [
    () => P("아래 그림은 BODA.VMS.Web 관리 화면입니다(관리자 로그인 기준)."),
    () => imgPara("40_web_login.png", 360),
    () => caption("그림 4-0. Web 로그인"),
  ],
  "4.3 Dashboard": [
    () => imgPara("41_web_dashboard.png", 600),
    () => caption("그림. Dashboard — 현장 KPI(전체 클라이언트/금일 생산·합격/불량률) + 사이드 메뉴"),
  ],
  "4.5 Alarms": [() => imgPara("43_web_알람.png", 600), () => caption("그림. Alarms — 알람 목록")],
  "4.6 Production History": [() => imgPara("44_web_생산_이력.png", 600), () => caption("그림. Production History — 라인/작업지시/LOT/기간(같은 날짜 = 하루치)/결과 필터, 요약 카드, WO/LOT 열, Excel 내보내기")],
  "4.7 Work Orders": [() => imgPara("45_web_작업_지시.png", 600), () => caption("그림. Work Orders — 작업지시 목록(진척률·상태·[시작]/[완료]/[LOT] 버튼), 검사마다 실시간 갱신")],
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
  out.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun("3.11 관리자 도구 다이얼로그 (Admin 전용)")] }));
  out.push(P("헤더의 User Management 아이콘과 Admin Tools(⋮) 드롭다운에서 실행되는 관리자 전용 다이얼로그다. (Admin 권한 로그인 시에만 표시) 각 그림 아래에 화면의 주요 컨트롤과 그 역할을 정리했다."));
  const dlgs = [
    ["23_dlg_usermgmt.png", "User Management — 사용자 계정·권한(UserGrade) 관리", [
      "Users 목록 — 등록된 계정을 표로 표시(Username / Display Name / Grade / Last Login). 행을 선택하면 우측 [Edit Selected User] 패널에 값이 채워진다.",
      "Add New User — Username·Display Name·Password 입력, Grade 콤보(권한 등급) 선택 후 [Add User] 로 신규 계정을 추가한다.",
      "Edit Selected User — 선택한 사용자의 Display Name·Grade 를 수정하고 [Save Changes] 로 저장한다.",
      "New Password + [Change Password] — 선택 사용자의 비밀번호를 재설정한다.",
      "[Delete User] — 선택한 사용자 계정을 삭제한다.",
      "하단 상태 메시지 — 추가/저장/삭제 등 작업 결과를 표시한다.",
    ]],
    ["24_dlg_audit.png", "Audit Log Viewer — 감사 로그 조회(카테고리/기간 필터)", [
      "상단 상태 — 현재 조회 기간과 결과 건수(예: 2026-06-05 ~ 2026-06-12: 331건).",
      "필터 바 — From / To 날짜, Category(감사 카테고리, 비우면 전체), Outcome(Success / Failure / Denied, 비우면 전체), Search(Action·User·Source·Details 텍스트 검색).",
      "버튼 — [조회] 필터 적용 · [필터 초기화] 조건 리셋 · [CSV 내보내기] 현재 결과를 CSV 파일로 저장.",
      "결과 그리드 — Timestamp(UTC) / Category / Action / Outcome / User / Source / Details. Outcome 은 성공(초록)·실패(빨강)·거부(주황)로 색상 구분된다.",
      "하단 — 원본 로그 저장 위치(%LocalAppData%\\BODA VISION AI\\audit\\YYYY-MM-DD.jsonl) 안내.",
    ]],
    ["25_dlg_health.png", "Health Check — 시스템 상태 점검", [
      "요약 카드 — OVERALL 종합 상태 배지(Pass 초록 / Warn 주황 / Fail 빨강), 요약 문구, 마지막 점검 시각(Last run).",
      "[Refresh] — 점검을 다시 실행한다. [Copy] — 점검 결과 전체를 클립보드로 복사한다.",
      "항목 목록 — 점검 항목마다 상태 배지(Pass / Warn / Fail) + 항목명 + 상세 메시지를 한 줄씩 표시한다.",
      "하단 — 결과는 System / StartupHealthCheck 감사 이벤트로 자동 기록된다.",
    ]],
    ["26_dlg_backup.png", "Backup / Restore — 데이터 백업 및 복원", [
      "백업 생성 — [감사 로그(audit/) 포함] 체크, ProductVersion(manifest 기록용) 입력, 상태 배지와 최근 백업 요약, [Run Backup...](저장 경로 지정 후 백업 실행).",
      "백업 복원 — 복원할 파일 경로(읽기 전용) + [Browse...], [기존 파일 덮어쓰기]·[감사 로그도 복원] 체크, 상태 배지와 최근 복원 요약, [Run Restore].",
      "복원된 백업 manifest — 복원한 백업의 manifest(생성 시각·버전·포함 항목) 요약을 표시한다.",
      "하단 — 모든 작업은 Configuration / BackupCreated·BackupRestored 감사 이벤트로 기록되며, 복원 후에는 VMS 재시작을 권장한다.",
    ]],
    ["27_dlg_autobackup.png", "Auto Backup Settings — 자동 백업 정책", [
      "[자동 백업 활성화] — 주기적 자동 백업 사용 여부.",
      "백업 주기(시간) [1–720] · 보존 기간(일) [1–365] — 자동 백업 실행 간격과 오래된 백업 삭제 기준.",
      "백업 위치 + [Browse...] — 저장 폴더(비우면 %LocalAppData%\\BODA VISION AI\\backups).",
      "[감사 로그(audit/) 포함] — 자동 백업에 감사 로그 포함 여부(보통 끔; 감사 보존은 별도 정책).",
      "ProductVersion(manifest) — 백업 manifest 에 기록할 제품 버전.",
      "하단 상태 표시줄 · [Reload](디스크 저장값 다시 읽기) · [Save](변경 저장).",
    ]],
    ["28_dlg_retention.png", "Retention Settings — 데이터 보존 정책(감사/백업/업로드 큐 + 카테고리별)", [
      "빠른 프리셋 — [Conservative](규제 사이트) / [Standard](GS 권장 기본) / [Minimal](디스크 제한 사이트) 로 값을 일괄 채운다(적용 후 Save 필요).",
      "전역 보존(일) — 감사 로그(audit/) · 자동 백업(backups/) · 업로드 큐(upload_queue/) 각각의 보존 기간. 입력란 옆에 허용 범위를 표시한다.",
      "카테고리별 차등 보존(일) — 9개 audit 카테고리별로 보존 기간을 별도 지정(전역 정리 후 재필터). 기본값은 GS 권장(보안/사용자/설정 1095일, 인증/권한/레시피 730일, 시퀀스/검사 365일, 시스템 90일).",
      "적용 미리보기 — [Preview] 를 누르면 dry-run(실제 삭제 없음) 결과 요약이 표시되고 [Export Preview...] 로 CSV 저장이 가능하다.",
      "하단 — 상태 표시줄 · [Reload] · [Preview] · [Export Preview...] · [Save]. 변경은 VMS 재시작 후 적용된다.",
    ]],
    ["29_dlg_support.png", "Support Package — 원격 지원용 진단 패키지 생성", [
      "안내 — 원격 지원/분석용 ZIP 을 생성한다. 백업과 달리 사용자 DB(BCrypt 해시)와 레시피는 포함되지 않는다.",
      "포함할 감사 로그 일수 [1–90] · ProductVersion(manifest) — 패키지에 담을 감사 로그 범위와 기록 버전.",
      "[환경 정보 포함] — OS / .NET / CPU / 메모리 / 드라이브 정보 포함.",
      "[백업 인덱스 포함] — 백업 파일명·크기·시각 등 메타데이터만 포함(내용 제외).",
      "[MachineName / DomainName 포함] — 사이트 식별 PII 로 기본 제외.",
      "상태 배지 + 요약 · [Run Export...](ZIP 생성). 작업은 System / SupportPackageCreated 감사 이벤트로 기록된다.",
    ]],
  ];
  dlgs.forEach(([f, c, items]) => {
    out.push(imgPara(f, 540));
    out.push(caption("그림. " + c));
    (items || []).forEach(t => out.push(bullet(t)));
  });
  return out;
}
// 챕터 3 시작 전에 AppSetup 마법사 섹션을 끼워넣음 (= 설치 챕터 말미)
const WIZARD_BEFORE = "3. VMS 클라이언트 매뉴얼";

// 마법사 컨트롤 항목 표 — 컨트롤 이미지(appsetup_ctl/, --capture-controls 산출물) + 항목/설명/기본값
const WIZ_COLW = [1600, 3000, 3100, 1326]; // 항목 / 컨트롤 / 설명 / 기본값 (합 = CONTENT_W)
function ctlCell(files, w, dir = "appsetup_ctl") {
  // 컨트롤 캡처는 2x DPI → 1/2 스케일이 원래 크기. 셀 폭(px) 초과 시 셀에 맞춰 축소.
  // 작은 아이콘은 한 줄에 여러 개 배치.
  const maxPx = Math.floor(w / 1440 * 96) - 12;
  const lines = [];
  let cur = [], curW = 0;
  (files || []).forEach(f => {
    const full = path.join(SHOT, dir, f);
    if (!fs.existsSync(full)) { lines.push([new TextRun({ text: `[${f}]`, color: "C00000", size: 16 })]); return; }
    const buf = fs.readFileSync(full);
    const iw = buf.readUInt32BE(16), ih = buf.readUInt32BE(20);
    const wPx = Math.min(Math.round(iw / 2), maxPx);
    const hPx = Math.round(wPx * ih / iw);
    const run = new ImageRun({ type: "png", data: buf, transformation: { width: wPx, height: hPx },
      altText: { title: f, description: f, name: f } });
    if (cur.length && curW + wPx + 6 > maxPx) { lines.push(cur); cur = []; curW = 0; }
    if (cur.length) { cur.push(new TextRun({ text: " " })); curW += 6; }
    cur.push(run); curW += wPx;
  });
  if (cur.length) lines.push(cur);
  const paras = lines.map(children => new Paragraph({ spacing: { before: 20, after: 20 }, alignment: AlignmentType.CENTER, children }));
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    verticalAlign: VerticalAlign.CENTER, children: paras.length ? paras : [new Paragraph({ spacing: { after: 0 }, children: runs("—", { size: 18 }) })] });
}
// VisionSetup 주요 컨트롤 표 — 항목 / 컨트롤 / 설명 3열 (visionsetup_ctl/)
const VIS_COLW = [2000, 2800, 4226];
function visTable(rows) {
  const heads = ["항목", "컨트롤", "설명"];
  const trs = [new TableRow({ tableHeader: true, children: heads.map((h, i) => hcell(h, VIS_COLW[i])) })];
  rows.forEach(([name, files, desc], ri) => {
    const fill = ri % 2 ? "F4F7FB" : undefined;
    trs.push(new TableRow({ children: [
      dcell(name, VIS_COLW[0], fill),
      ctlCell(files, VIS_COLW[1], "visionsetup_ctl"),
      dcell(desc, VIS_COLW[2], fill),
    ] }));
  });
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: VIS_COLW, rows: trs });
}

function wizardTable(rows) {
  const heads = ["항목", "컨트롤", "설명", "기본값 / 예시"];
  const trs = [new TableRow({ tableHeader: true, children: heads.map((h, i) => hcell(h, WIZ_COLW[i])) })];
  rows.forEach(([name, files, desc, def], ri) => {
    const fill = ri % 2 ? "F4F7FB" : undefined;
    trs.push(new TableRow({ children: [
      dcell(name, WIZ_COLW[0], fill),
      ctlCell(files, WIZ_COLW[1]),
      dcell(desc, WIZ_COLW[2], fill),
      dcell(def, WIZ_COLW[3], fill),
    ] }));
  });
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: WIZ_COLW, rows: trs });
}

function wizardSection() {
  const out = [];
  out.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun("2.5 최초 실행 — 시스템 설정 마법사 (VMS.AppSetup)")] }));
  out.push(P("⚙ 이 절은 설치 담당자용입니다. 마법사는 설치 후 최초 1회(또는 설정 파일이 없을 때)만 나타나며, 일반 작업자는 볼 일이 없습니다.", { size: 18, color: "595959" }));
  out.push(P("VMS 설치 후 최초 실행 시(또는 system_config.json 부재 시) 시스템 설정 마법사가 자동 실행된다. 총 7단계로 애플리케이션·네트워크·카메라·PLC·로봇/IO·보안 모드 및 초기 관리자 계정을 구성한 뒤 [Finish] 로 저장한다. 카메라가 없는 환경에서는 3단계에서 [Virtual Mode (Manual Setup)] 를 선택해 가상 구성으로 진행할 수 있다. 각 단계의 그림 아래에 입력 항목별 컨트롤 이미지와 설명을 표로 정리했다."));
  out.push(P("입력란 라벨 옆의 ⓘ 아이콘에 마우스를 올리면 그 항목의 쉬운 설명이 말풍선으로 표시된다 — 이 표의 설명과 같은 내용이므로, 설정 중에 매뉴얼을 뒤지지 않아도 된다."));
  const steps = [
    ["10_appsetup_step1.png", 470, "1단계 — 시작(Welcome)", [
      P("1단계는 마법사 시작 화면으로 입력 항목이 없다. [Next] 를 눌러 진행하며, 이후 애플리케이션·네트워크 → 카메라 → PLC → 로봇 → IO 보드 → 보안 모드 순으로 구성한다."),
      wizardTable([
        ["[← Back] / [Next]", ["P1_default_11_Button__Back.png", "P1_default_12_Button_Next.png"], "이전 / 다음 단계로 이동. 모든 단계 하단에 공통 표시되며, 마지막 7단계에서는 [Next] 대신 [Finish] 가 표시된다", "—"],
      ]),
    ]],
    ["10_appsetup_step2_full.png", 460, "2단계 — Application Settings: 애플리케이션명 / System IP / Web Server 연동(Client Index·Web Server URL·API Key) / SSO  [전체 화면 — 스크롤 콘텐츠 포함]", [
      P("■ 2단계 입력 항목 상세", { bold: true }),
      wizardTable([
        ["Application Name", ["P2_default_02_TextBox_OP102.png"], "시스템을 식별하는 애플리케이션 표시 이름", "BODA Vision System"],
        ["System IP Address", ["P2_default_04_TextBox_1921681102.png"], "머신비전 시스템 PC의 IP 주소", "192.168.0.1"],
        ["Client Index", ["P2_default_08_TextBox_2.png"], "BODA.VMS.Web에서 이 클라이언트를 식별하는 고유 번호(설치 라인별 1, 2, 3…)", "1"],
        ["Web Server URL", ["P2_default_11_TextBox_httplocalhost5292.png"], "BODA.VMS.Web 서버 주소 — Heartbeat 전송·파라미터 동기화에 사용", "http://localhost:5292"],
        ["Vision Server URL", ["P2_default_14_TextBox_httplocalhost5000.png"], "VisionServer API 주소 — 클라이언트 자동 등록에 사용", "http://localhost:5000"],
        ["Web Client API Key", ["P2_default_17_TextBox_TextBox.png"], "X-API-Key 헤더 비밀값(Web 서버의 ClientApiKey:Value와 동일). 비우면 헤더를 보내지 않으며 서버 호환 모드에서만 통과", "(빈 값)"],
        ["Web SSO 활성", ["P2_default_20_CheckBox_Web_SSO_활성__AdminManager_인증을_BODAVMSWeb_.png"], "체크 시 Admin/Manager 인증을 BODA.VMS.Web으로 위임(단일 계정 관리). Web 도달 불가 시 비상 계정 'local-admin'만 제한 권한으로 진입", "해제"],
        ["VMS Admin 비밀번호", ["P2_default_25_PasswordBox_InitialAdminPasswordBox.png"], "사용자 인증용 정규 admin 계정 비밀번호. VMS 단독 운영 또는 SSO 비활성 환경에서 사용. 최소 8자, 12자 이상 권장", "신규 설치 시 필수"],
        ["Local Fallback Admin 비밀번호", ["P2_default_28_PasswordBox_LocalAdminPasswordBox.png"], "'local-admin' 비상 계정 비밀번호. Web 도달 불가 시에만 제한 권한으로 사용하며 안전한 곳에 별도 보관", "신규 설치 시 필수"],
      ]),
      P("※ 신규 설치 시 두 비밀번호는 필수 입력입니다(디폴트 비밀번호 자동 시드는 보안상 제거됨). 기존 설치에 이미 계정이 존재하면 비워 두어 변경하지 않을 수 있습니다.", { size: 18, color: "595959" }),
    ]],
    ["10_appsetup_step3.png", 470, "3단계 — Camera Configuration: Live/Virtual 모드, [+ Add Camera], [Scan Network], 노출/게인/캡처모드", [
      P("■ 3단계 입력 항목 상세", { bold: true }),
      wizardTable([
        ["카메라 모드", ["P3_default_01_RadioButton_Live_Mode_Scan_Network.png", "P3_default_02_RadioButton_Virtual_Mode_Manual_Setup.png"], "Live: 네트워크 스캔으로 연결 카메라 자동 검출 / Virtual: 카메라 없이 수동 구성", "Virtual"],
        ["[+ Add Camera]", ["P3_default_03_Button__Add_Camera.png"], "카메라 항목을 수동으로 추가", "—"],
        ["[Scan Network]", ["P3_default_04_Button_Scan_Network.png"], "GigE Vision 표준(UDP 3956 브로드캐스트)으로 네트워크 카메라 검색", "—"],
        ["Name / IP", ["P3_camtypes_03_TextBox_AreaCam.png", "P3_camtypes_05_TextBox_1921680101.png"], "카메라 식별 이름 / IP 주소", "Camera 1 / 192.168.0.101"],
        ["Camera Type", ["P3_camtypes_06_ComboBox_AreaScan2D.png"], "스캔 방식(AreaScan2D·3D / LineScan2D·3D). 선택에 따라 하단 파라미터 패널 전환", "AreaScan2D"],
        ["Manufacturer", ["P3_camtypes_07_ComboBox_HIK.png"], "제조사. Matrox·Dalsa 선택 시 Frame Grabber(MIL) 패널 표시", "HIK"],
        ["〈Area Scan〉 Exposure(μs) / Gain", ["P3_camtypes_10_TextBox_5000.png", "P3_camtypes_12_TextBox_1.png"], "노출 시간 / 게인", "5000 / 1.0"],
        ["〈Line Scan〉 Trigger / Line Rate / Scan Length", ["P3_camtypes_22_ComboBox_Encoder.png", "P3_camtypes_24_TextBox_10000.png"], "트리거 소스(Internal·Encoder) / 라인 레이트(Hz) / 스캔 길이", "Internal / 10000 / 4096"],
        ["〈Line Scan·Encoder〉 Encoder Res(P/mm)", ["P3_camtypes_28_TextBox_10.png"], "트리거가 Encoder일 때 표시 — 엔코더 해상도", "10.0"],
        ["〈3D〉 Capture Mode / Filter / Z Min·Max(mm)", ["P3_camtypes_42_ComboBox_Both.png", "P3_camtypes_44_TextBox_3.png"], "2D·3D 캡처 모드 / 필터 강도 / Z 범위", "Both / 3 / 0·1000"],
        ["〈Frame Grabber〉 Board Type / Board# / Digitizer# / DCF", ["P3_camtypes_63_ComboBox_ComboBox.png", "P3_camtypes_70_Button_unnamed.png"], "MIL 보드 타입 / 보드·디지타이저 번호 / Camera Link DCF 파일", "SOLIOS / 0 / 0 / —"],
      ]),
      P("※ 〈 〉 표시 항목은 선택한 카메라 타입·제조사에 해당하는 패널이 나타날 때만 표시됩니다.", { size: 18, color: "595959" }),
    ]],
    ["10_appsetup_step4.png", 470, "4단계 — PLC Communication: 벤더 / 통신 타입 / IP·Port / 폴링·하트비트 / Write·Endian", [
      P("■ 4단계 입력 항목 상세", { bold: true }),
      wizardTable([
        ["PLC Vendor", ["P4_ethernet_02_RadioButton_Mitsubishi.png", "P4_ethernet_06_RadioButton_Modbus_TCP.png"], "Mitsubishi / Siemens / LS Electric / Omron / Modbus TCP / None", "None"],
        ["Communication Type", ["P4_ethernet_20_ComboBox_Ethernet.png"], "통신 방식(Ethernet / Serial 등)", "Ethernet"],
        ["〈Ethernet〉 PLC IP Address / PLC Port", ["P4_ethernet_22_TextBox_1921680100.png", "P4_ethernet_24_TextBox_502.png"], "PLC IP 주소 / 포트", "192.168.0.100 / 502"],
        ["〈Modbus 벤더〉 Modbus Unit ID", ["P4_modbus_02_TextBox_255.png"], "Modbus 슬레이브 ID(1–255)", "255"],
        ["〈Serial〉 Port / Baud / Data / Parity / Stop", ["P4_serial_03_TextBox_COM1.png", "P4_serial_05_ComboBox_115200.png"], "시리얼 포트 파라미터", "COM1 / 115200 / 8 / None / One"],
        ["Polling Interval(ms)", ["P4_ethernet_10_TextBox_20.png"], "PLC 폴링 주기", "20"],
        ["Use Heartbeat / Heartbeat Address", ["P4_ethernet_11_CheckBox_Use_Heartbeat.png"], "하트비트 감시 사용 / 주소(예: D100)", "해제 / —"],
        ["Auto Reconnect", ["P4_ethernet_13_CheckBox_Auto_Reconnect.png"], "연결 끊김 시 자동 재접속", "사용"],
        ["Write Mode / Endian Mode", ["P4_ethernet_16_ComboBox_Handshake.png", "P4_ethernet_18_ComboBox_LittleEndian.png"], "데이터 쓰기 모드 / 워드 바이트 순서", "Handshake / LittleEndian"],
      ]),
      P("※ 통신 타입이 Serial이면 IP·Port 대신 시리얼 포트 항목이, Modbus 벤더면 Modbus Unit ID가 표시됩니다.", { size: 18, color: "595959" }),
    ]],
    ["10_appsetup_step5_full.png", 520, "5단계 — Robot Configuration: 로봇 연동(Enable) / 벤더 / 연결 / 프로토콜  [전체 화면 — 로봇 연동 활성, 스크롤 콘텐츠 포함]", [
      P("■ 5단계 입력 항목 상세", { bold: true }),
      wizardTable([
        ["Enable Robot Integration", ["P5_disabled_01_CheckBox_Enable_Robot_Integration.png"], "멀티뷰 3D 스캔용 로봇 사용 활성화. 해제 시 이하 항목 숨김", "해제"],
        ["Robot Vendor", ["P5_enabled_02_RadioButton_Universal_Robots_UR.png", "P5_enabled_03_RadioButton_Doosan_Robotics.png"], "UR / Doosan / Jaka / ABB / Fanuc. 선택 시 Euler 규약·포트 자동 설정", "UR"],
        ["Robot IP Address / Port", ["P5_enabled_09_TextBox_1921680200.png", "P5_enabled_11_TextBox_502.png"], "로봇 IP / 포트(벤더 기본값)", "192.168.0.200 / 30003"],
        ["Communication Protocol", ["P5_enabled_13_RadioButton_Vendor_Native.png", "P5_enabled_14_RadioButton_Modbus-TCP.png"], "Vendor Native / Modbus-TCP(Doosan 전용) / Custom Socket(CSV)", "Vendor Native"],
        ["〈Modbus-TCP〉 Unit ID / Pose Start Register", ["P5_enabled_18_TextBox_1.png", "P5_enabled_20_TextBox_270.png"], "Holding Register 기반 실시간 TCP 포즈 읽기(기본 270=Doosan 표준)", "1 / 270"],
        ["Euler Convention", ["P5_enabled_23_ComboBox_Doosan_ZYX.png"], "회전 표현 규약. 벤더 선택 시 자동 설정, 수동 변경 가능", "UR_RotationVector"],
      ]),
      P("※ 로봇 연동을 활성화해야 벤더·연결·프로토콜·Euler 항목이 표시되며, Modbus-TCP는 Doosan 선택 시에만 제공됩니다.", { size: 18, color: "595959" }),
    ]],
    ["10_appsetup_step6.png", 470, "6단계 — IO Board Configuration: 등록된 IO 보드(벤더·모델·채널)", [
      P("■ 6단계 입력 항목 상세", { bold: true }),
      wizardTable([
        ["[+ Add] / [− Remove]", ["P6_withboard_02_Button__Add.png", "P6_withboard_03_Button__Remove.png"], "IO 보드 추가 / 선택 보드 제거", "—"],
        ["Vendor", ["P6_withboard_06_ComboBox_AdLink.png"], "제조사(None / AdLink / Advantech)", "AdLink"],
        ["Model", ["P6_withboard_08_TextBox_PCI-7432.png"], "보드 모델(예: PCI-7432, PCI-1716)", "PCI-7432"],
        ["Device ID", ["P6_withboard_10_TextBox_IoBoard_1.png"], "시퀀스 노드에서 참조할 식별자", "IoBoard_1"],
        ["Board ID", ["P6_withboard_12_TextBox_0.png"], "보드 인덱스", "0"],
        ["Input / Output 채널 수", ["P6_withboard_14_TextBox_16.png", "P6_withboard_16_TextBox_16.png"], "디지털 입력 / 출력 채널 수", "16 / 16"],
        ["활성화", ["P6_withboard_17_CheckBox_활성화_체크_해제_시_시스템_부팅_시_인스턴스_생성_skip.png"], "해제 시 시스템 부팅 시 인스턴스 생성 skip", "사용"],
        ["설명", ["P6_withboard_19_TextBox_TextBox.png"], "보드에 대한 메모(선택 입력)", "—"],
      ]),
      P("※ IO 보드를 실제로 쓰려면 제조사 드라이버(ADLink DASK / Advantech DAQNavi)를 64비트(x64)용으로 설치해야 합니다 — VMS 는 64비트 프로그램입니다 (§7.1 트러블슈팅 참고).", { size: 18, color: "595959" }),
    ]],
    ["10_appsetup_step7.png", 470, "7단계 — Security Mode: 운영(Production)/개발(Development) 선택, [Finish] 로 저장", [
      P("마지막 단계에서 이 PC 의 보안 모드를 선택한다. 여기서 선택한 값이 설정 파일에 함께 저장되며, 보안 모드가 저장되어 있지 않으면 VMS 가 시작 시 '보안 정책 오류' 를 표시하고 실행되지 않는다. 현장(운영) PC 는 반드시 Production 을 선택한다."),
      P("■ 7단계 입력 항목 상세", { bold: true }),
      wizardTable([
        ["Production (운영 — 권장)", ["P7_default_03_RadioButton_Production_운영__권장.png"], "서버와의 통신에 HTTPS 를 강제하고 인증서를 엄격하게 검증한다. 현장에 설치하는 PC 는 반드시 이 모드를 사용", "선택됨"],
        ["Development (개발 — 사내 테스트 전용)", ["P7_default_05_RadioButton_Development_개발__사내_테스트_전용.png"], "HTTP 와 자체 서명 인증서를 허용한다. 사내 개발·테스트 환경에서만 사용하며, 운영 PC 에는 설정하지 않는다", "—"],
      ]),
      P("※ 전산 담당자가 PC 에 보안 모드 환경변수(BODA_VMS_SECURITY_MODE)를 별도로 설정해 둔 경우에는 환경변수가 이 선택보다 우선 적용됩니다.", { size: 18, color: "595959" }),
    ]],
  ];
  steps.forEach(([f, w, c, extra]) => {
    out.push(imgPara(f, w));
    out.push(caption("그림. " + c));
    (extra || []).forEach(x => out.push(x));
    out.push(new Paragraph({ spacing: { after: 120 }, children: [] }));
  });
  return out;
}

function norm(s) { return s.replace(/\s+/g, " ").trim(); }

// ---- build body ----
const body = [];
// 순서 있는 목록(<ol>)은 목록마다 1부터 다시 시작해야 하므로 그룹별 numbering 참조를 만든다.
let olGroups = 0, prevOrderedLi = false;
function numbered(text, group) {
  return new Paragraph({ numbering: { reference: "n" + group, level: 0 }, spacing: { after: 50, line: 264 },
    children: runs(text, { size: 21 }) });
}
// drop everything before H1 title
let start = blocks.findIndex(b => b.t === "h" && b.level === 1);
if (start < 0) start = 0;
for (let i = start + 1; i < blocks.length; i++) {
  const b = blocks[i];
  const isOrderedLi = b.t === "li" && !!b.ordered;
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
    if (isOrderedLi) {
      if (!prevOrderedLi) olGroups++;
      body.push(numbered(b.text, olGroups - 1));
    } else {
      body.push(bullet(b.text));
    }
  } else if (b.t === "table") {
    body.push(tableBlock(b));
    body.push(new Paragraph({ spacing: { after: 120 }, children: [] }));
  }
  prevOrderedLi = isOrderedLi;
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
  children: [new TextRun({ text: "GS 인증 제출용 · 버전 1.1 · 2026-07-28", size: 22 })] }));
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
    // 순서 목록 그룹 — 목록마다 1부터 재시작 (body 빌드에서 그룹 수 확정)
    ...Array.from({ length: olGroups }, (_, i) => ({
      reference: "n" + i,
      levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 500, hanging: 250 } } } }],
    })),
  ] },
  sections: [{
    properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      children: [new TextRun({ text: "BODA VMS 사용자 취급 설명서 v1.1", size: 16, color: "808080" })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080" }),
        new TextRun({ text: " / ", size: 16, color: "808080" }),
        new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: "808080" })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then((buf) => { fs.writeFileSync(OUT, buf); console.log("WROTE " + OUT + " (" + buf.length + " bytes)"); });
