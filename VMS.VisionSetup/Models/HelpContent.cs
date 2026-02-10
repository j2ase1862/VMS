using System.Collections.Generic;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 도구 및 파라미터 도움말 정보
    /// </summary>
    public static class HelpContent
    {
        #region Tool Descriptions

        private static readonly Dictionary<string, ToolHelp> _toolHelp = new()
        {
            // Image Processing
            ["GrayscaleTool"] = new ToolHelp
            {
                Name = "Grayscale",
                Description = "컬러 이미지를 그레이스케일(흑백)로 변환합니다.",
                Usage = "이미지 전처리의 첫 단계로 자주 사용됩니다. 대부분의 비전 알고리즘은 그레이스케일 이미지에서 더 빠르고 효과적으로 동작합니다.",
                CognexEquivalent = "CogImageConvertTool (Gray conversion)"
            },

            ["BlurTool"] = new ToolHelp
            {
                Name = "Blur (블러)",
                Description = "이미지에 블러(흐림) 효과를 적용하여 노이즈를 줄입니다.",
                Usage = "노이즈 제거, 엣지 검출 전 전처리, 이미지 스무딩에 사용됩니다.",
                CognexEquivalent = "CogIPOneImageTool (Gaussian filter)",
                Parameters = new Dictionary<string, string>
                {
                    ["BlurType"] = "블러 알고리즘 선택:\n• Gaussian: 가우시안 블러 (자연스러운 흐림)\n• Median: 미디언 필터 (소금-후추 노이즈에 효과적)\n• Bilateral: 양방향 필터 (엣지 보존)\n• Box: 박스 필터 (단순 평균)",
                    ["KernelSize"] = "커널(필터) 크기. 홀수만 가능 (3, 5, 7...). 클수록 더 강한 블러 효과.",
                    ["SigmaX"] = "X방향 표준편차 (Gaussian에서 사용). 0이면 커널 크기로 자동 계산.",
                    ["SigmaY"] = "Y방향 표준편차 (Gaussian에서 사용). 0이면 SigmaX와 동일."
                }
            },

            ["ThresholdTool"] = new ToolHelp
            {
                Name = "Threshold (이진화)",
                Description = "이미지를 임계값을 기준으로 흑백(0 또는 255)으로 변환합니다.",
                Usage = "객체와 배경 분리, Blob 분석 전처리, 문서 스캔 이미지 처리에 사용됩니다.",
                CognexEquivalent = "CogIPOneImageTool (Threshold)",
                Parameters = new Dictionary<string, string>
                {
                    ["ThresholdValue"] = "임계값 (0-255). 이 값보다 작으면 0, 크거나 같으면 MaxValue로 설정.",
                    ["MaxValue"] = "임계값 이상인 픽셀에 적용할 값 (보통 255).",
                    ["UseOtsu"] = "Otsu 알고리즘으로 최적 임계값 자동 계산. 바이모달 히스토그램에 효과적.",
                    ["UseAdaptive"] = "적응형 이진화 사용. 조명이 불균일한 이미지에 효과적.",
                    ["BlockSize"] = "적응형 이진화의 블록 크기. 홀수만 가능.",
                    ["CValue"] = "적응형 이진화에서 계산된 평균에서 뺄 상수값."
                }
            },

            ["EdgeDetectionTool"] = new ToolHelp
            {
                Name = "Edge Detection (엣지 검출)",
                Description = "이미지에서 엣지(경계선)를 검출합니다.",
                Usage = "객체 윤곽 검출, 형상 분석, 라인 피팅 전처리에 사용됩니다.",
                CognexEquivalent = "CogSobelEdgeTool, CogCannyEdgeTool",
                Parameters = new Dictionary<string, string>
                {
                    ["Method"] = "엣지 검출 알고리즘:\n• Canny: 가장 널리 사용되는 다단계 엣지 검출\n• Sobel: 1차 미분 기반, 방향성 엣지 검출\n• Laplacian: 2차 미분 기반\n• Scharr: Sobel의 개선 버전",
                    ["CannyThreshold1"] = "Canny의 낮은 임계값. 엣지 연결에 사용.",
                    ["CannyThreshold2"] = "Canny의 높은 임계값. 강한 엣지 검출에 사용.",
                    ["CannyApertureSize"] = "Sobel 연산자의 커널 크기 (3, 5, 7).",
                    ["L2Gradient"] = "L2 norm 사용 여부. 더 정확하지만 느림."
                }
            },

            ["MorphologyTool"] = new ToolHelp
            {
                Name = "Morphology (형태학적 연산)",
                Description = "이진 또는 그레이스케일 이미지에 형태학적 연산을 적용합니다.",
                Usage = "노이즈 제거, 객체 분리/연결, 홀 채우기, 엣지 추출에 사용됩니다.",
                CognexEquivalent = "CogIPOneImageTool (Morphology operations)",
                Parameters = new Dictionary<string, string>
                {
                    ["Operation"] = "형태학적 연산 종류:\n• Erode: 침식 - 객체 축소, 작은 노이즈 제거\n• Dilate: 팽창 - 객체 확대, 홀 채우기\n• Open: 열기 (침식→팽창) - 작은 노이즈 제거\n• Close: 닫기 (팽창→침식) - 작은 홀 채우기\n• Gradient: 팽창-침식, 윤곽선 추출\n• TopHat: 원본-열기, 밝은 영역 강조\n• BlackHat: 닫기-원본, 어두운 영역 강조",
                    ["KernelWidth"] = "구조 요소(커널)의 너비.",
                    ["KernelHeight"] = "구조 요소(커널)의 높이.",
                    ["Iterations"] = "연산 반복 횟수. 클수록 효과 강함."
                }
            },

            ["HistogramTool"] = new ToolHelp
            {
                Name = "Histogram (히스토그램)",
                Description = "이미지의 히스토그램 분석 및 평활화를 수행합니다.",
                Usage = "이미지 대비 개선, 조명 보정, 이미지 품질 분석에 사용됩니다.",
                CognexEquivalent = "CogHistogramTool",
                Parameters = new Dictionary<string, string>
                {
                    ["Operation"] = "히스토그램 연산:\n• Equalize: 히스토그램 평활화 - 대비 개선\n• CLAHE: 적응형 평활화 - 로컬 대비 개선\n• Analyze: 히스토그램 분석만 수행",
                    ["ClipLimit"] = "CLAHE의 대비 제한값. 클수록 대비 강함.",
                    ["TileGridWidth"] = "CLAHE 타일 그리드 너비.",
                    ["TileGridHeight"] = "CLAHE 타일 그리드 높이."
                }
            },

            // Pattern Matching
            ["FeatureMatchTool"] = new ToolHelp
            {
                Name = "Feature Match (에지 기반 기하학적 패턴 매칭)",
                Description = "에지 기반 Generalized Hough Voting과 그래디언트 내적 스코어링을 사용하여 학습된 패턴을 검출합니다.\n1단계: 검색 이미지의 에지 포인트가 후보 중심 위치에 투표 (Hough Voting)\n2단계: 투표 결과 주변에서 그래디언트 내적 점수로 정밀 보정",
                Usage = "부품 위치 검출, 정렬, 회전/스케일 변화가 있는 패턴 검출에 사용됩니다. ROI로 패턴을 학습하고, Search Region으로 검색 범위를 제한하면 속도가 더욱 향상됩니다.",
                CognexEquivalent = "CogPMAlignTool (PatMax)",
                Parameters = new Dictionary<string, string>
                {
                    ["CannyLow"] = "Canny 에지 검출의 낮은 임계값. 약한 에지 연결에 사용됩니다. 낮출수록 더 많은 에지가 검출됩니다.",
                    ["CannyHigh"] = "Canny 에지 검출의 높은 임계값. 강한 에지 판정에 사용됩니다. 높일수록 확실한 에지만 검출됩니다.",
                    ["MaxModelPoints"] = "학습 시 사용할 최대 모델 에지 포인트 수. 클수록 정확하지만 속도가 느려집니다.\n• 권장: 100~300",
                    ["AngleStart"] = "검색할 회전 각도 범위의 시작 (도).\n예: -45이면 반시계 방향 45°부터 검색.",
                    ["AngleExtent"] = "검색할 회전 각도 범위의 크기 (도).\n예: AngleStart=-45, AngleExtent=90이면 -45°~+45° 검색.",
                    ["AngleStep"] = "각도 검색 간격 (도). 작을수록 정밀하지만 느려집니다.\n• Hough Voting 단계에서는 max(AngleStep, 2°) 사용\n• 보정 단계에서는 AngleStep/2 사용",
                    ["MinScale"] = "검색할 최소 스케일. 0.9 = 90% 크기.",
                    ["MaxScale"] = "검색할 최대 스케일. 1.1 = 110% 크기.",
                    ["ScaleStep"] = "스케일 검색 간격. 작을수록 정밀하지만 보정 단계에서 계산량 증가.",
                    ["ScoreThreshold"] = "최종 그래디언트 내적 점수 임계값 (0~1). 이 값 이상이면 매칭 성공.\n• 0.5: 느슨한 매칭\n• 0.7: 일반적\n• 0.85: 엄격한 매칭",
                    ["NumLevels"] = "이미지 피라미드 레벨 수 (현재 미사용, 향후 확장용).",
                    ["Greediness"] = "조기 종료 탐욕도 (0~1). 높을수록 빠르지만 놓칠 가능성 증가.\n• 0: 모든 포인트 평가 (정확)\n• 0.8: 기본값 (속도/정확도 균형)\n• 1.0: 가장 빠름 (놓칠 위험)",
                    ["UseSearchRegion"] = "Search Region 사용 여부. 활성화하면 지정된 영역 내에서만 패턴을 검색합니다.",
                    ["SearchRegionX"] = "Search Region의 X 좌표 (픽셀).",
                    ["SearchRegionY"] = "Search Region의 Y 좌표 (픽셀).",
                    ["SearchRegionWidth"] = "Search Region의 너비 (픽셀).",
                    ["SearchRegionHeight"] = "Search Region의 높이 (픽셀)."
                }
            },

            // Blob Analysis
            ["BlobTool"] = new ToolHelp
            {
                Name = "Blob Analysis (블롭 분석)",
                Description = "이진화된 이미지에서 연결된 영역(블롭)을 찾고 분석합니다.",
                Usage = "객체 검출, 개수 카운팅, 면적/둘레 측정, 결함 검출에 사용됩니다.",
                CognexEquivalent = "CogBlobTool",
                Parameters = new Dictionary<string, string>
                {
                    ["UseInternalThreshold"] = "내부 이진화 사용 여부. 비활성화 시 입력 이미지가 이미 이진화되어 있어야 함.",
                    ["ThresholdValue"] = "내부 이진화 임계값.",
                    ["InvertPolarity"] = "극성 반전. 밝은 객체 대신 어두운 객체 검출.",
                    ["MinArea"] = "최소 블롭 면적 (픽셀). 이보다 작은 블롭 제외.",
                    ["MaxArea"] = "최대 블롭 면적 (픽셀). 이보다 큰 블롭 제외.",
                    ["MinCircularity"] = "최소 원형도 (0-1). 1에 가까울수록 원형.",
                    ["MaxCircularity"] = "최대 원형도 (0-1).",
                    ["MaxBlobCount"] = "반환할 최대 블롭 수.",
                    ["DrawContours"] = "블롭 외곽선 그리기.",
                    ["DrawBoundingBox"] = "바운딩 박스 그리기.",
                    ["DrawCenterPoint"] = "중심점 표시.",
                    ["DrawLabels"] = "블롭 번호 라벨 표시."
                }
            },

            // Measurement
            ["CaliperTool"] = new ToolHelp
            {
                Name = "Caliper (캘리퍼)",
                Description = "지정된 경로를 따라 엣지를 검출하고 거리를 측정합니다.",
                Usage = "폭 측정, 엣지 간 거리 측정, 위치 검출에 사용됩니다.",
                CognexEquivalent = "CogCaliperTool",
                Parameters = new Dictionary<string, string>
                {
                    ["StartPoint"] = "검색 시작점 좌표.",
                    ["EndPoint"] = "검색 끝점 좌표.",
                    ["SearchWidth"] = "검색 영역 너비 (픽셀).",
                    ["Polarity"] = "엣지 극성:\n• LightToDark: 밝음→어두움\n• DarkToLight: 어두움→밝음\n• Any: 양방향",
                    ["EdgeThreshold"] = "엣지 검출 임계값. 높을수록 강한 엣지만 검출.",
                    ["Mode"] = "캘리퍼 모드:\n• SingleEdge: 단일 엣지 검출\n• EdgePair: 엣지 쌍 검출 (폭 측정)",
                    ["ExpectedWidth"] = "예상 폭 (EdgePair 모드에서 사용)."
                }
            },

            ["LineFitTool"] = new ToolHelp
            {
                Name = "Line Fit (라인 피팅)",
                Description = "여러 점에서 엣지를 검출하고 직선을 피팅합니다.",
                Usage = "직선 엣지의 위치/각도 측정, 정렬 검사에 사용됩니다.",
                CognexEquivalent = "CogFindLineTool",
                Parameters = new Dictionary<string, string>
                {
                    ["NumCalipers"] = "사용할 캘리퍼 수. 많을수록 정확하지만 느림.",
                    ["SearchLength"] = "각 캘리퍼의 검색 길이.",
                    ["SearchWidth"] = "각 캘리퍼의 검색 너비.",
                    ["Polarity"] = "검출할 엣지의 극성.",
                    ["EdgeThreshold"] = "엣지 검출 임계값.",
                    ["FitMethod"] = "라인 피팅 방법:\n• LeastSquares: 최소자승법\n• RANSAC: 이상치에 강건함\n• Huber: 로버스트 피팅",
                    ["RansacThreshold"] = "RANSAC 이상치 판정 거리.",
                    ["MinFoundCalipers"] = "최소 검출 캘리퍼 수. 이보다 적으면 실패."
                }
            },

            ["CircleFitTool"] = new ToolHelp
            {
                Name = "Circle Fit (원 피팅)",
                Description = "원주 위의 여러 점에서 엣지를 검출하고 원을 피팅합니다.",
                Usage = "원형 객체의 중심/반지름 측정, 동심도 검사에 사용됩니다.",
                CognexEquivalent = "CogFindCircleTool",
                Parameters = new Dictionary<string, string>
                {
                    ["CenterPoint"] = "예상 원 중심 좌표.",
                    ["ExpectedRadius"] = "예상 원 반지름.",
                    ["NumCalipers"] = "원주를 따라 배치할 캘리퍼 수.",
                    ["SearchLength"] = "각 캘리퍼의 검색 길이 (반경 방향).",
                    ["SearchWidth"] = "각 캘리퍼의 검색 너비.",
                    ["StartAngle"] = "검색 시작 각도 (도).",
                    ["EndAngle"] = "검색 끝 각도 (도).",
                    ["Polarity"] = "검출할 엣지의 극성.",
                    ["EdgeThreshold"] = "엣지 검출 임계값.",
                    ["FitMethod"] = "원 피팅 방법:\n• LeastSquares: 최소자승법\n• RANSAC: 이상치에 강건함"
                }
            }
        };

        #endregion

        /// <summary>
        /// 도구 타입으로 도움말 정보 가져오기
        /// </summary>
        public static ToolHelp? GetToolHelp(string toolType)
        {
            return _toolHelp.TryGetValue(toolType, out var help) ? help : null;
        }

        /// <summary>
        /// 파라미터 도움말 가져오기
        /// </summary>
        public static string? GetParameterHelp(string toolType, string parameterName)
        {
            if (_toolHelp.TryGetValue(toolType, out var help) &&
                help.Parameters != null &&
                help.Parameters.TryGetValue(parameterName, out var paramHelp))
            {
                return paramHelp;
            }
            return null;
        }
    }

    /// <summary>
    /// 도구 도움말 정보
    /// </summary>
    public class ToolHelp
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Usage { get; set; } = "";
        public string CognexEquivalent { get; set; } = "";
        public Dictionary<string, string>? Parameters { get; set; }
    }
}
