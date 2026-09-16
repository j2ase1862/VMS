# 사용자 매뉴얼 v3 빌드 — docs/manuals/src/NN_*.html 조각을 합쳐 BODA-VMS-User-Manual.html 생성
#   python docs/manuals/build_manual.py            → HTML 합치기 + 사이드 목차 자동 생성
# 이후 docs/gs/pipeline/parse_manual.py → gen_user_manual.js 로 docx 를 만든다.
import glob, io, os, re, html

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
OUT = os.path.join(HERE, "BODA-VMS-User-Manual.html")

head = io.open(os.path.join(SRC, "00_head.html"), encoding="utf-8").read()
parts = sorted(f for f in glob.glob(os.path.join(SRC, "[0-9][0-9]_*.html")) if not f.endswith("00_head.html"))
body = "\n".join(io.open(f, encoding="utf-8").read() for f in parts)

# 사이드 목차: h2/h3 에서 생성 (id 없으면 부여)
toc = []
def ensure_id(m):
    tag, attrs, text = m.group(1), m.group(2) or "", m.group(3)
    idm = re.search(r'id="([^"]+)"', attrs)
    plain = re.sub(r"<[^>]+>", "", text).strip()
    if not idm:
        slug = re.sub(r"[^0-9A-Za-z가-힣]+", "-", plain).strip("-")[:60]
        attrs += f' id="{slug}"'; hid = slug
    else:
        hid = idm.group(1)
    toc.append((tag, hid, plain))
    return f"<{tag}{attrs}>{text}</{tag}>"
body = re.sub(r"<(h[23])([^>]*)>(.*?)</\1>", ensure_id, body, flags=re.S)

nav = ['<nav class="toc"><h2>목차</h2><ol>']
open_h2 = False
for tag, hid, text in toc:
    if tag == "h2":
        if open_h2: nav.append("</ol></li>")
        nav.append(f'<li><a href="#{hid}">{html.escape(re.sub(r"^\d+\.\s*", "", text))}</a><ol>'); open_h2 = True
    else:
        nav.append(f'<li><a href="#{hid}">{html.escape(re.sub(r"^\d+\.\d+\s*", "", text))}</a></li>')
if open_h2: nav.append("</ol></li>")
nav.append("</ol></nav>")

out = head.replace("<!--NAV-->", "\n".join(nav)).replace("<!--BODY-->", body)
io.open(OUT, "w", encoding="utf-8", newline="\n").write(out)
n_fig = len(re.findall(r"<figure", body)); n_h2 = sum(1 for t in toc if t[0] == "h2"); n_h3 = len(toc) - n_h2
print(f"WROTE {OUT}: parts={len(parts)} h2={n_h2} h3={n_h3} figures={n_fig} chars={len(out)}")
# 그림 경로 검증
missing = [s for s in re.findall(r'<img src="([^"]+)"', body) if not os.path.exists(os.path.normpath(os.path.join(HERE, s)))]
if missing: print("MISSING IMAGES:", *missing, sep="\n  ")
