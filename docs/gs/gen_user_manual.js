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
  "3.3 사이드 패널 (Settings)": [
    () => imgPara("05_vms_sidepanel.png", 600),
    () => caption("그림. 사이드 패널(Settings) 펼친 상태 — Camera Control / Recipe / External Tools / Updates / Web Parameters / Image Saving"),
    () => P("외부 도구의 [Vision Tool Setup] 버튼으로 실행되는 비전 설정(VMS.VisionSetup) 화면은 다음과 같다.", {}),
    () => imgPara("03_visionsetup.png", 600),
    () => caption("그림. 비전 설정(VMS.VisionSetup) — 카메라 / Steps / Tool Palette(12 카테고리) / Tool Workspace / ROI 도구 / 이미지 뷰"),
  ],
  "4. BODA.VMS.Web (관리자 / MES)": [
    () => placeholder("BODA.VMS.Web 관리 화면(Dashboard / Production History / Work Orders / Alarms / Audit Logs 등) 스크린샷은 Web 서버 실행 환경에서 캡처하여 삽입 예정."),
  ],
};
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
