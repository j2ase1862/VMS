#!/usr/bin/env python3
"""
VMS 전체 비전 툴맵 카드 (홍보영상 '폭' 비트). 다크/시안 톤, 카드 스태거 페이드인.
사용법: python build_toolmap.py <ffmpeg>
출력: docs/promo/toolmap.mp4  (1920x1080, 30fps, ~4.6s, 무음)
"""
import os, sys, subprocess, tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

FF = sys.argv[1] if len(sys.argv) > 1 else "ffmpeg"
PROMO = r"D:\Repo\VMS\docs\promo"
W, H, FPS = 1920, 1080, 30
CYAN = (0, 216, 224); WHITE = (238, 244, 248); GRAY = (165, 182, 194)

def font(names, size):
    for n in names:
        p = os.path.join(r"C:\Windows\Fonts", n)
        if os.path.exists(p): return ImageFont.truetype(p, size)
    return ImageFont.load_default()

F_TITLE = font(["malgunbd.ttf"], 56)
F_SUB = font(["malgun.ttf"], 30)
F_CAT = font(["malgunbd.ttf"], 30)
F_TOOL = font(["malgun.ttf"], 20)

CATS = [
    ("전처리", "Blur · Morphology · Enhance"),
    ("변환", "Grayscale · Threshold · Edge"),
    ("패턴 매칭", "Feature · Shape Match"),
    ("블롭 분석", "Blob · 면적 / 개수"),
    ("측정", "Caliper · Circle / Line Fit"),
    ("식별", "OCR · OCV"),
    ("코드 리딩", "1D · QR · DataMatrix"),
    ("컬러", "Color Extract · Match"),
    ("캘리브레이션", "Image Rectify"),
    ("3D 분석", "PointCloud · Plane Fit"),
    ("딥러닝", "Detection · Seg · Anomaly"),
    ("표면 검사", "Photometric Stereo"),
]

# 그리드 레이아웃
COLS, ROWS = 4, 3
MX, MTOP, GAP = 110, 250, 28
CW = (W - 2*MX - (COLS-1)*GAP) // COLS
CH = (H - MTOP - 90 - (ROWS-1)*GAP) // ROWS

def dark_bg():
    yy = np.linspace(0, 1, H)[:, None]
    grad = (np.array([10, 15, 23]) * (1 - yy) + np.array([5, 8, 13]) * yy)[:, None, :] * np.ones((1, W, 1))
    xs, ys = np.meshgrid(np.arange(W), np.arange(H))
    d = np.sqrt(((xs - W/2)/(W*0.55))**2 + ((ys - H*0.45)/(H*0.6))**2)
    grad += (np.clip(1 - d, 0, 1)**2.2)[:, :, None] * np.array([0, 34, 40])
    return np.clip(grad, 0, 255).astype(np.uint8)
BG = dark_bg()

def ease(t): t = max(0, min(1, t)); return 1 - (1 - t)**3
def appear(t, s, d=0.4): return ease((t - s) / d)

def frame(t):
    img = Image.fromarray(BG, "RGB").convert("RGBA")
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    # 타이틀
    a = appear(t, 0.0, 0.5)
    if a > 0:
        d.text((W//2, 120), "하나의 플랫폼, 30개+ 비전 툴", font=F_TITLE,
               fill=WHITE + (int(a*255),), anchor="mm")
        d.text((W//2, 180), "룰베이스부터 딥러닝까지 — 드래그&드롭 노코드", font=F_SUB,
               fill=CYAN + (int(a*255),), anchor="mm")
    # 카드 (스태거)
    for i, (cat, tools) in enumerate(CATS):
        r, c = divmod(i, COLS)
        ca = appear(t, 0.55 + i*0.07, 0.4)
        if ca <= 0.01: continue
        A = int(ca*255)
        x = MX + c*(CW+GAP); y = MTOP + r*(CH+GAP)
        d.rounded_rectangle([x, y, x+CW, y+CH], radius=12,
                            fill=(18, 26, 36, int(ca*230)), outline=(0, 173, 181, int(ca*160)), width=1)
        d.rounded_rectangle([x, y, x+6, y+CH], radius=3, fill=CYAN + (A,))  # 좌측 시안 바
        d.text((x+28, y+44), cat, font=F_CAT, fill=WHITE + (A,), anchor="lm")
        d.text((x+28, y+CH-44), tools, font=F_TOOL, fill=GRAY + (A,), anchor="lm")
    return Image.alpha_composite(img, ov).convert("RGB")

def main():
    with tempfile.TemporaryDirectory() as tmp:
        n = int(4.6 * FPS)
        for i in range(n):
            frame(i / FPS).save(os.path.join(tmp, f"{i:05d}.png"))
        out = os.path.join(PROMO, "toolmap.mp4")
        subprocess.run([FF, "-y", "-loglevel", "error", "-framerate", str(FPS),
                        "-i", os.path.join(tmp, "%05d.png"),
                        "-vf", "fade=t=in:st=0:d=0.3",
                        "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", out],
                       check=True, stderr=subprocess.PIPE)
        print("toolmap.mp4 written")

if __name__ == "__main__":
    main()
