# BODA-VMS-User-Manual.html -> 블록 JSON (docx 생성기 입력)
import json, re, html
from html.parser import HTMLParser

SRC = r"D:\Repo\VMS\docs\manuals\BODA-VMS-User-Manual.html"
OUT = r"D:\Repo\VMS\docs\gs\_manual_blocks.json"

class P(HTMLParser):
    def __init__(self):
        super().__init__()
        self.blocks = []
        self.cur = None          # current text-accumulating block
        self.skip = 0            # inside script/style
        # table state
        self.tbl = None; self.row = None; self.cell = None
        self.list_stack = []     # 'ul'/'ol'

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
        elif tag == "br":
            if self.cur is not None: self.cur["text"] += "\n"
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
            t = re.sub(r"[ \t]+", " ", self.cur["text"]).strip()
            t = re.sub(r"\n ?", "\n", t)
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
