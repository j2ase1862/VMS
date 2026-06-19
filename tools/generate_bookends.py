#!/usr/bin/env python3
"""
VMS 홍보영상 인트로/아웃트로 모션그래픽 생성기.
Python(PIL+numpy)로 프레임을 그려 ffmpeg로 인코딩한다 (브라우저 불필요).

사용법:  python generate_bookends.py <ffmpeg_exe> <out_dir>
출력:    <out_dir>/intro.mp4 , <out_dir>/outro.mp4   (1920x1080, 30fps, 무음)
"""
import os
import sys
import math
import subprocess
import tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

W, H, FPS = 1920, 1080, 30
CX = W // 2

# ── 색상 ──
BG_TOP = (10, 15, 23)
BG_BOT = (5, 8, 13)
CYAN = (0, 216, 224)
CYAN_D = (0, 150, 165)
WHITE = (238, 244, 248)
GRAY = (150, 170, 182)


def load_font(names, size):
    for n in names:
        p = os.path.join(r"C:\Windows\Fonts", n)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


F_TITLE = load_font(["segoeuib.ttf", "arialbd.ttf"], 240)
F_SUB = load_font(["segoeui.ttf", "arial.ttf"], 60)
F_TAG = load_font(["segoeui.ttf", "arial.ttf"], 44)
F_KR = load_font(["malgunbd.ttf", "malgun.ttf"], 58)
F_FOOT = load_font(["segoeui.ttf", "arial.ttf"], 34)


def ease_out(t):
    t = max(0.0, min(1.0, t))
    return 1 - (1 - t) ** 3


def appear(t, start, dur=0.7):
    """start 부터 dur 동안 0→1 (easeOut)."""
    return ease_out((t - start) / dur)


def fade_out(t, start, dur=0.6):
    return 1.0 - ease_out((t - start) / dur)


# ── 배경(그라데이션 + 비네트) 사전계산 ──
def make_bg():
    yy = np.linspace(0, 1, H)[:, None]
    top = np.array(BG_TOP); bot = np.array(BG_BOT)
    grad = (top * (1 - yy) + bot * yy)[:, None, :] * np.ones((1, W, 1))
    # 중앙 시안 글로우
    xs, ys = np.meshgrid(np.arange(W), np.arange(H))
    d = np.sqrt(((xs - CX) / (W * 0.5)) ** 2 + ((ys - H * 0.42) / (H * 0.5)) ** 2)
    glow = np.clip(1 - d, 0, 1) ** 2.2
    grad += glow[:, :, None] * np.array([0, 40, 46])
    # 비네트
    vig = np.clip(1 - (np.sqrt(((xs - CX) / (W * 0.62)) ** 2 + ((ys - H / 2) / (H * 0.62)) ** 2) - 0.4) * 0.7, 0.45, 1)
    grad *= vig[:, :, None]
    return np.clip(grad, 0, 255).astype(np.uint8)

BG = make_bg()

# 포인트클라우드 느낌의 부유 입자 (결정론적)
rng = np.random.default_rng(42)
N_P = 60
PX = rng.uniform(0, W, N_P)
PY = rng.uniform(0, H, N_P)
PR = rng.uniform(1.2, 3.0, N_P)
PPH = rng.uniform(0, math.tau, N_P)
PCY = rng.uniform(0, 1, N_P)  # 시안 비율


def draw_particles(draw, t):
    for i in range(N_P):
        dy = 10 * math.sin(t * 0.5 + PPH[i])
        dx = 6 * math.cos(t * 0.35 + PPH[i])
        a = int(40 + 45 * (0.5 + 0.5 * math.sin(t * 1.6 + PPH[i])))
        col = CYAN if PCY[i] > 0.5 else WHITE
        x, y = PX[i] + dx, PY[i] + dy
        r = PR[i]
        draw.ellipse([x - r, y - r, x + r, y + r], fill=col + (a,))


def ctext(draw, cy, text, font, color, alpha, dy=0):
    if alpha <= 0.003:
        return
    a = int(max(0, min(1, alpha)) * 255)
    bbox = draw.textbbox((0, 0), text, font=font)
    w = bbox[2] - bbox[0]
    h = bbox[3] - bbox[1]
    x = CX - w / 2 - bbox[0]
    y = cy - h / 2 - bbox[1] + dy
    draw.text((x, y), text, font=font, fill=color + (a,))


def underline(draw, cy, half_w, alpha, color=CYAN):
    if alpha <= 0.01:
        return
    a = int(max(0, min(1, alpha)) * 255)
    draw.rectangle([CX - half_w, cy - 2, CX + half_w, cy + 2], fill=color + (a,))


def render(frame_fn, n_frames, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for i in range(n_frames):
        t = i / FPS
        img = Image.fromarray(BG, "RGB").convert("RGBA")
        ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(ov)
        draw_particles(d, t)
        frame_fn(d, t)
        img = Image.alpha_composite(img, ov).convert("RGB")
        img.save(os.path.join(out_dir, f"{i:05d}.png"))


# ── 인트로 (5초) ──
def intro_frame(d, t):
    # VMS 타이틀
    a1 = appear(t, 0.4, 0.8)
    ctext(d, 340, "VMS", F_TITLE, WHITE, a1, dy=int((1 - a1) * 24))
    # 언더라인
    aU = appear(t, 1.2, 0.7)
    underline(d, 470, int(230 * aU), aU)
    # 서브타이틀
    a2 = appear(t, 1.6, 0.7)
    ctext(d, 540, "Vision  Management  System", F_SUB, WHITE, a2, dy=int((1 - a2) * 16))
    # 태그라인
    a3 = appear(t, 2.6, 0.7)
    ctext(d, 650, "2D · 3D · Deep Learning · Surface Inspection", F_TAG, CYAN, a3)
    # 한글 슬로건
    a4 = appear(t, 3.4, 0.7)
    ctext(d, 740, "검사의 처음부터 끝까지, 하나의 비전 플랫폼", F_KR, GRAY, a4)


# ── 아웃트로 (5초) ──
def outro_frame(d, t):
    fo = 1.0 if t < 4.3 else max(0.0, fade_out(t, 4.3, 0.7))
    a1 = appear(t, 0.3, 0.8) * fo
    ctext(d, 300, "VMS", F_TITLE, WHITE, a1)
    aU = appear(t, 1.0, 0.7) * fo
    underline(d, 430, int(230 * appear(t, 1.0, 0.7)), aU)
    a2 = appear(t, 1.2, 0.7) * fo
    ctext(d, 500, "Vision  Management  System", F_SUB, WHITE, a2)
    a3 = appear(t, 1.8, 0.7) * fo
    ctext(d, 600, "하나의 플랫폼으로, 검사의 처음부터 끝까지", F_KR, CYAN, a3)
    a4 = appear(t, 2.5, 0.7) * fo
    ctext(d, 690, "2D · 3D · Deep Learning · Surface Inspection", F_TAG, GRAY, a4)
    a5 = appear(t, 3.2, 0.7) * fo
    ctext(d, 790, "BODA  Vision  AI", F_FOOT, CYAN_D, a5)


def encode(ffmpeg, frames_dir, out_mp4):
    subprocess.run([
        ffmpeg, "-y", "-loglevel", "error",
        "-framerate", str(FPS), "-i", os.path.join(frames_dir, "%05d.png"),
        "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p",
        out_mp4
    ], check=True)
    print("wrote", out_mp4)


def main():
    ffmpeg = sys.argv[1] if len(sys.argv) > 1 else "ffmpeg"
    out_dir = sys.argv[2] if len(sys.argv) > 2 else "."
    os.makedirs(out_dir, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        di = os.path.join(tmp, "intro"); do = os.path.join(tmp, "outro")
        print("rendering intro frames...")
        render(intro_frame, 5 * FPS, di)
        print("rendering outro frames...")
        render(outro_frame, 5 * FPS, do)
        encode(ffmpeg, di, os.path.join(out_dir, "intro.mp4"))
        encode(ffmpeg, do, os.path.join(out_dir, "outro.mp4"))
    print("done.")


if __name__ == "__main__":
    main()
