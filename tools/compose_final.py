#!/usr/bin/env python3
"""최종 합성: 인트로 → 앱 데모(트림) → 웹 포털 세그먼트 → 아웃트로 (크로스페이드)."""
import os, sys, subprocess
FF = sys.argv[1]; FP = sys.argv[2]
P = r"D:\Repo\VMS\docs\promo"
intro = os.path.join(P, "intro.mp4"); outro = os.path.join(P, "outro.mp4")
web = os.path.join(P, "web_segment.mp4"); v1 = os.path.join(P, "vms_demo_v1.mp4")
toolmap = os.path.join(P, "toolmap.mp4")
body = os.path.join(P, "_body.mp4")
final = os.path.join(P, sys.argv[3] if len(sys.argv) > 3 else "vms_promo_v3.mp4")
D = 0.5

def run(a): subprocess.run(a, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
def dur(p): return float(subprocess.run([FP, "-v", "error", "-show_entries", "format=duration",
                                         "-of", "default=nw=1:nk=1", p], capture_output=True, text=True).stdout.strip())

# 1) 본편 트림 4.2~38.5 + 1920x1080 레터박스
run([FF, "-y", "-loglevel", "error", "-ss", "4.2", "-i", v1, "-t", "34.3",
     "-vf", "scale=1920:1080:force_original_aspect_ratio=decrease,"
            "pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black,fps=30,setsar=1,format=yuv420p",
     "-an", "-c:v", "libx264", "-preset", "slow", "-crf", "18", body])

# 인트로 → 앱데모 → 툴맵 → 웹 → 아웃트로
di, db, dt, dw = dur(intro), dur(body), dur(toolmap), dur(web)
o1 = di - D
o2 = di + db - 2*D
o3 = di + db + dt - 3*D
o4 = di + db + dt + dw - 4*D
fc = (f"[0:v][1:v]xfade=transition=fade:duration={D}:offset={o1:.3f}[a];"
      f"[a][2:v]xfade=transition=fade:duration={D}:offset={o2:.3f}[b];"
      f"[b][3:v]xfade=transition=fade:duration={D}:offset={o3:.3f}[c];"
      f"[c][4:v]xfade=transition=fade:duration={D}:offset={o4:.3f}[v]")
run([FF, "-y", "-loglevel", "error", "-i", intro, "-i", body, "-i", toolmap, "-i", web, "-i", outro,
     "-filter_complex", fc, "-map", "[v]", "-c:v", "libx264", "-preset", "slow",
     "-crf", "19", "-pix_fmt", "yuv420p", final])
try: os.remove(body)
except OSError: pass
print("FINAL v3:", final)
print("duration:", round(dur(final), 2), "sec  size:",
      round(os.path.getsize(final)/1024/1024, 1), "MB")
