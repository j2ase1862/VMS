// GS 제출용 사용자 매뉴얼(docx) 생성기 — v3 (2026-09-16 전면 개편)
// 입력: _manual_blocks.json (parse_manual.py 출력) — 그림·표·콜아웃이 HTML 본문 순서대로 들어 있다.
//       생성기는 본문을 "순서대로" 옮길 뿐, 절이나 그림을 따로 끼워 넣지 않는다.
// 출력: docs/gs/VMS_사용자매뉴얼_v2.0.docx
const path = require("path");
const fs = require("fs");
const GLOBAL = "C:/Users/vinos/AppData/Roaming/npm/node_modules";
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  VerticalAlign, PageNumber, PageBreak, Header, Footer, ImageRun, TableOfContents,
} = require(path.join(GLOBAL, "docx"));

const DIR = __dirname;                              // docs/gs/pipeline
const GS = path.join(DIR, "..");                    // docs/gs
const HTML = path.join(GS, "..", "manuals", "BODA-VMS-User-Manual.html");
const HTML_DIR = path.dirname(HTML);
const blocks = JSON.parse(fs.readFileSync(path.join(DIR, "_manual_blocks.json"), "utf-8"));
const OUT = path.join(GS, process.env.MANUAL_OUT || "VMS_사용자매뉴얼_v2.0.docx");

const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1300;
const CONTENT_W = PAGE_W - 2 * MARGIN;              // twips
const CONTENT_PX = Math.floor(CONTENT_W / 1440 * 96); // ≈ 620px

// 표지 메타 (HTML 헤더 스트립에서)
const rawHtml = fs.readFileSync(HTML, "utf-8");
const subM = rawHtml.match(/<div class="sub">([^<]*)<\/div>/);
const SUB = subM ? subM[1].trim() : "";
const verM = SUB.match(/버전\s*([0-9.]+)/); const dateM = SUB.match(/(\d{4}-\d{2}-\d{2})/);
const VERSION = verM ? verM[1] : "3.0"; const DATE = dateM ? dateM[1] : new Date().toISOString().slice(0, 10);

const border = { style: BorderStyle.SINGLE, size: 1, color: "BFBFBF" };
const borders = { top: border, bottom: border, left: border, right: border };
const cellMargins = { top: 40, bottom: 40, left: 90, right: 90 };
const FONT = "Malgun Gothic";
const warnings = [];
function warn(m) { warnings.push(m); console.warn("⚠ " + m); }

// ---- 인라인 세그먼트 → TextRun
function segRuns(segs, opts = {}) {
  const out = [];
  const size = opts.size || 20;
  (segs || []).forEach(seg => {
    const parts = String(seg.s).split("\n");
    parts.forEach((t, i) => {
      if (i) out.push(new TextRun({ break: 1 }));
      if (!t) return;
      out.push(new TextRun({ text: t, size: seg.m ? size - 1 : size, bold: seg.b || opts.bold, color: opts.color,
        font: seg.m ? "Consolas" : FONT, shading: seg.m ? { fill: "F2F2F2", type: ShadingType.CLEAR } : undefined }));
    });
  });
  return out;
}
function P(segs, opts = {}) {
  const children = typeof segs === "string" ? [new TextRun({ text: segs, size: opts.size || 20, bold: opts.bold, color: opts.color, font: FONT })] : segRuns(segs, opts);
  return new Paragraph({ spacing: { after: opts.after ?? 100, line: 276 }, alignment: opts.alignment, keepNext: opts.keepNext, children });
}
function bullet(segs, depth) {
  return new Paragraph({ numbering: { reference: "b", level: Math.min(depth, 2) }, spacing: { after: 40, line: 264 }, children: segRuns(segs) });
}
let olGroups = 0;
function numbered(segs, group, depth) {
  return new Paragraph({ numbering: { reference: "n" + group, level: Math.min(depth, 2) }, spacing: { after: 40, line: 264 }, children: segRuns(segs) });
}
function pngSize(buf) {
  if (buf.readUInt32BE(0) === 0x89504E47) return [buf.readUInt32BE(16), buf.readUInt32BE(20)];
  // JPEG
  let i = 2; while (i < buf.length) { if (buf[i] !== 0xFF) { i++; continue; } const m = buf[i + 1];
    if (m === 0xC0 || m === 0xC2) return [buf.readUInt16BE(i + 7), buf.readUInt16BE(i + 5)];
    i += 2 + buf.readUInt16BE(i + 2); }
  return [600, 400];
}
function resolveSrc(src) {
  const full = path.resolve(HTML_DIR, src);
  return fs.existsSync(full) ? full : null;
}
function imgRun(full, wPx) {
  const buf = fs.readFileSync(full);
  const [iw, ih] = pngSize(buf);
  const w = Math.min(wPx, iw), h = Math.round(w * ih / iw);
  const type = full.toLowerCase().endsWith(".jpg") || full.toLowerCase().endsWith(".jpeg") ? "jpg" : "png";
  return new ImageRun({ type, data: buf, transformation: { width: w, height: h },
    altText: { title: path.basename(full), description: path.basename(full), name: path.basename(full) } });
}
let figNo = 0;
function figure(b) {
  const full = resolveSrc(b.src);
  if (!full) { warn(`그림 누락: ${b.src}`); return [P(`[그림 자리: ${b.src}]`, { color: "C00000", bold: true })]; }
  const buf = fs.readFileSync(full); const [iw] = pngSize(buf);
  let w = b.width ? parseInt(b.width, 10) : Math.min(CONTENT_PX, iw);
  w = Math.min(w, CONTENT_PX);
  figNo++;
  const out = [new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 80, after: 40 }, keepNext: true, children: [imgRun(full, w)] })];
  const cap = (b.caption || "").replace(/^그림\s*[\d.]*\s*/, "");
  if (cap) out.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 160 },
    children: [new TextRun({ text: `그림 ${figNo}. ${cap}`, italics: true, size: 17, color: "595959", font: FONT })] }));
  return out;
}
function callout(b) {
  const K = { note: ["ℹ", "EAF2FB", "2E5496"], tip: ["💡", "E8F7F3", "0E8F73"], warn: ["⚠", "FFF6E0", "B26A00"], danger: ["⛔", "FDE9E7", "B71C1C"] };
  const [icon, fill, color] = K[b.kind] || K.note;
  return new Paragraph({ spacing: { before: 60, after: 140, line: 264 }, shading: { fill, type: ShadingType.CLEAR },
    indent: { left: 120, right: 120 },
    border: { left: { style: BorderStyle.SINGLE, size: 18, color, space: 6 }, top: { style: BorderStyle.SINGLE, size: 2, color: fill }, bottom: { style: BorderStyle.SINGLE, size: 2, color: fill } },
    children: [new TextRun({ text: icon + " ", size: 20, color, font: FONT }), ...segRuns(b.segs, { size: 19 })] });
}
function codeBlock(b) {
  const lines = b.text.split("\n");
  return new Paragraph({ spacing: { before: 60, after: 140 }, shading: { fill: "F2F2F2", type: ShadingType.CLEAR }, indent: { left: 200, right: 200 },
    border: { left: { style: BorderStyle.SINGLE, size: 12, color: "9E9E9E", space: 6 } },
    children: lines.flatMap((l, i) => [...(i ? [new TextRun({ break: 1 })] : []), new TextRun({ text: l, font: "Consolas", size: 17 })]) });
}
function hcell(segs, w) {
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: { fill: "1F3864", type: ShadingType.CLEAR }, verticalAlign: VerticalAlign.CENTER,
    children: [new Paragraph({ spacing: { after: 0 }, children: segRuns(segs, { size: 17, bold: true, color: "FFFFFF" }) })] });
}
function dcell(c, w, fill) {
  const children = [];
  const maxPx = Math.floor(w / 1440 * 96) - 14;
  if (c.imgs && c.imgs.length) {
    // 컨트롤 캡처(2x DPI)는 1/2 축소가 원래 크기 — 셀 폭 초과 시 셀에 맞춤. 작은 아이콘은 한 줄에 여러 개.
    let line = [], lineW = 0;
    const flush = () => { if (line.length) { children.push(new Paragraph({ spacing: { before: 20, after: 20 }, alignment: AlignmentType.CENTER, children: line })); line = []; lineW = 0; } };
    c.imgs.forEach(src => {
      const full = resolveSrc(src);
      if (!full) { warn(`셀 그림 누락: ${src}`); line.push(new TextRun({ text: `[${path.basename(src)}]`, color: "C00000", size: 15 })); return; }
      const buf = fs.readFileSync(full); const [iw] = pngSize(buf);
      const wPx = Math.min(Math.round(iw / 2), maxPx);
      if (line.length && lineW + wPx + 6 > maxPx) flush();
      if (line.length) { line.push(new TextRun({ text: " " })); lineW += 6; }
      line.push(imgRun(full, wPx)); lineW += wPx;
    });
    flush();
  }
  if ((c.text || "").trim() || !children.length)
    children.push(new Paragraph({ spacing: { after: 0, line: 252 }, children: segRuns(c.segs, { size: 17 }) }));
  return new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: fill ? { fill, type: ShadingType.CLEAR } : undefined, verticalAlign: VerticalAlign.CENTER, children });
}
function tableBlock(b) {
  const rows = b.rows;
  const ncol = Math.max(...rows.map(r => r.length));
  // 열 폭: class="cols-a-b-c" (비율) 지원, 없으면 균등. 이미지 열(imgs 있는 첫 데이터 행)은 자동 가중.
  let ratio = null;
  const m = (b.cls || "").match(/cols-([\d-]+)/);
  if (m) ratio = m[1].split("-").map(Number);
  if (!ratio) {
    ratio = Array(ncol).fill(1);
    const hasImg = Array(ncol).fill(false);
    rows.slice(1).forEach(r => r.forEach((c, i) => { if (c.imgs && c.imgs.length) hasImg[i] = true; }));
    const lens = Array(ncol).fill(0);
    rows.forEach(r => r.forEach((c, i) => { lens[i] = Math.max(lens[i], Math.min(60, (c.text || "").length)); }));
    ratio = lens.map((l, i) => hasImg[i] ? 2.2 : Math.max(1, Math.min(4, l / 12)));
  }
  const sum = ratio.reduce((a, x) => a + x, 0);
  const widths = ratio.map(x => Math.floor(CONTENT_W * x / sum)); widths[ncol - 1] += CONTENT_W - widths.reduce((a, x) => a + x, 0);
  const firstHeader = rows[0].every(c => c.header);
  const trs = rows.map((r, ri) => new TableRow({ tableHeader: ri === 0 && firstHeader, cantSplit: true, children:
    Array.from({ length: ncol }, (_, ci) => {
      const c = r[ci] || { segs: [], text: "", imgs: [] };
      if (ri === 0 && firstHeader) return hcell(c.segs, widths[ci]);
      return dcell(c, widths[ci], ri % 2 ? "F4F7FB" : undefined);
    }) }));
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: widths, rows: trs });
}

// ---- build body ----
const body = [];
let start = blocks.findIndex(b => b.t === "h" && b.level === 1);
const title = start >= 0 ? blocks[start].text : "BODA VMS 사용자 매뉴얼";
let prevOrdered = false, prevDepth = -1;
let h1Count = 0;
for (let i = (start >= 0 ? start + 1 : 0); i < blocks.length; i++) {
  const b = blocks[i];
  const isOl = b.t === "li" && !!b.ordered;
  if (b.t === "h") {
    if (b.level === 2) { h1Count++; if (h1Count > 1) body.push(new Paragraph({ children: [new PageBreak()] })); }
    const lvl = b.level === 2 ? HeadingLevel.HEADING_1 : b.level === 3 ? HeadingLevel.HEADING_2 : b.level === 4 ? HeadingLevel.HEADING_3 : HeadingLevel.HEADING_4;
    body.push(new Paragraph({ heading: lvl, keepNext: true, children: [new TextRun({ text: b.text, font: FONT })] }));
  } else if (b.t === "p") body.push(P(b.segs));
  else if (b.t === "li") {
    if (isOl) { if (!prevOrdered || b.depth < prevDepth && b.depth === 0) olGroups++; body.push(numbered(b.segs, olGroups - 1, b.depth)); }
    else body.push(bullet(b.segs, b.depth));
  }
  else if (b.t === "table") { body.push(tableBlock(b)); body.push(new Paragraph({ spacing: { after: 100 }, children: [] })); }
  else if (b.t === "figure") figure(b).forEach(x => body.push(x));
  else if (b.t === "callout") body.push(callout(b));
  else if (b.t === "code") body.push(codeBlock(b));
  prevOrdered = isOl; prevDepth = b.t === "li" ? b.depth : -1;
}

// ---- front matter ----
const front = [];
front.push(new Paragraph({ spacing: { before: 2400, after: 0 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "사용자 매뉴얼", bold: true, size: 60, color: "1F3864", font: FONT })] }));
front.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 100 },
  children: [new TextRun({ text: "(User Manual)", size: 26, color: "595959", font: FONT })] }));
front.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 240, after: 60 },
  children: [new TextRun({ text: "BODA Vision Management System", bold: true, size: 34, font: FONT })] }));
front.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 400 },
  children: [new TextRun({ text: "VMS · VMS.VisionSetup · VMS.AppSetup · BODA.VMS.Web · BODA.VMS.MLOps", size: 22, color: "595959", font: FONT })] }));
const coverImg = resolveSrc("../gs/screenshots/cover_vms_main.png") || resolveSrc("../gs/screenshots/01_vms_main.png");
if (coverImg) front.push(new Paragraph({ alignment: AlignmentType.CENTER, children: [imgRun(coverImg, 500)] }));
front.push(new Paragraph({ spacing: { before: 480 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: `문서 버전 ${VERSION} · ${DATE}`, size: 22, font: FONT })] }));
front.push(new Paragraph({ spacing: { before: 60 }, alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "BODA Vision AI", size: 22, color: "595959", font: FONT })] }));
front.push(new Paragraph({ children: [new PageBreak()] }));
front.push(new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun({ text: "목차", font: FONT })] }));
front.push(new TableOfContents("목차", { hyperlink: true, headingStyleRange: "1-2" }));
front.push(new Paragraph({ children: [new PageBreak()] }));

const children = [...front, ...body];
const doc = new Document({
  creator: "BODA Vision AI", title,
  styles: {
    default: { document: { run: { font: FONT, size: 20 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 34, bold: true, color: "1F3864", font: FONT },
        paragraph: { spacing: { before: 240, after: 200 }, outlineLevel: 0, keepNext: true,
          border: { bottom: { style: BorderStyle.SINGLE, size: 8, color: "1F3864", space: 4 } } } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 26, bold: true, color: "2E5496", font: FONT },
        paragraph: { spacing: { before: 280, after: 120 }, outlineLevel: 1, keepNext: true } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 22, bold: true, color: "404040", font: FONT },
        paragraph: { spacing: { before: 200, after: 80 }, outlineLevel: 2, keepNext: true } },
      { id: "Heading4", name: "Heading 4", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 20, bold: true, color: "0E8F73", font: FONT },
        paragraph: { spacing: { before: 140, after: 60 }, outlineLevel: 3, keepNext: true } },
    ],
  },
  numbering: { config: [
    { reference: "b", levels: [
      { level: 0, format: LevelFormat.BULLET, text: "●", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 440, hanging: 260 } }, run: { size: 12 } } },
      { level: 1, format: LevelFormat.BULLET, text: "→", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 840, hanging: 260 } } } },
      { level: 2, format: LevelFormat.BULLET, text: "·", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 1200, hanging: 220 } } } },
    ] },
    ...Array.from({ length: olGroups }, (_, i) => ({ reference: "n" + i, levels: [
      { level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 440, hanging: 300 } } } },
      { level: 1, format: LevelFormat.LOWER_LETTER, text: "%2.", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 840, hanging: 300 } } } },
      { level: 2, format: LevelFormat.BULLET, text: "·", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 1200, hanging: 220 } } } },
    ] })),
  ] },
  sections: [{
    properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      children: [new TextRun({ text: `BODA VMS 사용자 매뉴얼 v${VERSION}`, size: 16, color: "808080", font: FONT })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080" }),
        new TextRun({ text: " / ", size: 16, color: "808080" }),
        new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: "808080" })] })] }) },
    children,
  }],
});
Packer.toBuffer(doc).then((buf) => {
  fs.writeFileSync(OUT, buf);
  console.log(`WROTE ${OUT} (${(buf.length / 1048576).toFixed(1)} MB) figures=${figNo} olGroups=${olGroups} warnings=${warnings.length}`);
});
