# 핵심 비전 툴의 ToolSettings XAML -> 파라미터 JSON (매뉴얼 #4 생성기 입력)
import json, re, os

BASE = r"D:\Repo\VMS\VMS.VisionSetup\Views\ToolSettings\Tools"
OUT = r"D:\Repo\VMS\docs\gs\_tool_params.json"

# (표시명, 카테고리, 파일)
CORE = [
    ("Grayscale (그레이 변환)", "Conversion", "GrayscaleToolSettings.xaml"),
    ("Threshold (이진화)", "Conversion", "ThresholdToolSettings.xaml"),
    ("Blur (블러)", "Preprocessing", "BlurToolSettings.xaml"),
    ("Morphology (형태학 연산)", "Preprocessing", "MorphologyToolSettings.xaml"),
    ("Edge Detection (에지 검출)", "Preprocessing", "EdgeDetectionToolSettings.xaml"),
    ("Histogram (히스토그램)", "Preprocessing", "HistogramToolSettings.xaml"),
    ("Blob Analysis (블롭 분석)", "Blob", "BlobToolSettings.xaml"),
    ("Caliper (캘리퍼 측정)", "Measurement", "CaliperToolSettings.xaml"),
    ("Line Fit (직선 피팅)", "Measurement", "LineFitToolSettings.xaml"),
    ("Circle Fit (원 피팅)", "Measurement", "CircleFitToolSettings.xaml"),
    ("Geometry (기하 측정)", "Measurement", "GeometryToolSettings.xaml"),
    ("Shape Match (형상 매칭)", "Pattern Matching", "ShapeMatchToolSettings.xaml"),
    ("Feature Match (특징 매칭)", "Pattern Matching", "FeatureMatchToolSettings.xaml"),
    ("Code Reader (코드 판독)", "Code Reading", "CodeReaderToolSettings.xaml"),
    ("OCR (문자 인식)", "Identification", "OCRToolSettings.xaml"),
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
