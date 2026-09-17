# 6장 "비전 도구 설명 및 사용법" 생성 — docs/manuals/src/06_tools.html
#   입력: docs/gs/pipeline/_tool_params.json (파라미터 표), docs/manuals/tools_prose.json (도구별 설명),
#         docs/gs/screenshots/v3/tools/NN_*.png (설정 패널 캡처 — 긴 패널은 2단으로 나눠 붙임)
import json, io, os, html, re
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
PARAMS = os.path.normpath(os.path.join(HERE, "..", "gs", "pipeline", "_tool_params.json"))
PROSE = os.path.join(HERE, "tools_prose.json")
SHOT = os.path.normpath(os.path.join(HERE, "..", "gs", "screenshots", "v3", "tools"))
OUT = os.path.join(HERE, "src", "06_tools.html")
tools = {t["tool"]: t for t in json.load(io.open(PARAMS, encoding="utf-8"))}
prose = json.load(io.open(PROSE, encoding="utf-8"))
esc = html.escape

# 팔레트 순서 (카테고리 → [(params.json 키, 패널 PNG)])
ORDER = [
  ("Preprocessing (Color) — 컬러 전처리", [("Blur (블러)", "01_Blur.png"), ("Morphology (형태학 연산)", "02_Morphology.png"), ("Image Enhance (이미지 보정)", "03_Image_Enhance.png"), ("Polar Unwrap (극좌표 펼치기)", "04_Polar_Unwrap.png")]),
  ("Conversion (Gray) — 그레이 변환", [("Grayscale (그레이 변환)", "05_Grayscale.png"), ("Threshold (이진화)", "06_Threshold.png"), ("Edge Detection (에지 검출)", "07_Edge_Detection.png"), ("Histogram (히스토그램)", "08_Histogram.png")]),
  ("Pattern Matching — 패턴 매칭", [("Feature Match (특징 매칭)", "09_Feature_Match.png"), ("Shape Match (형상 매칭)", "10_Shape_Match.png")]),
  ("Alignment — 얼라인", [("Match Align (매치 얼라인)", "11_Match_Align.png"), ("Multi-Step Align (다중 스텝 얼라인)", "12_Multi-Step_Align.png")]),
  ("Blob Analysis — 블롭 분석", [("Blob Analysis (블롭 분석)", "13_Blob.png")]),
  ("Measurement — 치수 측정", [("Caliper (캘리퍼 측정)", "14_Caliper.png"), ("Line Fit (직선 피팅)", "15_Line_Fit.png"), ("Circle Fit (원 피팅)", "16_Circle_Fit.png"), ("Geometry (기하 측정)", "17_Geometry.png")]),
  ("Identification — 문자 인식 · 검증", [("OCR (문자 인식)", "18_OCR.png"), ("OCV (문자 검증)", "19_OCV.png")]),
  ("Code Reading — 코드 판독", [("Code Reader (코드 판독)", "20_Code_Reader.png")]),
  ("3D Processing — 점군 처리", [("Point Cloud Filter (포인트클라우드 필터)", "21_PointCloud_Filter.png"), ("Point Cloud Registration (포인트클라우드 정합)", "22_PointCloud_Registration.png"), ("Point Cloud Mask Crop (마스크 점군 크롭)", "23_PointCloud_Mask_Crop.png"), ("Point Cloud Cluster (포인트클라우드 클러스터)", "24_PointCloud_Cluster.png")]),
  ("3D Measurement — 3D 측정", [("Point Cloud Deviation (기준 형상 편차)", "25_PointCloud_Deviation.png"), ("Height Slicer (높이 슬라이스)", "26_Height_Slicer.png"), ("Plane Fit (평면 피팅)", "27_Plane_Fit.png"), ("Geometry 3D (3D 기하)", "28_3D_Geometry.png")]),
  ("Deep Learning — 딥러닝 추론", [("Detection (딥러닝 검출)", "29_Detection_YOLO.png"), ("Segmentation (분할)", "30_Segmentation.png"), ("YOLO Segmentation", "31_YOLOv8-seg.png"), ("RF-DETR-seg (인스턴스 분할)", "32_RF-DETR-seg.png"), ("Classify (분류)", "33_Classify.png"), ("Anomaly (이상 탐지)", "34_Anomaly.png")]),
  ("Judgment — 판정", [("Result (결과/판정)", "35_Result.png")]),
  ("Color — 색상", [("Color Extract (색 추출)", "36_Color_Extract.png"), ("Color Match (색 매칭)", "37_Color_Match.png")]),
  ("Calibration — 보정", [("Image Rectify (이미지 정류)", "38_Image_Rectify.png")]),
  ("Surface Analysis — 표면 분석", [("Photometric Stereo (포토메트릭 스테레오)", "39_Photometric_Stereo.png")]),
]
DESC = json.load(io.open(os.path.join(HERE, "tools_param_desc.json"), encoding="utf-8")) if os.path.exists(os.path.join(HERE, "tools_param_desc.json")) else {}

def panel_figure(png, title, override=None):
    # override: docs/gs/screenshots/v3 기준 상대 경로 (예: "vs/scene_fm_settings.png") — 학습 상태 등 실사용 캡처로 교체
    shot_dir, rel_dir = SHOT, "../gs/screenshots/v3/tools"
    if override:
        shot_dir = os.path.normpath(os.path.join(SHOT, "..", os.path.dirname(override))); png = os.path.basename(override)
        rel_dir = "../gs/screenshots/v3/" + os.path.dirname(override)
    src = os.path.join(shot_dir, png)
    if not os.path.exists(src): return f'<p class="callout warn">패널 캡처 없음: {esc(png)}</p>\n'
    im = Image.open(src)
    w, h = im.size
    rel = f"{rel_dir}/{png}"
    width = 240
    if h > 1500:
        # 2단(또는 3단)으로 나눠 가로로 붙인다
        cols = 2 if h <= 3000 else 3
        seg = (h + cols - 1) // cols
        gap = 24
        sheet = Image.new("RGB", (cols * w + (cols - 1) * gap, seg), (30, 34, 43))
        for i in range(cols):
            part = im.crop((0, i * seg, w, min(h, (i + 1) * seg)))
            sheet.paste(part, (i * (w + gap), 0))
        out_png = png.replace(".png", "_split.png")
        sheet.save(os.path.join(shot_dir, out_png))
        rel = f"{rel_dir}/{out_png}"
        width = min(620, 240 * cols + 20)
    return f'<figure><img src="{rel}" data-width="{width}" alt="{esc(title)} 설정 패널"><figcaption>{esc(title)} — Tool Settings 패널</figcaption></figure>\n'

out = ['<section id="tools">\n<h2>6. 비전 도구 설명 및 사용법</h2>\n',
 '<p>Tool Palette 의 도구를 카테고리 순서대로 설명합니다. 도구마다 <strong>용도 · 설정 패널 · 주요 파라미터 · 결과 값 · 사용 요령</strong>을 정리했으며, '
 '설정 패널 그림은 Expert Mode 를 끈 기본 상태입니다. 파라미터의 정확한 범위와 전체 목록은 부록 13.2 를, 도구를 연결하는 방법은 §5.5 를 참고하세요. '
 '각 파라미터 옆의 물음표(?)에 마우스를 올리면 같은 설명이 프로그램 안에서도 표시됩니다.</p>\n']
sec = 0
for cat, items in ORDER:
    sec += 1
    out.append(f'<h3 id="tools-{sec}">6.{sec} {esc(cat)}</h3>\n')
    for key, png in items:
        t = tools.get(key); p = prose.get(key, {})
        title = p.get("title") or key
        out.append(f'<h4>{esc(title)}</h4>\n')
        if p.get("purpose"): out.append(f'<p>{p["purpose"]}</p>\n')
        out.append(panel_figure(png, title, p.get("panel")))
        # 파라미터 표 — prose 의 params(우선) 또는 _tool_params.json 기본 라벨
        rows = p.get("params")
        if rows:
            out.append('<table class="cols-3-2-4">\n<tr><th>파라미터</th><th>기본값 / 범위</th><th>설명</th></tr>\n')
            for r in rows: out.append(f'<tr><td>{r[0]}</td><td>{r[1]}</td><td>{r[2]}</td></tr>\n')
            out.append('</table>\n')
        elif t and t.get("params"):
            out.append('<table class="cols-3-2-4">\n<tr><th>파라미터</th><th>컨트롤 / 범위</th><th>설명</th></tr>\n')
            for pp in t["params"]:
                ctrl = pp["ctrl"] + (f' ({pp["enum"]})' if pp.get("enum") else (f' [{pp.get("min","")}~{pp.get("max","")}]' if (pp.get("min") or pp.get("max")) else ""))
                out.append(f'<tr><td>{esc(pp["label"])}</td><td>{esc(ctrl)}</td><td>{esc(DESC.get(pp["param"], ""))}</td></tr>\n')
            out.append('</table>\n')
        else:
            out.append('<p>이 도구는 별도 수치 파라미터 없이 입력 연결과 ROI 만으로 동작합니다.</p>\n')
        if p.get("outputs"): out.append(f'<p><strong>결과 값</strong> — {p["outputs"]}</p>\n')
        if p.get("judgment"): out.append(f'<p><strong>판정</strong> — {p["judgment"]}</p>\n')
        if p.get("tips"):
            out.append('<ul>\n' + "".join(f'<li>{x}</li>\n' for x in p["tips"]) + '</ul>\n')
        if p.get("callout"): out.append(f'<div class="callout tip">{p["callout"]}</div>\n')
out.append('</section>\n')
io.open(OUT, "w", encoding="utf-8", newline="\n").write("".join(out))
missing = [k for _, items in ORDER for k, _ in items if k not in prose]
print("WROTE", OUT, "tools:", sum(len(i) for _, i in ORDER), "missing prose:", missing)
