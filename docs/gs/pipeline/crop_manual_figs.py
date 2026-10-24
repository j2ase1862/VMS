# 매뉴얼 4 · 5 장 그림 — --capture-inspect(VMS) · --capture-scenes(VisionSetup) 전체 화면 PNG 에서 영역을 잘라 JPEG 로 저장
#   사용: python crop_manual_figs.py <VMS 캡처 폴더> <VisionSetup 캡처 폴더>
#   좌표는 화면을 가로 2000 으로 줄였을 때 기준 (x0, y0, x1, y1). 여러 상자를 주면 가로로 이어 붙인다.
import sys, os
from PIL import Image
vms_dir, vs_dir = sys.argv[1], sys.argv[2]
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "screenshots", "v3"))
FIGS = [
    # (출력, 원본 폴더, 파일, [상자...], 가로 px)
    ("vms/inspect_full.jpg",          vms_dir, "VMS_inspect.png",            [(0, 0, 2000, 1088)], 1600),
    ("vms/inspect_kpi.jpg",           vms_dir, "VMS_inspect.png",            [(0, 38, 2000, 100)], 1600),
    ("vms/inspect_camera_box.jpg",    vms_dir, "VMS_inspect_controlbox.png", [(0, 103, 2000, 968)], 1600),
    ("vs/scene_menu_toolbar.jpg",     vs_dir,  "Blob.png",                   [(0, 0, 700, 60)], 1400),
    ("vs/scene_step_camera.jpg",      vs_dir,  "StepCameraSettings.png",     [(0, 60, 156, 215), (1765, 60, 2000, 300)], 1100),
    ("vs/scene_workspace_links.jpg",  vs_dir,  "PCCluster.png",              [(160, 60, 700, 420)], 1000),
]
for out, d, f, boxes, width in FIGS:
    im = Image.open(os.path.join(d, f)).convert("RGB")
    k = im.width / 2000
    parts = [im.crop(tuple(int(v * k) for v in b)) for b in boxes]
    if len(parts) > 1:
        gap = int(16 * k); h = max(p.height for p in parts)
        sheet = Image.new("RGB", (sum(p.width for p in parts) + gap * (len(parts) - 1), h), (22, 27, 38))
        x = 0
        for p in parts: sheet.paste(p, (x, 0)); x += p.width + gap
        parts = [sheet]
    img = parts[0]
    if img.width > width: img = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
    dst = os.path.join(OUT, out)
    img.save(dst, quality=90, optimize=True)
    print(out, img.size, os.path.getsize(dst) // 1024, "KB")
