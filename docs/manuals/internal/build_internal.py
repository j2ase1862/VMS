# 사내판 매뉴얼 빌드 — internal/src/NN_*.html 을 합쳐 internal/BODA-VMS-Internal-Manual.html 을 만든다.
#   python docs/manuals/internal/build_internal.py            → 그림은 상대 경로 (리포 안에서 열기)
#   python docs/manuals/internal/build_internal.py --embed <출력.html>  → 그림을 품은 한 파일 (배포용)
#
# 제출본(docs/manuals/src · build_manual.py · docs/gs)은 GS 동결 대상이라 건드리지 않는다. 이 폴더만 쓴다.
# 하는 일
#   - 사이드 목차, h2/h3 id 부여
#   - 그림 경로: 제출본 조각의 "../gs/…" 는 이 폴더 기준 "../../gs/…" 로, 사내판 그림은 "figs/…"
#   - "§3.5" 같은 절 참조를 그 절로 가는 링크로 (찾지 못한 참조는 목록으로 알림)
#   - videos.json 의 {절 id: 영상 번호} 또는 제목의 data-video 로 영상 배지
#   - ◆(확장 옵션) 표시에 설명 풍선
#   - 없는 그림 · 같은 id · 찾지 못한 참조 점검
import base64, glob, html, io, json, mimetypes, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
OUT = os.path.join(HERE, "BODA-VMS-Internal-Manual.html")
embed_out = sys.argv[sys.argv.index("--embed") + 1] if "--embed" in sys.argv else None

head = io.open(os.path.join(SRC, "00_head.html"), encoding="utf-8").read()
parts = sorted(f for f in glob.glob(os.path.join(SRC, "[0-9][0-9]_*.html")) if not f.endswith("00_head.html"))
body = "\n".join(io.open(f, encoding="utf-8").read() for f in parts)

gl = os.path.join(HERE, "glossary.html")
if os.path.exists(gl): body = body.replace("<!--GLOSSARY-->", io.open(gl, encoding="utf-8").read())

# 그림 경로
body = body.replace('src="../gs/', 'src="../../gs/')

videos = json.load(io.open(os.path.join(HERE, "videos.json"), encoding="utf-8")) if os.path.exists(os.path.join(HERE, "videos.json")) else {}

toc, ids, nums = [], [], {}
def heading(m):
    tag, attrs, text = m.group(1), m.group(2) or "", m.group(3)
    plain = html.unescape(re.sub(r"<[^>]+>", "", text)).strip()
    idm = re.search(r'id="([^"]+)"', attrs)
    if idm: hid = idm.group(1)
    else:
        hid = re.sub(r"[^0-9A-Za-z가-힣]+", "-", plain).strip("-")[:60]; attrs += f' id="{hid}"'
    ids.append(hid)
    nm = re.match(r"^(?:부록\s*)?(\d+(?:\.\d+)*)\.?\s", plain)
    if nm: nums.setdefault(nm.group(1), hid)
    vid = (re.search(r'data-video="([^"]+)"', attrs) or [None, None])[1] or videos.get(hid)
    badge = f' <span class="video" title="이 절을 따라 하는 사용 영상">▶ 영상 {" · ".join(vid) if isinstance(vid, list) else vid}</span>' if vid else ""
    toc.append((tag, hid, plain, vid))
    return f"<{tag}{attrs}>{text}{badge}</{tag}>"
body = re.sub(r"<(h[23])([^>]*)>(.*?)</\1>", heading, body, flags=re.S)

# h4 영상 배지 — videos.json 의 "h4:제목 앞부분" 키, 또는 6장(도구) h4 는 차례대로 V-6-n
def vlabel(v): return " · ".join(v) if isinstance(v, list) else v
toc_h4, tool_n = [], [0]
def h4(m):
    attrs, text = m.group(1) or "", m.group(2)
    plain = re.sub(r"<[^>]+>", "", text).replace("◆", "").strip()
    vid = next((v for k, v in videos.items() if k.startswith("h4:") and plain.startswith(k[3:])), None)
    in_tools = m.string.rfind('<section id="tools">', 0, m.start()) > m.string.rfind("</section>", 0, m.start())
    if vid is None and in_tools:
        tool_n[0] += 1; vid = f"V-6-{tool_n[0]}"
    if not vid: return m.group(0)
    toc_h4.append(vid)
    return f'<h4{attrs}>{text} <span class="video" title="이 기능을 따라 하는 사용 영상">▶ 영상 {vlabel(vid)}</span></h4>'
body = re.sub(r"<h4([^>]*)>(.*?)</h4>", h4, body, flags=re.S)

# § 참조 → 링크 (태그 · code · 이미 링크인 곳은 건너뜀)
unresolved = {}
def link_refs(seg):
    def rep(m):
        n = m.group(1).rstrip(".")
        hid = nums.get(n)
        if not hid:
            unresolved[n] = unresolved.get(n, 0) + 1
            return m.group(0)
        return f'<a class="ref" href="#{hid}">§{n}</a>'
    return re.sub(r"§(\d+(?:\.\d+)*)", rep, seg)
out, in_code, in_a = [], 0, 0
for tok in re.split(r"(<[^>]+>)", body):
    if tok.startswith("<"):
        t = tok.lower()
        if t.startswith("<code") or t.startswith("<pre"): in_code += 1
        elif t.startswith("</code") or t.startswith("</pre"): in_code = max(0, in_code - 1)
        elif t.startswith("<a "): in_a += 1
        elif t.startswith("</a"): in_a = max(0, in_a - 1)
        out.append(tok)
    else:
        out.append(tok if (in_code or in_a) else link_refs(tok))
body = "".join(out)

# ◆ — 확장 옵션 표시
body = re.sub(r"◆(?![^<]*>)", '<span class="opt" title="확장 옵션 — 라이선스에 이 옵션이 있어야 보입니다">◆</span>', body)

nav = ['<nav class="toc"><h2>목차</h2><ol>']
open_h2 = False
for tag, hid, text, vid in toc:
    v = ' <span class="v">▶</span>' if vid else ""
    if tag == "h2":
        if open_h2: nav.append("</ol></li>")
        nav.append(f'<li><a href="#{hid}">{html.escape(re.sub(r"^\d+\.\s*", "", text))}</a><ol>'); open_h2 = True
    else:
        nav.append(f'<li><a href="#{hid}">{html.escape(re.sub(r"^\d+(\.\d+)+\s*", "", text))}{v}</a></li>')
if open_h2: nav.append("</ol></li>")
nav.append("</ol></nav>")

page = head.replace("<!--NAV-->", "\n".join(nav)).replace("<!--BODY-->", body)
io.open(OUT, "w", encoding="utf-8", newline="\n").write(page)

imgs = re.findall(r'<img src="([^"]+)"', body)
missing = sorted({s for s in imgs if not s.startswith("data:") and not os.path.exists(os.path.normpath(os.path.join(HERE, s)))})
dups = sorted({i for i in ids if ids.count(i) > 1})
n_h2 = sum(1 for t in toc if t[0] == "h2")
print(f"WROTE {OUT}: parts={len(parts)} h2={n_h2} h3={len(toc) - n_h2} figures={len(re.findall(r'<figure', body))} "
      f"video-badges={sum(1 for t in toc if t[3]) + len(toc_h4)} refs={body.count('class=\"ref\"')}")
if missing: print(f"MISSING IMAGES ({len(missing)}):", *missing, sep="\n  ")
if dups: print("DUPLICATE IDS:", dups)
if unresolved: print("UNRESOLVED § REFS:", ", ".join(f"§{k}×{v}" for k, v in sorted(unresolved.items())))

if embed_out:
    def emb(m):
        p = os.path.normpath(os.path.join(HERE, m.group(1)))
        if not os.path.exists(p): return m.group(0)
        mt = mimetypes.guess_type(p)[0] or "image/png"
        return f'<img src="data:{mt};base64,{base64.b64encode(open(p, "rb").read()).decode()}"'
    io.open(embed_out, "w", encoding="utf-8", newline="\n").write(re.sub(r'<img src="([^"]+)"', emb, page))
    print("EMBED", embed_out, f"{os.path.getsize(embed_out) / 1e6:.1f} MB")
