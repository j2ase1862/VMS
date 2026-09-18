# 별책(AI 학습 도구 매뉴얼) 배포본 생성 — 그림을 파일 안에 담아 한 파일로 만든다
#   python docs/manuals/build_ai_tools_manual.py
#   입력 : docs/manuals/BODA-VMS-AI-Tools-Manual.html (편집용 원본 — 그림은 ../gs/screenshots/… 상대 경로)
#   출력 : docs/gs/VMS_AI학습도구매뉴얼.html (제출용 — <img src> 를 data: URI 로 치환)
#
# 왜 필요한가: 원본은 자기 폴더 밖(docs/gs/screenshots/)의 PNG 를 상대 경로로 참조한다.
# 파일 하나만 복사해 보내거나 상위 폴더 접근이 막힌 뷰어로 열면 그림이 전부 깨진다.
# 배포본은 그림을 본문에 담아 어디서 열어도 그대로 보인다.
import base64, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "BODA-VMS-AI-Tools-Manual.html")
OUT = os.path.normpath(os.path.join(HERE, "..", "gs", "VMS_AI학습도구매뉴얼.html"))

MIME = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
        ".gif": "image/gif", ".svg": "image/svg+xml", ".webp": "image/webp"}

html = io.open(SRC, encoding="utf-8", newline="").read()
missing, inlined, total_bytes = [], 0, 0

# data-internal 이 붙은 절·목차 항목은 배포본에서 뺀다 (예: 변경 이력 — 사내 기록이라
# 시험기관·고객에게 줄 문서에는 넣지 않는다. 본편 v3.0 도 변경 이력 절을 폐지했다)
dropped = len(re.findall(r'<section[^>]*\sdata-internal[^>]*>', html))
html = re.sub(r'\s*<!--[^>]*data-internal[^>]*-->', "", html)
html = re.sub(r'\s*<section[^>]*\sdata-internal[^>]*>.*?</section>', "", html, flags=re.S)
html = re.sub(r'\s*<li[^>]*\sdata-internal[^>]*>.*?</li>', "", html, flags=re.S)


def repl(m):
    global inlined, total_bytes
    src = m.group(1)
    if src.startswith("data:"):
        return m.group(0)
    path = os.path.normpath(os.path.join(HERE, src))
    if not os.path.exists(path):
        missing.append(src)
        return m.group(0)
    ext = os.path.splitext(path)[1].lower()
    mime = MIME.get(ext)
    if mime is None:
        missing.append(src + " (지원하지 않는 형식)")
        return m.group(0)
    raw = open(path, "rb").read()
    inlined += 1
    total_bytes += len(raw)
    return 'src="data:%s;base64,%s"' % (mime, base64.b64encode(raw).decode("ascii"))


out = re.sub(r'src="([^"]+)"', repl, html)

# 배포본임을 표지에 한 줄 남긴다 (원본 파일명을 함께 적어 편집 위치를 잃지 않도록)
marker = "</header>"
note = ('<p class="dist-note" style="color:#9b9ea4;font-size:12px;margin:-18px 0 24px">'
        '그림을 파일 안에 담은 배포본입니다 — 편집은 <code>docs/manuals/BODA-VMS-AI-Tools-Manual.html</code> 에서 하고 '
        '<code>docs/manuals/build_ai_tools_manual.py</code> 로 다시 만듭니다.</p>')
assert out.count(marker) == 1, "헤더를 찾지 못했다 — 원본 구조가 바뀌었는지 확인"
out = out.replace(marker, marker + "\n" + note, 1)

io.open(OUT, "w", encoding="utf-8", newline="").write(out)

print("WROTE %s (%.1f MB) — 이미지 %d장 담음(원본 %.1f MB) · 내부 전용 절 %d개 제외"
      % (OUT, len(out.encode("utf-8")) / 1e6, inlined, total_bytes / 1e6, dropped))
if missing:
    print("MISSING:", *missing, sep="\n  ")
    sys.exit(1)
