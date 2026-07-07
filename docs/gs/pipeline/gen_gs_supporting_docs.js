// GS 부가 서류 3종 생성: OSS 라이선스 확인서 / GS 신청서 템플릿 / 신청 체크리스트
const path = require("path");
const fs = require("fs");
const GLOBAL = "C:/Users/vinos/AppData/Roaming/npm/node_modules";
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  VerticalAlign, PageNumber, Header, Footer, PageBreak,
} = require(path.join(GLOBAL, "docx"));

const DIR = __dirname;
const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1440, CONTENT_W = PAGE_W - 2 * MARGIN;
const border = { style: BorderStyle.SINGLE, size: 1, color: "BFBFBF" };
const borders = { top: border, bottom: border, left: border, right: border };
const cm = { top: 55, bottom: 55, left: 100, right: 100 };

const H1 = (t) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(t)] });
const H2 = (t) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(t)] });
const P = (t, o = {}) => new Paragraph({ spacing: { after: 110, line: 276 }, alignment: o.alignment,
  children: [new TextRun({ text: t, bold: o.bold, italics: o.italics, color: o.color, size: o.size || 21 })] });
const bullet = (t) => new Paragraph({ numbering: { reference: "b", level: 0 }, spacing: { after: 60, line: 268 }, children: [new TextRun({ text: t, size: 21 })] });
const check = (t) => new Paragraph({ numbering: { reference: "c", level: 0 }, spacing: { after: 70, line: 276 }, children: [new TextRun({ text: t, size: 21 })] });
const hc = (t, w) => new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cm, shading: { fill: "1F3864", type: ShadingType.CLEAR }, verticalAlign: VerticalAlign.CENTER, children: [new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text: t, bold: true, color: "FFFFFF", size: 18 })] })] });
const dc = (t, w, fill) => new TableCell({ borders, width: { size: w, type: WidthType.DXA }, margins: cm, shading: fill ? { fill, type: ShadingType.CLEAR } : undefined, verticalAlign: VerticalAlign.CENTER, children: [new Paragraph({ spacing: { after: 0, line: 248 }, children: [new TextRun({ text: String(t), size: 18 })] })] });
function table(headers, rows, widths) {
  const head = new TableRow({ tableHeader: true, children: headers.map((h, i) => hc(h, widths[i])) });
  const trs = rows.map((r, ri) => new TableRow({ children: r.map((c, ci) => dc(c, widths[ci], ri % 2 ? "F4F7FB" : undefined)) }));
  return new Table({ width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: widths, rows: [head, ...trs] });
}
function titlePage(title, subtitle) {
  return [
    new Paragraph({ spacing: { before: 2800, after: 0 }, alignment: AlignmentType.CENTER, children: [new TextRun({ text: title, bold: true, size: 52, color: "1F3864" })] }),
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 160, after: 60 }, children: [new TextRun({ text: subtitle, size: 26, color: "595959" })] }),
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 320, after: 0 }, children: [new TextRun({ text: "BODA Vision Management System (VMS)", bold: true, size: 28 })] }),
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 600 }, children: [new TextRun({ text: "GS 인증 제출용 · 버전 1.0 · 2026-06-12", size: 22 })] }),
    new Paragraph({ children: [new PageBreak()] }),
  ];
}
function makeDoc(headerText, children) {
  return new Document({
    creator: "BODA VMS", title: headerText,
    styles: {
      default: { document: { run: { font: "Malgun Gothic", size: 21 } } },
      paragraphStyles: [
        { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true, run: { size: 28, bold: true, color: "1F3864", font: "Malgun Gothic" }, paragraph: { spacing: { before: 280, after: 150 }, outlineLevel: 0 } },
        { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true, run: { size: 24, bold: true, color: "2E5496", font: "Malgun Gothic" }, paragraph: { spacing: { before: 200, after: 100 }, outlineLevel: 1 } },
      ],
    },
    numbering: { config: [
      { reference: "b", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 500, hanging: 250 } } } }] },
      { reference: "c", levels: [{ level: 0, format: LevelFormat.BULLET, text: "☐", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 460, hanging: 280 } } } }] },
    ] },
    sections: [{
      properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN } } },
      headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [new TextRun({ text: headerText, size: 16, color: "808080" })] })] }) },
      footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080" }), new TextRun({ text: " / ", size: 16, color: "808080" }), new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: "808080" })] })] }) },
      children,
    }],
  });
}
function write(name, doc) { return Packer.toBuffer(doc).then((b) => { fs.writeFileSync(path.join(DIR, "..", name), b); console.log("WROTE " + name + " (" + b.length + ")"); }); }

// ===================== 1) OSS 라이선스 확인서 =====================
const dist = [
  ["OpenCvSharp4 (+ Extensions, WpfExtensions, runtime.win)", "4.11.0", "Apache-2.0", "영상처리(2D/3D)", "라이선스 전문 + NOTICE 동봉, 변경 시 고지"],
  ["CommunityToolkit.Mvvm", "8.4.0", "MIT", "MVVM 인프라", "라이선스 전문 + 저작권 고지 보존"],
  ["HelixToolkit.Wpf.SharpDX", "3.1.2", "MIT", "3D 포인트클라우드 시각화", "라이선스 전문 + 저작권 고지 보존"],
  ["Microsoft.ML.OnnxRuntime (.Gpu)", "1.21.0", "MIT", "딥러닝 추론", "라이선스 전문 + 저작권 고지 보존"],
  ["Tesseract", "5.2.0", "Apache-2.0", "OCR 문자 인식", "라이선스 전문 + NOTICE 동봉"],
  ["ZXing.Net", "0.16.11", "Apache-2.0", "바코드/QR 판독", "라이선스 전문 + NOTICE 동봉"],
  ["Microsoft.AspNetCore.SignalR.Client", "8.0.0", "MIT", "Web 실시간 연동", "라이선스 전문 + 저작권 고지 보존"],
  ["Microsoft.Data.Sqlite", "8.0.0", "MIT", "로컬 데이터 저장", "MIT 고지(SQLite 엔진은 Public Domain)"],
  ["System.Drawing.Common", "8.0.11", "MIT", "이미지 유틸리티", "라이선스 전문 + 저작권 고지 보존"],
  ["BCrypt.Net-Next", "4.0.3", "MIT", "비밀번호 해시", "라이선스 전문 + 저작권 고지 보존"],
];
const dev = [
  ["Microsoft.NET.Test.Sdk", "17.11.1", "MIT", "테스트 SDK"],
  ["xunit", "2.9.0", "Apache-2.0", "단위 테스트 프레임워크"],
  ["xunit.runner.visualstudio", "2.8.2", "Apache-2.0 / MIT", "테스트 러너"],
];
const oss = [
  ...titlePage("오픈소스 라이선스 확인서", "(OSS License Compliance)"),
  H1("1. 개요"),
  P("본 문서는 BODA Vision Management System(VMS)의 개발·배포에 사용된 오픈소스/서드파티 라이브러리와 각 라이선스, 고지 의무 준수 사항을 명시한다. 모든 라이브러리는 NuGet 패키지로 관리되며, 버전은 프로젝트(.csproj) 기준이다."),
  H1("2. 배포 구성요소 라이브러리 (런타임)"),
  P("제품과 함께 배포되는 런타임 의존성이다."),
  table(["라이브러리", "버전", "라이선스", "용도", "고지 의무 준수"],
    dist.map(r => r), [2750, 900, 1200, 1700, CONTENT_W - 2750 - 900 - 1200 - 1700]),
  H1("3. 개발/테스트 전용 (비배포)"),
  P("빌드/테스트에만 사용되며 최종 제품에 포함·배포되지 않는다."),
  table(["라이브러리", "버전", "라이선스", "용도"],
    dev.map(r => r), [3200, 1100, 1900, CONTENT_W - 3200 - 1100 - 1900]),
  H1("4. 라이선스별 고지 의무 요약"),
  bullet("MIT: 라이선스 전문과 저작권 고지를 배포물(또는 about/third-party 고지)에 포함하면 됨. 상표·보증 책임 면책."),
  bullet("Apache-2.0: 라이선스 전문 포함 + 배포물에 NOTICE 파일이 있으면 그 내용을 전달. 변경(수정) 시 변경 사실 고지. 특허 라이선스 조항 포함."),
  bullet("Public Domain(SQLite 엔진): 별도 고지 의무 없음(자유 사용)."),
  P("준수 방법: 설치 디렉토리 또는 프로그램 내 '오픈소스 고지(Third-Party Notices)'에 위 라이브러리들의 라이선스 전문/NOTICE를 일괄 수록한다."),
  H1("5. 준수 확인"),
  check("배포물에 Third-Party Notices(또는 LICENSES 폴더) 포함"),
  check("Apache-2.0 라이브러리(OpenCvSharp4, Tesseract, ZXing.Net)의 NOTICE 내용 전달"),
  check("각 라이선스 전문 및 저작권 고지 보존"),
  check("라이브러리 수정(소스 변경) 없음 — 변경 시 변경 고지 추가"),
  P("※ 제출 전 각 패키지의 공식 LICENSE/NOTICE 원문을 최종 확인할 것. 본 표의 라이선스는 각 패키지 배포 시점의 공표 라이선스를 기준으로 정리하였다.", { italics: true, size: 18, color: "C00000" }),
];

// ===================== 2) GS 신청서 템플릿 =====================
const tpl = (label) => new Paragraph({ spacing: { after: 90 }, children: [
  new TextRun({ text: label + ": ", bold: true, size: 21 }),
  new TextRun({ text: "[ 기입 ]", size: 21, color: "C00000" }),
] });
const appl = [
  ...titlePage("GS 인증 신청서 (템플릿)", "(Application Form — Template)"),
  P("※ 본 문서는 인증기관(TTA/KTL 등) 공식 양식 작성을 돕기 위한 템플릿이다. 실제 신청은 인증기관 제공 양식에 아래 정보를 옮겨 기입한다. 대괄호 [ ] 항목을 채울 것.", { italics: true, color: "595959", size: 19 }),
  H1("1. 신청인 정보"),
  tpl("회사명(상호)"), tpl("대표자"), tpl("사업자등록번호"), tpl("주소"),
  tpl("담당자 성명/직위"), tpl("연락처(전화/이메일)"),
  H1("2. 제품 정보"),
  tpl("제품명"), tpl("제품 버전"),
  new Paragraph({ spacing: { after: 90 }, children: [new TextRun({ text: "제품 분류: ", bold: true }), new TextRun("산업용 머신비전 검사 소프트웨어 (응용 소프트웨어)")] }),
  new Paragraph({ spacing: { after: 90 }, children: [new TextRun({ text: "구동 환경: ", bold: true }), new TextRun("Windows 10/11 x64, .NET 8.0 (상세는 제품설명서 §4)")] }),
  tpl("형상관리 버전/빌드"),
  H1("3. 시험 범위"),
  P("제품설명서의 기능명세(Function List)에 기재된 기능 전체. 카메라 미연결 환경에서는 파일 불러오기(가상) 모드로 검사 기능을 시연한다."),
  H1("4. 제출물 목록"),
  bullet("GS 인증 신청서 (본 문서)"),
  bullet("제품설명서 (ISO/IEC 25051 기준, 기능명세 포함)"),
  bullet("사용자 취급 설명서(매뉴얼) — 설치/환경설정/UI 조작법(스크린샷 포함)"),
  bullet("실행 소프트웨어(설치 파일) + 테스트 데이터(샘플 이미지/설정)"),
  bullet("오픈소스 라이선스 확인서"),
  bullet("사업자등록증 사본"),
  H1("5. 확인 / 서명"),
  P("상기 기재 내용 및 제출물이 사실과 같음을 확인합니다."),
  new Paragraph({ spacing: { before: 240 }, children: [new TextRun({ text: "신청일: ", bold: true }), new TextRun({ text: "20  .   .   .", size: 21 })] }),
  new Paragraph({ spacing: { before: 120 }, children: [new TextRun({ text: "신청인(서명/인): ", bold: true }), new TextRun({ text: "________________", size: 21 })] }),
];

// ===================== 3) 신청 체크리스트 =====================
const chk = [
  ...titlePage("GS 인증 신청 정보 체크리스트", "(Submission Checklist)"),
  P("신청 전 누락 항목을 자체 점검하는 요약서다. 각 항목을 확인 후 체크한다."),
  H1("1. 필수 제출 서류"),
  check("GS 인증 신청서 (신청인/제품/버전 기입 완료)"),
  check("제품설명서 (개요·동작환경·기능명세 Function List 포함)"),
  check("사용자 취급 설명서(매뉴얼) — 설치/환경설정/UI 화면별 조작법 + 스크린샷"),
  H1("2. 소프트웨어 및 구동 환경 (실물)"),
  check("실행 소프트웨어 제품 (설치 미디어/파일)"),
  check("테스트 데이터 (샘플 이미지, 설정 파일)"),
  check("카메라 미보유 시연 대비 — 파일 불러오기(가상/에뮬레이터) 모드 포함"),
  check("필요 시 테스트용 PC(하드웨어 일체) 대여 제출 여부 결정"),
  H1("3. 부가/선택 서류"),
  check("오픈소스 라이선스 확인서 (OpenCvSharp4 등)"),
  check("사업자등록증 사본"),
  check("품질인증 신청정보 체크리스트 (본 문서)"),
  H1("4. 품질 사전 점검 (보완요청 예방)"),
  check("매뉴얼의 버튼/메뉴 명칭이 실제 UI 텍스트와 100% 일치 (예: AUTO RUN, Work Orders, Image Save Settings)"),
  check("기능명세에 미구현 기능 없음 — 현재 동작 확인된 기능만 기재"),
  check("예외 처리 일치 — 카메라 연결 해제/비정상 입력 시 안내(알럿/로그) 정상 동작"),
  check("제품 버전이 신청서·제품설명서·실행 파일에서 동일"),
  check("Third-Party Notices(오픈소스 고지) 배포물 포함"),
];

Promise.all([
  write("VMS_OSS_라이선스_확인서_v1.0.docx", makeDoc("VMS OSS 라이선스 확인서 v1.0", oss)),
  write("VMS_GS_신청서_템플릿_v1.0.docx", makeDoc("VMS GS 신청서(템플릿) v1.0", appl)),
  write("VMS_GS_신청_체크리스트_v1.0.docx", makeDoc("VMS GS 신청 체크리스트 v1.0", chk)),
]).then(() => console.log("done"));
