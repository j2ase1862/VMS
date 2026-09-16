# BODA-VMS-User-Manual.html -> 블록 JSON (docx 생성기 입력)
#
# v3 (2026-09-16): 매뉴얼 전면 개편에 맞춰 재작성.
#  - <figure><img src><figcaption> → figure 블록 (그림은 HTML 본문 안에 직접 둔다 — 생성기 앵커 주입 없음)
#  - 표 셀 안의 <img> → 셀 이미지 (컨트롤 캡처 표)
#  - <strong>/<b>/<code> 인라인 서식 유지 (segments)
#  - <div class="callout [tip|warn|danger|note]"> → callout 블록
#  - <pre> → code 블록, <ol>/<ul> 중첩 깊이 유지
import json, re, html, sys, os
from html.parser import HTMLParser

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", "..", "manuals", "BODA-VMS-User-Manual.html"))
OUT = os.path.join(HERE, "_manual_blocks.json")

class P(HTMLParser):
    def __init__(self):
        super().__init__()
        self.blocks = []
        self.cur = None            # current text-accumulating block {t, segs:[{s,b,m}]}
        self.skip = 0              # inside script/style/nav
        self.bold = 0; self.mono = 0
        self.tbl = None; self.row = None; self.cell = None
        self.list_stack = []       # 'ul'/'ol'
        self.div_stack = []        # callout kind or None
        self.fig = None            # current figure {src, width, caption}
        self.in_caption = False
        self.in_pre = False
        self.in_main = False

    # ---- helpers
    def _target(self):
        if self.in_caption and self.fig is not None: return self.fig["capsegs"]
        if self.cell is not None: return self.cell["segs"]
        if self.cur is not None: return self.cur["segs"]
        return None
    def _add_text(self, data):
        segs = self._target()
        if segs is None: return
        segs.append({"s": data, "b": self.bold > 0, "m": self.mono > 0 or self.in_pre})
    def _new(self, t, **kw):
        self.flush(); self.cur = {"t": t, "segs": []}; self.cur.update(kw)

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag in ("script", "style", "nav"): self.skip += 1; return
        if tag == "main": self.in_main = True
        if self.skip: return
        if tag in ("h1", "h2", "h3", "h4", "h5"):
            self._new("h", level=int(tag[1]))
        elif tag == "p":
            self._new("p")
        elif tag == "pre":
            self._new("code"); self.in_pre = True
        elif tag in ("ul", "ol"):
            self.flush(); self.list_stack.append(tag)
        elif tag == "li":
            self._new("li", ordered=(self.list_stack[-1] == "ol" if self.list_stack else False),
                      depth=max(0, len(self.list_stack) - 1))
        elif tag == "table":
            self.flush(); self.tbl = {"t": "table", "rows": [], "cls": a.get("class", "")}
        elif tag == "tr":
            if self.tbl is not None: self.row = []
        elif tag in ("td", "th"):
            if self.tbl is not None: self.cell = {"segs": [], "header": tag == "th", "imgs": []}
        elif tag == "figure":
            self.flush(); self.fig = {"t": "figure", "src": "", "width": a.get("data-width"), "capsegs": []}
        elif tag == "img":
            src = a.get("src", "")
            if self.fig is not None and not self.fig["src"]:
                self.fig["src"] = src
                if a.get("data-width"): self.fig["width"] = a.get("data-width")
            elif self.cell is not None:
                self.cell["imgs"].append(src)
        elif tag == "figcaption":
            self.in_caption = True
        elif tag == "div":
            cls = a.get("class", "")
            kind = None
            if "callout" in cls:
                kind = "tip" if "tip" in cls else "warn" if "warn" in cls else "danger" if "danger" in cls else "note"
                self.flush(); self.cur = {"t": "callout", "kind": kind, "segs": []}
            self.div_stack.append(kind)
        elif tag in ("strong", "b"):
            self.bold += 1
        elif tag == "code":
            self.mono += 1
        elif tag == "br":
            self._add_text("\x00")
        elif tag == "span" and "chip" in a.get("class", ""):
            self._add_text("[")
    def handle_endtag(self, tag):
        if tag in ("script", "style", "nav"):
            if self.skip: self.skip -= 1
            return
        if self.skip: return
        if tag in ("h1", "h2", "h3", "h4", "h5", "p", "li"):
            self.flush()
        elif tag == "pre":
            self.in_pre = False; self.flush()
        elif tag in ("ul", "ol"):
            self.flush()
            if self.list_stack: self.list_stack.pop()
            # callout 안에서 리스트 뒤 텍스트도 문단으로
            if self.div_stack and self.div_stack[-1]:
                self.cur = {"t": "callout", "kind": self.div_stack[-1], "segs": []}
        elif tag == "div":
            if self.div_stack and self.div_stack.pop():
                self.flush()
        elif tag in ("td", "th"):
            if self.cell is not None and self.row is not None:
                self.row.append(self.cell); self.cell = None
        elif tag == "tr":
            if self.row is not None and self.tbl is not None:
                if any(_txt(c["segs"]).strip() or c["imgs"] for c in self.row): self.tbl["rows"].append(self.row)
                self.row = None
        elif tag == "table":
            if self.tbl is not None and self.tbl["rows"]: self.blocks.append(self.tbl)
            self.tbl = None
        elif tag == "figcaption":
            self.in_caption = False
        elif tag == "figure":
            if self.fig is not None:
                self.fig["caption"] = _clean(_txt(self.fig["capsegs"]))
                del self.fig["capsegs"]
                self.blocks.append(self.fig)
            self.fig = None
        elif tag in ("strong", "b"):
            self.bold = max(0, self.bold - 1)
        elif tag == "code":
            self.mono = max(0, self.mono - 1)
        elif tag == "span":
            pass
    def handle_data(self, data):
        if self.skip: return
        self._add_text(data)
    def flush(self):
        if self.cur is not None:
            segs = _merge(self.cur["segs"], pre=self.cur["t"] == "code")
            if _txt(segs).strip():
                self.cur["segs"] = segs; self.cur["text"] = _txt(segs); self.blocks.append(self.cur)
            self.cur = None

def _txt(segs): return "".join(s["s"] for s in segs)
def _clean(t):
    t = re.sub(r"\s*\n\s*", " ", t); t = re.sub(r"[ \t]+", " ", t).strip()
    return re.sub(r" ?\x00 ?", "\n", t)
def _merge(segs, pre=False):
    out = []
    for s in segs:
        txt = html.unescape(s["s"])
        if not pre:
            txt = re.sub(r"\s*\n\s*", " ", txt); txt = re.sub(r"[ \t]+", " ", txt)
            txt = re.sub(r" ?\x00 ?", "\n", txt)
        else:
            txt = txt.replace("\x00", "")
        if not txt: continue
        if out and out[-1]["b"] == s["b"] and out[-1]["m"] == s["m"]:
            out[-1]["s"] += txt
        else:
            out.append({"s": txt, "b": s["b"], "m": s["m"]})
    if out:
        out[0]["s"] = out[0]["s"].lstrip() if not pre else out[0]["s"].lstrip("\n")
        out[-1]["s"] = out[-1]["s"].rstrip() if not pre else out[-1]["s"].rstrip("\n")
    return [s for s in out if s["s"]]

raw = open(SRC, encoding="utf-8").read()
p = P(); p.feed(raw); p.flush()
for b in p.blocks:
    if b["t"] == "table":
        for row in b["rows"]:
            for c in row:
                c["segs"] = _merge(c["segs"]); c["text"] = _txt(c["segs"])
json.dump(p.blocks, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
from collections import Counter
print("blocks:", len(p.blocks), "->", OUT, dict(Counter(b["t"] for b in p.blocks)))
