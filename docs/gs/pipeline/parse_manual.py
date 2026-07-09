# BODA-VMS-User-Manual.html -> 블록 JSON (docx 생성기 입력)
import json, re, html
from html.parser import HTMLParser

SRC = r"D:\Repo\VMS\docs\manuals\BODA-VMS-User-Manual.html"
OUT = r"D:\Repo\VMS\docs\gs\pipeline\_manual_blocks.json"

class P(HTMLParser):
    def __init__(self):
        super().__init__()
        self.blocks = []
        self.cur = None          # current text-accumulating block
        self.skip = 0            # inside script/style
        # table state
        self.tbl = None; self.row = None; self.cell = None
        self.list_stack = []     # 'ul'/'ol'
        self.div_stack = []      # True = callout div (직접 텍스트를 문단으로 승격)

    def handle_starttag(self, tag, attrs):
        if tag in ("script", "style"): self.skip += 1; return
        if self.skip: return
        if tag in ("h1","h2","h3","h4"):
            self.flush(); self.cur = {"t":"h","level":int(tag[1]),"text":""}
        elif tag == "p":
            self.flush(); self.cur = {"t":"p","text":""}
        elif tag in ("ul","ol"):
            self.flush(); self.list_stack.append(tag)
        elif tag == "li":
            self.flush(); self.cur = {"t":"li","text":"","ordered": (self.list_stack[-1]=="ol" if self.list_stack else False)}
        elif tag == "table":
            self.flush(); self.tbl = {"t":"table","rows":[]}
        elif tag == "tr":
            if self.tbl is not None: self.row = []
        elif tag in ("td","th"):
            if self.tbl is not None: self.cell = {"text":"","header": tag=="th"}
        elif tag == "div":
            # callout 박스는 <p> 없이 직접 텍스트를 담는 경우가 많다 — 문단 블록으로 승격.
            # (그 외 div 의 직접 텍스트는 기존대로 무시: 표지 sub 등)
            is_callout = "callout" in dict(attrs).get("class", "")
            self.div_stack.append(is_callout)
            if is_callout:
                self.flush(); self.cur = {"t": "p", "text": ""}
        elif tag == "br":
            # 의도된 줄바꿈은 <br> 만 — HTML 소스의 raw 개행(코드 정리용 줄 감싸기)과
            # 구분하기 위해 sentinel 로 표시하고 flush 에서 \n 으로 복원한다.
            if self.cur is not None: self.cur["text"] += "\x00"
            elif self.cell is not None: self.cell["text"] += " "

    def handle_endtag(self, tag):
        if tag in ("script","style"):
            if self.skip: self.skip -= 1
            return
        if self.skip: return
        if tag in ("h1","h2","h3","h4","p","li"):
            self.flush()
        elif tag in ("ul","ol"):
            if self.list_stack: self.list_stack.pop()
            # callout div 안에서 리스트 뒤에 이어지는 직접 텍스트도 문단으로.
            if any(self.div_stack):
                self.flush(); self.cur = {"t": "p", "text": ""}
        elif tag == "div":
            if self.div_stack and self.div_stack.pop():
                self.flush()
        elif tag in ("td","th"):
            if self.cell is not None and self.row is not None:
                self.row.append(self.cell); self.cell = None
        elif tag == "tr":
            if self.row is not None and self.tbl is not None:
                if any(c["text"].strip() for c in self.row): self.tbl["rows"].append(self.row)
                self.row = None
        elif tag == "table":
            if self.tbl is not None and self.tbl["rows"]: self.blocks.append(self.tbl)
            self.tbl = None

    def handle_data(self, data):
        if self.skip: return
        if self.cell is not None: self.cell["text"] += data
        elif self.cur is not None: self.cur["text"] += data

    def flush(self):
        if self.cur is not None:
            # HTML 소스의 raw 개행은 렌더링과 동일하게 공백으로 접는다 — 그대로 두면
            # docx 생성기(runs)가 \n 마다 강제 줄바꿈을 넣어 문장 중간이 끊긴다.
            t = re.sub(r"\s*\n\s*", " ", self.cur["text"])
            t = re.sub(r"[ \t]+", " ", t).strip()
            t = re.sub(r" ?\x00 ?", "\n", t)   # <br> sentinel 만 진짜 줄바꿈으로
            if t:
                self.cur["text"] = t; self.blocks.append(self.cur)
            self.cur = None

raw = open(SRC, encoding="utf-8").read()
p = P(); p.feed(raw); p.flush()
# unescape + clean cell text
for b in p.blocks:
    if b["t"] == "table":
        for row in b["rows"]:
            for c in row:
                c["text"] = re.sub(r"[ \t]+"," ", html.unescape(c["text"])).strip()
    else:
        b["text"] = html.unescape(b["text"])
json.dump(p.blocks, open(OUT,"w",encoding="utf-8"), ensure_ascii=False, indent=1)
print("blocks:", len(p.blocks), "->", OUT)
from collections import Counter
print(Counter(b["t"] for b in p.blocks))
