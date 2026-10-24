# 매뉴얼 6장 "실행 예" — VisionSetup --capture-scenes 전체 화면(2x) PNG 를 이미지 뷰 · Run Results · Tool Settings 영역으로 잘라 JPEG 로 저장
#   사용: python crop_tool_scenes.py <캡처 폴더> [이름 ...]
#   출력: docs/gs/screenshots/v3/scenes/<이름>.jpg (가로 1500 px)
import sys, os, glob
import numpy as np
from PIL import Image
src = sys.argv[1]
names = sys.argv[2:] or sorted(os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(src, "*.png")))
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "screenshots", "v3", "scenes"))
os.makedirs(OUT, exist_ok=True)
for n in names:
    im = Image.open(os.path.join(src, n + ".png")).convert("RGB")
    W, H = im.size
    k = W / 2000                         # 아래 좌표는 화면을 가로 2000 으로 줄였을 때 기준
    # 왼쪽 레시피 · 팔레트 · 작업 공간을 빼고(x ≥ 710), 메뉴 · 툴바 아래(y ≥ 60)부터 상태바 위까지
    box = (int(710 * k), int(60 * k), W, int(1062 * k))
    im = im.crop(box)
    w = 1500
    im = im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)
    # Run Results 아래 빈 공간은 잘라 낸다 — 표 행 · 설정 패널 글자가 끝나는 곳 + 여백 (이미지 뷰는 항상 남김)
    g = np.asarray(im.convert("L"))
    rows = np.nonzero((g[:, 10:1210] > 110).sum(1) > 3)[0]
    rows_set = np.nonzero((g[:, 1235:1495] > 110).sum(1) > 3)[0]
    last = max(rows.max() if len(rows) else 0, rows_set.max() if len(rows_set) else 0)
    im = im.crop((0, 0, w, min(im.height, max(960, last + 24))))
    im.save(os.path.join(OUT, n + ".jpg"), quality=90, optimize=True)
    print(n, im.size, os.path.getsize(os.path.join(OUT, n + ".jpg")) // 1024, "KB")
