# 핵심 비전 툴의 ToolSettings XAML -> 파라미터 JSON (매뉴얼 #4 생성기 입력)
import json, re, os

BASE = r"D:\Repo\VMS\VMS.VisionSetup\Views\ToolSettings\Tools"
OUT = r"D:\Repo\VMS\docs\gs\pipeline\_tool_params.json"

# (표시명, 카테고리, 파일) — 전체 도구(카테고리 순)
CORE = [
    ("Grayscale (그레이 변환)", "Conversion", "GrayscaleToolSettings.xaml"),
    ("Threshold (이진화)", "Conversion", "ThresholdToolSettings.xaml"),
    ("Blur (블러)", "Preprocessing", "BlurToolSettings.xaml"),
    ("Morphology (형태학 연산)", "Preprocessing", "MorphologyToolSettings.xaml"),
    ("Edge Detection (에지 검출)", "Preprocessing", "EdgeDetectionToolSettings.xaml"),
    ("Histogram (히스토그램)", "Preprocessing", "HistogramToolSettings.xaml"),
    ("Image Enhance (이미지 보정)", "Preprocessing", "ImageEnhanceToolSettings.xaml"),
    ("Image Rectify (이미지 정류)", "Preprocessing", "ImageRectifyToolSettings.xaml"),
    ("Polar Unwrap (극좌표 펼치기)", "Preprocessing", "PolarUnwrapToolSettings.xaml"),
    ("Color Extract (색 추출)", "Color", "ColorExtractToolSettings.xaml"),
    ("Color Match (색 매칭)", "Color", "ColorMatchToolSettings.xaml"),
    ("Blob Analysis (블롭 분석)", "Blob", "BlobToolSettings.xaml"),
    ("Caliper (캘리퍼 측정)", "Measurement", "CaliperToolSettings.xaml"),
    ("Line Fit (직선 피팅)", "Measurement", "LineFitToolSettings.xaml"),
    ("Circle Fit (원 피팅)", "Measurement", "CircleFitToolSettings.xaml"),
    ("Geometry (기하 측정)", "Measurement", "GeometryToolSettings.xaml"),
    ("Shape Match (형상 매칭)", "Pattern Matching", "ShapeMatchToolSettings.xaml"),
    ("Feature Match (특징 매칭)", "Pattern Matching", "FeatureMatchToolSettings.xaml"),
    ("OCR (문자 인식)", "Identification", "OCRToolSettings.xaml"),
    ("OCV (문자 검증)", "Identification", "OCVToolSettings.xaml"),
    ("Code Reader (코드 판독)", "Code Reading", "CodeReaderToolSettings.xaml"),
    ("Geometry 3D (3D 기하)", "3D Analysis", "Geometry3DToolSettings.xaml"),
    ("Plane Fit (평면 피팅)", "3D Analysis", "PlaneFitToolSettings.xaml"),
    ("Height Slicer (높이 슬라이스)", "3D Analysis", "HeightSlicerToolSettings.xaml"),
    ("Point Cloud Cluster (포인트클라우드 클러스터)", "3D Analysis", "PointCloudClusterToolSettings.xaml"),
    ("Point Cloud Filter (포인트클라우드 필터)", "3D Analysis", "PointCloudFilterToolSettings.xaml"),
    ("Point Cloud Registration (포인트클라우드 정합)", "3D Analysis", "PointCloudRegistrationToolSettings.xaml"),
    ("Detection (딥러닝 검출)", "Deep Learning", "DetectionToolSettings.xaml"),
    ("Segmentation (분할)", "Deep Learning", "SegmentationToolSettings.xaml"),
    ("YOLO Segmentation", "Deep Learning", "YoloSegToolSettings.xaml"),
    ("Anomaly (이상 탐지)", "Deep Learning", "AnomalyToolSettings.xaml"),
    ("Classify (분류)", "Deep Learning", "ClassifyToolSettings.xaml"),
    ("Ensemble (앙상블)", "Judgment", "EnsembleToolSettings.xaml"),
    ("Result (결과/판정)", "Judgment", "ResultToolSettings.xaml"),
]

def attr(s, name):
    m = re.search(name + r'\s*=\s*"([^"]*)"', s)
    return m.group(1) if m else None

CTRL_KO = {
    "SliderParameter": "슬라이더",
    "EnumComboBoxParameter": "선택(목록)",
    "TextBoxParameter": "입력",
    "CheckBoxParameter": "체크박스",
    "ParamCodeLink": "파라미터 코드 연동",
    "ComboBoxParameter": "선택(목록)",
}

result = []
for disp, cat, fname in CORE:
    path = os.path.join(BASE, fname)
    if not os.path.exists(path):
        result.append({"tool": disp, "category": cat, "file": fname, "missing": True, "params": []})
        continue
    xml = open(path, encoding="utf-8").read()
    params = []
    # 각 controls:* 파라미터 컨트롤 추출
    for m in re.finditer(r"<controls:(\w+)\b(.*?)/?>", xml, re.S):
        ctrl, body = m.group(1), m.group(2)
        label = attr(body, "Label")
        if not label:
            continue
        pname = attr(body, "ParameterName") or ""
        mn = attr(body, "Minimum"); mx = attr(body, "Maximum")
        enum = re.search(r"\{x:Type\s+\w+:(\w+)\}", body)
        params.append({
            "label": label, "param": pname, "ctrl": CTRL_KO.get(ctrl, ctrl),
            "min": mn, "max": mx, "enum": enum.group(1) if enum else None,
        })
    result.append({"tool": disp, "category": cat, "file": fname, "params": params})

json.dump(result, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("tools:", len(result), "->", OUT)
for r in result:
    print(f"  {r['tool']}: {len(r['params'])} params" + (" [MISSING]" if r.get('missing') else ""))
