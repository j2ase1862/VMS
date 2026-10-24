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
  ("3D Measurement — 3D 측정", [("Point Cloud Deviation (기준 형상 편차)", "25_PointCloud_Deviation.png"), ("Height Slicer (높이 슬라이스)", "26_Height_Slicer.png"), ("Plane Fit (평면 피팅)", "27_Plane_Fit.png"), ("Geometry 3D (3D 기하)", "28_3D_Geometry.png"), ("Point Cloud Line Fit (포인트클라우드 직선 피팅)", "29_PointCloud_Line_Fit.png")]),
  ("Deep Learning — 딥러닝 추론", [("Detection (딥러닝 검출)", "29_Detection.png"), ("Segmentation (분할)", "30_Segmentation.png"), ("RF-DETR-seg (인스턴스 분할)", "32_RF-DETR-seg.png"), ("Classify (분류)", "33_Classify.png"), ("Anomaly (이상 탐지)", "34_Anomaly.png")]),
  ("Judgment — 판정", [("Result (결과/판정)", "35_Result.png"), ("Ensemble (앙상블)", "36_Ensemble.png")]),
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

# 도구별 "실행 예" — 예제 레시피 · 이미지를 실제로 실행한 화면 (docs/gs/screenshots/v3/scenes/*.jpg,
#   VisionSetup Debug --capture-scenes 로 찍고 docs/gs/pipeline/crop_tool_scenes.py 로 자름). 캡션의 값은 그 실행의 결과다.
SCENES = {
 "Blur (블러)": ("Blur", "그레이 이미지에 Gaussian 블러(커널 5)를 적용한 결과 이미지. Run Results 에 적용한 방식 · 커널 크기가 표시됩니다"),
 "Morphology (형태학 연산)": ("Morphology", "이진화된 부품 이미지에 Open(5 × 5, 1 회)을 적용 — 작은 잡음 점이 지워진 결과"),
 "Image Enhance (이미지 보정)": ("ImageEnhance", "UnsharpMask(강도 1.0)로 윤곽을 선명하게 만든 결과"),
 "Polar Unwrap (극좌표 펼치기)": ("PolarUnwrap", "원형으로 인쇄된 문자(35° 돌아감)를 반지름 300~400 px 띠(2513 × 100 px)로 펼친 결과. 뒤에 연결한 OCR 이 \"LOT 260923 A12\" 를 읽습니다(예제 이미지는 합성)"),
 "Grayscale (그레이 변환)": ("Grayscale", "컬러 이미지를 1 채널 그레이(2448 × 2048)로 바꾼 결과"),
 "Threshold (이진화)": ("Threshold", "Otsu 자동 이진화 결과(계산된 임계값 128) — 밝은 배경과 어두운 부품이 흑백으로 나뉩니다"),
 "Edge Detection (에지 검출)": ("EdgeDetection", "Canny 에지 검출 결과 — 밝기가 크게 바뀌는 윤곽만 흰 점으로 남습니다(EdgePixelCount 6,763)"),
 "Histogram (히스토그램)": ("Histogram", "어둡게 찍힌 저대비 이미지에 히스토그램 평활화(Equalize)를 적용해 대비를 넓힌 결과"),
 "Feature Match (특징 매칭)": ("FeatureMatch", "학습한 피팅 모델로 화면 안의 2 개를 함께 찾은 예(Max Instances 2) — 찾은 위치마다 녹색 외곽선, Run Results 에 개수 · 점수 · 위치 · 각도 · 커버리지"),
 "Shape Match (형상 매칭)": ("ShapeMatch", "35° 돌아간 피팅을 찾은 예 — 점수 0.967, 각도 −35.0°"),
 "Match Align (매치 얼라인)": ("MatchAlign", "돌아가고 옮겨진 피팅의 기준 위치 대비 보정량 — ΔX −39.6 px · ΔY −3.8 px · Δθ 33.38°, 허용 범위 안이라 OK"),
 "Multi-Step Align (다중 스텝 얼라인)": ("MultiStepAlign", "A 스텝(왼쪽 시야)과 B 스텝(오른쪽 시야)에서 각각 매칭한 결과를 합쳐 계산한 보정량 — 부품을 3° 돌려 놓은 장면에서 Δθ −2.899°, 판정 OK. B 스텝 화면입니다"),
 "Blob Analysis (블롭 분석)": ("Blob", "판의 구멍 6 개를 검출 — 개수 6 · 총 면적 48,417 px² 가 기준 안이라 OK. 구멍마다 윤곽 · 중심 · 번호가 표시됩니다"),
 "Caliper (캘리퍼 측정)": ("Caliper", "막대의 폭을 엣지 쌍(EdgePair)으로 측정 — 141.11 px(캘리브레이션 적용 8.051 mm)"),
 "Line Fit (직선 피팅)": ("LineFit", "블록 윗변에 캘리퍼 12 개를 놓아 직선 피팅 — 각도 0.35°, 12/12 점 사용"),
 "Circle Fit (원 피팅)": ("CircleFit", "구멍 A 의 원 피팅 — 중심 (1005.8, 982.2), 반지름 70.4 px"),
 "Geometry (기하 측정)": ("Geometry", "두 구멍의 중심 거리 판정에서 NG 가 난 예 — 21.045 mm 가 기준 20 ± 0.3 mm(19.7~20.3 mm) 밖이라 NG(왼쪽 위 붉은 배지)"),
 "OCR (문자 인식)": ("OCR", "Tesseract 엔진으로 인쇄 문자 \"LOT 260928-A01\" 을 읽어 기대 문자열과 일치 — 신뢰도 91%"),
 "OCV (문자 검증)": ("OCV", "글자 하나가 빠진 인쇄(기준 VMS2658K → 읽힘 VMS268K)를 불일치로 판정한 NG 예"),
 "Code Reader (코드 판독)": ("CodeReader", "GS1 DataMatrix 판독 — 읽은 데이터를 응용 식별자 (01) · (17) · (10) · (21) 로 나눠 해석합니다"),
 "Point Cloud Filter (포인트클라우드 필터)": ("PCFilter", "프레임 점군 28,382 점을 4 mm 복셀로 솎아 5,330 점으로 줄인 결과(81.2% 감소). 뒤의 정합 · 편차 계산이 빨라집니다"),
 "Point Cloud Registration (포인트클라우드 정합)": ("PCRegistration", "−44° 돌아가 놓인 프레임을 기준 점군에 정합 — 회전 RotationZ −44.06°, 평균 오차 2.736 mm, 신뢰도 0.697 이 하한 0.3 이상이라 OK"),
 "Point Cloud Mask Crop (마스크 점군 크롭)": ("PCMaskCrop", "Height Slicer 마스크로 전체 3,145,728 점 가운데 프레임 28,770 점만 남긴 결과"),
 "Point Cloud Cluster (포인트클라우드 클러스터)": ("PCCluster", "남은 점을 덩어리로 나눠 프레임 1 개를 찾은 예 — 치수가 기준 376 × 252 mm(−6 / +6) 안이라 OK"),
 "Point Cloud Deviation (기준 형상 편차)": ("PCDeviation", "비틀린 프레임(합성 불량)의 편차 — 기준에서 5 mm 넘게 벗어난 점이 41.2%로 상한 15% 를 넘어 NG"),
 "Height Slicer (높이 슬라이스)": ("HeightSlicer", "높이 1780~1825 mm 구간의 점만 흰색으로 남긴 2D 마스크 — 바닥 위에 놓인 프레임만 남습니다"),
 "Plane Fit (평면 피팅)": ("PlaneFit", "ROI 안의 점으로 평면을 피팅 — 인라이어 70,403 / 81,114 점, 평면 식과 평면도가 Run Results 에 표시됩니다(이 레시피는 기준면 용도라 판정 끔)"),
 "Geometry 3D (3D 기하)": ("Geometry3D", "높이가 낮아진 프레임(합성 불량)의 점-평면 거리 — 81.44 mm 가 기준 90 ± 5 mm 밖이라 NG"),
 "Point Cloud Line Fit (포인트클라우드 직선 피팅)": ("PCLineFit", "가운데가 8 mm 휜 막대(합성 점군) — 직진도 5.318 mm 가 상한 3 mm 를 넘어 NG"),
 "Detection (딥러닝 검출)": ("Detection", "T · Y 피팅 3 개를 검출 — 객체마다 클래스별 색 박스, Run Results 에 개수 · 클래스 · 신뢰도"),
 "Segmentation (분할)": ("Segmentation", "화소 단위 분할 — 배경 · T · Y 3 클래스를 색으로 칠하고, 클래스별 화소 수 · 비율을 냅니다"),
 "RF-DETR-seg (인스턴스 분할)": ("RfdetrSeg", "인스턴스 분할 — 피팅 3 개 각각의 마스크 · 박스 · 클래스 · 신뢰도"),
 "Classify (분류)": ("Classify", "테이프가 붙은 피팅을 tape(100%)로 분류 — tape 는 OK Classes 에 없으므로 NG(왼쪽 위 붉은 글자)"),
 "Anomaly (이상 탐지)": ("Anomaly", "수정액 자국이 있는 피팅 — 이상 점수 0.665 가 임계값 0.545 를 넘어 NG. 히트맵에서 이상 부위가 밝게 표시됩니다"),
 "Result (결과/판정)": ("Result", "막대 폭이 불량인 이미지 — Bar Width 가 실패하자 Result All Pass(모두 합격)는 NG, Result Any Pass(하나라도 합격)는 OK. 오른쪽 Source Results 에 연결된 도구별 OK/NG 가 보입니다"),
 "Ensemble (앙상블)": ("Ensemble", "수정액 자국이 있는 피팅 — Detection 은 OK, Anomaly 는 NG. 결합 방식별로 And · Weighted · Consensus 는 NG, Or 만 OK"),
 "Color Extract (색 추출)": ("ColorExtract", "학습한 파란 캡 색(HSV 범위)을 추출 — 화면의 3.47%(36,970 px). 개수 · 면적 판정은 뒤의 Blob 이 합니다"),
 "Color Match (색 매칭)": ("ColorMatch", "학습한 파란 캡 색과 ΔE 25 이내인 화소 3.79%(40,353 px)를 표시. 면적 판정은 뒤의 Blob 이 합니다"),
 "Image Rectify (이미지 정류)": ("ImageRectify", "레시피의 캘리브레이션으로 렌즈 왜곡을 보정한 이미지(화소 크기 0.057 mm) — 뒤의 Caliper · Circle Fit 이 보정된 이미지로 잽니다"),
 "Photometric Stereo (포토메트릭 스테레오)": ("PhotometricStereo", "조명 4 방향 이미지로 계산한 결함 강조(DefectEnhanced) 이미지 — 긁힘(선)과 찍힘(점)이 드러나고, 뒤의 Threshold · Blob 이 NG 로 판정합니다(예제 이미지는 합성)"),
}

def scene_figure(key, title):
    if key not in SCENES: return ""
    name, cap = SCENES[key]
    src = os.path.normpath(os.path.join(SHOT, "..", "scenes", name + ".jpg"))
    if not os.path.exists(src): return f'<p class="callout warn">실행 예 캡처 없음: {esc(name)}.jpg</p>\n'
    return (f'<figure><img src="../gs/screenshots/v3/scenes/{name}.jpg" data-width="620" alt="{esc(title)} 실행 예">'
            f'<figcaption>실행 예 — {esc(cap)}</figcaption></figure>\n')

out = ['<section id="tools">\n<h2>6. 비전 도구 설명 및 사용법</h2>\n',
 '<p>Tool Palette 의 도구를 카테고리 순서대로 설명합니다. 도구마다 <strong>용도 · 설정 패널 · 주요 파라미터 · 결과 값 · 사용 요령</strong>을 정리했으며, '
 '설정 패널 그림은 Expert Mode 를 끈 기본 상태이고, <strong>실행 예</strong> 그림은 예제 레시피와 이미지를 실제로 실행한 화면(이미지 뷰 · Run Results · 그 레시피의 Tool Settings)입니다. 파라미터의 정확한 범위와 전체 목록은 부록 13.2 를, 도구를 연결하는 방법은 §5.5 를 참고하세요. '
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
        out.append(scene_figure(key, title))
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
