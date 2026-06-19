#!/usr/bin/env python3
"""
BODA.VMS.Web 쇼케이스 세그먼트 빌드 — 다크 스크린샷에 Ken Burns + 캡션 + 타이틀 카드.
캡션/타이틀은 PIL(말굽고딕)로 PNG 오버레이 생성, ffmpeg 로 zoompan + overlay + xfade.

사용법: python build_web_segment.py <ffmpeg> <ffprobe>
출력: docs/promo/web_segment.mp4  (1920x1080, 30fps, 무음)
"""
import os, sys, subprocess, tempfile, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

FF = sys.argv[1] if len(sys.argv) > 1 else "ffmpeg"
FP = sys.argv[2] if len(sys.argv) > 2 else "ffprobe"
PROMO = r"D:\Repo\VMS\docs\promo"
WEB = os.path.join(PROMO, "web")
W, H, FPS = 1920, 1080, 30
CYAN = (0, 216, 224); WHITE = (240, 245, 248); GRAY = (170, 186, 196)

def font(names, size):
    for n in names:
        p = os.path.join(r"C:\Windows\Fonts", n)
        if os.path.exists(p): return ImageFont.truetype(p, size)
    return ImageFont.load_default()

F_TITLE = font(["segoeuib.ttf"], 150)
F_TKR = font(["malgunbd.ttf"], 52)
F_MAIN = font(["malgunbd.ttf"], 46)
F_SUB = font(["malgun.ttf"], 28)
F_TAG = font(["segoeuib.ttf"], 26)

# (파일, 메인캡션, 서브캡션)
SCREENS = [
    ("01_dashboard.png", "실시간 통합 대시보드", "현장 모니터링 · 라인별 생산 / 합격 / 불량률"),
    ("04_pareto.png",    "품질 분석 — 파레토 차트", "불량 유형 집중 분석 · 누적 비중"),
    ("05_oee.png",       "설비종합효율 OEE",       "가용성 × 성능 × 품질 · 라인별 실시간"),
    ("06_reliability.png","설비 신뢰성 MTBF / MTTR", "고장 간격 · 수리 시간 · 가용성"),
    ("08_alarms.png",    "실시간 알람 · 안돈",      "NG · 설비 이상 즉시 통지 / 조치 추적"),
    ("03_workorders.png","생산 작업지시 · MES",     "작업지시 · 로트 · 생산 추적성"),
]
DUR = 2.7        # 화면당 길이
XF = 0.5         # 크로스페이드

def dark_bg():
    yy = np.linspace(0, 1, H)[:, None]
    grad = (np.array([10, 15, 23]) * (1 - yy) + np.array([5, 8, 13]) * yy)[:, None, :] * np.ones((1, W, 1))
    xs, ys = np.meshgrid(np.arange(W), np.arange(H))
    d = np.sqrt(((xs - W/2)/(W*0.5))**2 + ((ys - H*0.42)/(H*0.5))**2)
    grad += (np.clip(1 - d, 0, 1)**2.2)[:, :, None] * np.array([0, 38, 44])
    return np.clip(grad, 0, 255).astype(np.uint8)
BG = dark_bg()

def ctext(d, cx, cy, text, fnt, color, anchor="mm"):
    d.text((cx, cy), text, font=fnt, fill=color, anchor=anchor)

def make_title(path):
    img = Image.fromarray(BG, "RGB").convert("RGBA")
    d = ImageDraw.Draw(img)
    ctext(d, W//2, 430, "BODA.VMS.Web", F_TITLE, WHITE)
    d.rectangle([W//2-230, 530, W//2+230, 534], fill=CYAN)
    ctext(d, W//2, 590, "중앙 관리 · 분석 포털", F_TKR, CYAN)
    ctext(d, W//2, 660, "MES · 품질 · 설비 · 리포트 통합 웹", F_SUB, GRAY)
    img.convert("RGB").save(path)

def make_caption(path, main, sub):
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    # 하단 그라데이션
    for i in range(220):
        a = int(225 * (i/220)**1.3)
        d.line([(0, H-220+i), (W, H-220+i)], fill=(0, 0, 0, a))
    # 상단 좌측 BODA.VMS.Web 배지
    d.rounded_rectangle([40, 34, 290, 80], radius=6, fill=(0, 173, 181, 235))
    ctext(d, 60, 57, "BODA.VMS.Web", F_TAG, (255, 255, 255, 255), anchor="lm")
    # 캡션
    ctext(d, 64, H-128, main, F_MAIN, WHITE + (255,), anchor="lm")
    ctext(d, 66, H-76, sub, F_SUB, GRAY + (255,), anchor="lm")
    ov.save(path)

def run(args):
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

def probe(path):
    return float(subprocess.run([FP, "-v", "error", "-show_entries", "format=duration",
                                 "-of", "default=nw=1:nk=1", path], capture_output=True, text=True).stdout.strip())

def main():
    with tempfile.TemporaryDirectory() as tmp:
        clips = []
        # 타이틀 카드 (2.4s, 페이드)
        tpng = os.path.join(tmp, "title.png"); make_title(tpng)
        tclip = os.path.join(tmp, "c_title.mp4")
        run([FF, "-y", "-loglevel", "error", "-loop", "1", "-t", "2.4", "-i", tpng,
             "-vf", f"fade=t=in:st=0:d=0.4,fps={FPS},format=yuv420p", "-r", str(FPS),
             "-c:v", "libx264", "-preset", "medium", "-crf", "18", tclip])
        clips.append(tclip)

        # 각 화면 Ken Burns + 캡션
        for i, (fn, main_c, sub_c) in enumerate(SCREENS):
            src = os.path.join(WEB, fn)
            if not os.path.exists(src):
                print("skip missing", fn); continue
            cap = os.path.join(tmp, f"cap{i}.png"); make_caption(cap, main_c, sub_c)
            out = os.path.join(tmp, f"c{i}.mp4")
            df = int(DUR*FPS)
            # 정적 화면(줌 없음, UI 텍스트 떨림 방지) + 캡션만 페이드인 → 미세한 움직임.
            run([FF, "-y", "-loglevel", "error", "-loop", "1", "-i", src,
                 "-loop", "1", "-i", cap, "-filter_complex",
                 f"[0:v]scale={W}:{H},setsar=1[bg];"
                 f"[1:v]format=yuva420p,fade=t=in:st=0.15:d=0.6:alpha=1[cap];"
                 f"[bg][cap]overlay=0:0,format=yuv420p[v]",
                 "-map", "[v]", "-frames:v", str(df), "-r", str(FPS),
                 "-c:v", "libx264", "-preset", "medium", "-crf", "18", out])
            clips.append(out)

        # xfade 체인
        durs = [probe(c) for c in clips]
        inputs = []
        for c in clips: inputs += ["-i", c]
        fc = ""; prev = "[0:v]"; off = 0.0
        for k in range(1, len(clips)):
            off += durs[k-1] - XF
            lbl = f"[x{k}]" if k < len(clips)-1 else "[v]"
            fc += f"{prev}[{k}:v]xfade=transition=fade:duration={XF}:offset={off:.3f}{lbl};"
            prev = f"[x{k}]"
        fc = fc.rstrip(";")
        outp = os.path.join(PROMO, "web_segment.mp4")
        run([FF, "-y", "-loglevel", "error", *inputs, "-filter_complex", fc,
             "-map", "[v]", "-r", str(FPS), "-c:v", "libx264", "-preset", "slow", "-crf", "18",
             "-pix_fmt", "yuv420p", outp])
        print("web_segment.mp4:", round(probe(outp), 2), "sec")

if __name__ == "__main__":
    main()
