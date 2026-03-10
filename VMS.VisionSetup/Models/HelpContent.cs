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
                    ["UseContrastInvariant"] = "대비 불변 매칭 활성화. 활성화하면 조명 변화로 인한 대비 차이에 강건해집니다.\n그래디언트 방향만 비교하여 밝기 변화에 영향을 덜 받습니다.",
                    ["IsAutoTuneEnabled"] = "자동 튜닝 활성화. 활성화하면 매칭 실행 시 파라미터를 자동으로 최적화합니다.\n초기 설정이 어려운 경우 활성화하면 도움이 됩니다.",
                    ["CurvatureWeight"] = "곡률 가중치 (0~1). 에지 포인트 샘플링 시 곡률이 높은 부분(코너, 곡선)에 가중치를 부여합니다.\n• 0: 균일 샘플링\n• 0.5: 곡률 부분 가중 (권장)\n• 1.0: 곡률 부분만 집중",
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
                    // Segmentation
                    ["UseInternalThreshold"] = "내부 이진화 사용 여부. 활성화하면 ThresholdValue로 자체 이진화를 수행합니다. 비활성화 시 입력 이미지가 이미 이진화되어 있어야 합니다.",
                    ["ThresholdValue"] = "내부 이진화 임계값 (0-255). 이 값을 기준으로 픽셀을 흑/백으로 분류합니다.\n• 낮은 값: 더 많은 영역이 흰색(객체)으로 분류\n• 높은 값: 밝은 영역만 흰색으로 분류",
                    ["SegmentationPolarity"] = "검출 극성 선택:\n• LightOnDark: 어두운 배경 위의 밝은 객체 검출 (Binary)\n• DarkOnLight: 밝은 배경 위의 어두운 객체 검출 (BinaryInv)\n\nCognex VisionPro의 Polarity 설정과 동일한 개념입니다.",

                    // Area Filter
                    ["MinArea"] = "최소 블롭 면적 (픽셀²). 이 값보다 작은 블롭은 필터링됩니다.\n• 노이즈 제거에 유용 (예: 100 이상으로 설정)\n• 기본값: 100",
                    ["MaxArea"] = "최대 블롭 면적 (픽셀²). 이 값보다 큰 블롭은 필터링됩니다.\n• 너무 큰 객체를 제외할 때 사용\n• 기본값: 무제한",

                    // Shape Filter
                    ["MinCircularity"] = "최소 원형도 (0~1). 4π × 면적 / 둘레²로 계산됩니다.\n• 1.0: 완전한 원\n• 0.78: 정사각형\n• 값이 낮을수록 불규칙한 형상 허용",
                    ["MaxCircularity"] = "최대 원형도 (0~1). 원형에 가까운 블롭만 제외할 때 사용합니다.\n• 기본값: 1.0 (제한 없음)",
                    ["MinPerimeter"] = "최소 블롭 둘레 (픽셀). 이 값보다 작은 둘레를 가진 블롭을 제외합니다.\n• 기본값: 0 (제한 없음)",
                    ["MaxPerimeter"] = "최대 블롭 둘레 (픽셀). 이 값보다 큰 둘레를 가진 블롭을 제외합니다.\n• 기본값: 무제한",
                    ["MinAspectRatio"] = "최소 종횡비 (너비/높이). 가로로 긴 블롭만 검출할 때 사용합니다.\n• 1.0: 정사각형\n• 2.0: 가로가 세로의 2배\n• 기본값: 0 (제한 없음)",
                    ["MaxAspectRatio"] = "최대 종횡비 (너비/높이). 세로로 긴 블롭만 검출할 때 사용합니다.\n• 기본값: 무제한",
                    ["MinConvexity"] = "최소 볼록도 (0~1). 블롭 면적 / 볼록 껍질 면적으로 계산됩니다.\n• 1.0: 완전히 볼록한 형상\n• 값이 낮으면 오목한 형상도 허용\n• 기본값: 0 (제한 없음)",

                    // Sort & Limit
                    ["SortBy"] = "블롭 정렬 기준:\n• Area: 면적 기준\n• Perimeter: 둘레 기준\n• CenterX: X좌표 기준 (왼쪽→오른쪽)\n• CenterY: Y좌표 기준 (위→아래)\n• Circularity: 원형도 기준\n• AspectRatio: 종횡비 기준",
                    ["SortDescending"] = "내림차순 정렬 여부. 활성화하면 큰 값부터 정렬합니다.\n• 면적 기준 + 내림차순: 가장 큰 블롭이 첫 번째",
                    ["MaxBlobCount"] = "반환할 최대 블롭 수. 정렬 후 상위 N개만 결과에 포함됩니다.\n• 기본값: 100",

                    // Judgment
                    ["EnableJudgment"] = "양/불 판정 활성화. 활성화하면 면적 판정, 개수 판정 결과에 따라 PASS/FAIL이 결정됩니다.\n비활성화 시 블롭이 1개 이상 검출되면 항상 Success입니다.",
                    ["UseAreaJudgment"] = "면적 기반 판정 사용. 첫 번째 블롭(정렬 후 최상위)의 면적이 기준 면적 ± 허용 오차 범위 내에 있는지 판정합니다.",
                    ["ExpectedArea"] = "기준 면적 (픽셀²). 전체 블롭의 총 면적이 ExpectedArea - Minus ~ ExpectedArea + Plus 범위에 있으면 PASS.\n• 예: 기준 5000, +500/-300 → 4700~5500이면 합격",
                    ["AreaTolerancePlus"] = "면적 상한 허용 오차 (픽셀²). 기준 면적(ExpectedArea)에 이 값을 더한 것이 허용 상한입니다.\n• 기준 1000, +200 → 상한 1200",
                    ["AreaToleranceMinus"] = "면적 하한 허용 오차 (픽셀²). 기준 면적(ExpectedArea)에서 이 값을 뺀 것이 허용 하한입니다.\n• 기준 1000, -200 → 하한 800",
                    ["UseCountJudgment"] = "개수 기반 판정 사용. 검출된 블롭 개수가 설정 조건을 만족하는지 판정합니다.",
                    ["CountMode"] = "개수 판정 모드:\n• Equal: 정확히 N개일 때 합격\n• GreaterOrEqual: N개 이상일 때 합격\n• LessOrEqual: N개 이하일 때 합격\n• Range: Min~Max 범위 내일 때 합격",
                    ["ExpectedCount"] = "기준 블롭 개수.\n• Equal 모드: 정확히 이 수와 일치해야 합격\n• GreaterOrEqual 모드: 이 수 이상이면 합격\n• LessOrEqual 모드: 이 수 이하면 합격\n• Range 모드: 최소값으로 사용",
                    ["ExpectedCountMax"] = "최대 블롭 개수 (Range 모드에서만 사용). 블롭 수가 ExpectedCount ~ ExpectedCountMax 범위 내에 있으면 합격.",

                    // Display
                    ["DrawContours"] = "블롭 외곽선(컨투어) 그리기. 각 블롭이 다른 색상으로 표시됩니다.",
                    ["DrawBoundingBox"] = "바운딩 박스(외접 사각형) 그리기. 노란색 사각형으로 표시됩니다.",
                    ["DrawCenterPoint"] = "블롭 중심점 표시. 빨간색 십자 마커로 표시됩니다.",
                    ["DrawLabels"] = "블롭 번호 라벨 표시. 중심점 옆에 #0, #1, #2... 형태로 표시됩니다."
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
                    ["SearchWidth"] = "프로파일 투영 폭 (픽셀). 검색 라인에 수직인 방향의 폭으로, 이 범위 내 픽셀을 평균하여 프로파일을 생성합니다.\n• 클수록 노이즈에 강하지만 미세 엣지를 놓칠 수 있음\n• 작을수록 정밀하지만 노이즈에 민감",
                    ["SearchAxis"] = "탐색 방향 축 설정. ROI의 Width/Height 비율이 변해도 탐색 방향이 고정됩니다.\n• AlongWidth: ROI의 Width 축 방향으로 탐색\n• AlongHeight: ROI의 Height 축 방향으로 탐색\n\n화살표 방향이 실제 탐색 방향을 나타냅니다.",
                    ["Polarity"] = "검출할 엣지의 극성 (탐색 방향 기준):\n• DarkToLight: 탐색 방향을 따라 어두움→밝음으로 변하는 엣지\n• LightToDark: 탐색 방향을 따라 밝음→어두움으로 변하는 엣지\n• Any: 양방향 모두 검출",
                    ["EdgeThreshold"] = "엣지 검출 임계값 (그래디언트 크기). 높을수록 강한 엣지만 검출됩니다.\n• 낮은 값 (5~20): 약한 엣지도 검출 (노이즈 주의)\n• 중간 값 (30~60): 일반적인 사용\n• 높은 값 (70~100): 매우 강한 엣지만 검출",
                    ["FilterHalfWidth"] = "미분 필터의 반폭. 필터 커널 크기 = 2×반폭+1.\n• 작을수록 (1~2): 날카로운 엣지에 민감, 노이즈에 약함\n• 클수록 (4~10): 넓은 영역 평균, 노이즈에 강함\n\nGaussian Filter 사용 시 가우시안 커널 크기도 이 값으로 결정됩니다.",
                    ["Mode"] = "캘리퍼 동작 모드:\n• SingleEdge: 단일 엣지 검출 — 1개의 엣지 위치 반환\n• EdgePair: 엣지 쌍 검출 — 반대 극성의 두 엣지 사이 폭(Width) 측정",
                    ["MaxEdges"] = "최대 검출 엣지 수. 이 수를 초과하는 엣지 후보는 Score 순으로 잘립니다.\n• EdgePair 모드에서는 쌍 조합의 기반이 되므로 충분히 크게 설정",
                    ["ScorerMode"] = "엣지 점수 산정 모드. 여러 엣지 후보 중 최적을 선정하는 가중치 프리셋:\n• MaxContrast: 대비(밝기 차이)가 가장 큰 엣지 우선\n• Closest: 검색 라인 중앙에 가장 가까운 엣지 우선\n• BestOverall: 대비 + 위치 + 극성 종합 평가\n• Custom: 가중치를 수동 설정",
                    ["SelectionMode"] = "최종 엣지 선택 방식 (Cognex 호환):\n• Best: Score가 가장 높은 엣지 선택\n• First: 검색 시작점에서 가장 가까운 엣지 선택\n• Last: 검색 시작점에서 가장 먼 엣지 선택\n\nFirst/Last는 밀집된 패턴에서 특정 위치의 엣지만 선택할 때 유용합니다.",
                    ["ExpectedWidth"] = "예상 폭 (EdgePair 모드에서 사용). 엣지 쌍 사이 거리가 이 값에 가까운 쌍이 우선 선택됩니다.",
                    ["ProjectionMode"] = "프로파일 투영 방식:\n• Uniform: 균일 평균 — 검색 폭 내 모든 픽셀을 동일 가중치로 평균\n• Gaussian: 가우시안 가중 평균 — 중심선에 가까운 픽셀에 높은 가중치\n\nGaussian 모드는 엣지 양 끝단의 노이즈 영향을 줄여줍니다.",
                    ["UseGaussianFilter"] = "가우시안 1D 필터 사용 여부.\n• 활성화: 가우시안 평활화 + 가우시안 미분 커널 적용 (정밀도 향상)\n• 비활성화: 기존 이동 평균 미분 필터 사용 (빠름)\n\n고정밀 측정에는 활성화를 권장합니다.",
                    ["GaussianSigma"] = "가우시안 필터의 표준편차 (σ). Gaussian Filter 활성화 시 사용됩니다.\n• 작을수록 (0.3~1.0): 날카로운 엣지에 민감\n• 클수록 (2.0~5.0): 넓은 범위 평활화, 노이즈에 강함\n• 권장: 1.0~2.0",
                    ["UseNormalizedContrast"] = "정규화된 대비 스코어링 사용 여부.\n• 활성화: 그래디언트를 국부 평균 밝기로 나누어 정규화 — 조명 불균일 환경에 효과적\n• 비활성화: 절대 그래디언트 값으로 스코어링",
                    ["SubPixelMethod"] = "서브픽셀 보간 방법:\n• Parabolic: 3점 포물선 보간 (기본, 빠름)\n• Gaussian: 3점 가우시안 피팅 (대칭 피크에 정확)\n• Quartic5Point: 5점 다항식 피팅 (고정밀, 약간 느림)\n\n고정밀 측정에는 Gaussian 또는 Quartic5Point를 권장합니다."
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
                    ["NumCalipers"] = "사용할 캘리퍼 수. 기준선을 따라 균등 배치됩니다.\n• 많을수록 정확하지만 처리 시간 증가\n• 권장: 5~20",
                    ["SearchLength"] = "각 캘리퍼의 검색 길이 (기준선에 수직 방향). 엣지를 찾기 위해 탐색하는 거리입니다.\n• 짧으면 빠르지만 엣지가 범위 밖일 수 있음\n• 길면 넓은 범위 탐색 가능",
                    ["SearchWidth"] = "각 캘리퍼의 프로파일 투영 폭 (기준선 방향). 이 범위 내 픽셀을 평균하여 노이즈를 줄입니다.",
                    ["Polarity"] = "검출할 엣지의 극성 (탐색 방향 기준):\n• DarkToLight: 어두움→밝음 방향 엣지\n• LightToDark: 밝음→어두움 방향 엣지\n• Any: 양방향 모두 검출",
                    ["EdgeThreshold"] = "엣지 검출 임계값. 그래디언트 크기가 이 값을 초과해야 엣지로 인정됩니다.",
                    ["FilterHalfWidth"] = "미분 필터의 반폭. 필터 커널 크기 = 2×반폭+1.\n• 작을수록: 날카로운 엣지에 민감\n• 클수록: 노이즈에 강함",
                    ["FitMethod"] = "라인 피팅 방법:\n• LeastSquares: 최소자승법 — 빠르지만 이상치에 민감\n• RANSAC: 이상치에 강건함 — 부분적으로 가려진 엣지에 효과적\n• Huber: 로버스트 피팅 — LeastSquares와 RANSAC의 중간",
                    ["RansacThreshold"] = "RANSAC 이상치 판정 거리 (픽셀). 피팅 라인으로부터 이 거리 이상 떨어진 점은 이상치로 처리됩니다.",
                    ["MinFoundCalipers"] = "최소 검출 캘리퍼 수. 유효 엣지가 이보다 적으면 피팅 실패로 판정됩니다."
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
                    ["CenterPoint.X"] = "예상 원 중심의 X 좌표 (픽셀). ROI 설정 시 자동으로 동기화됩니다.",
                    ["CenterPoint.Y"] = "예상 원 중심의 Y 좌표 (픽셀). ROI 설정 시 자동으로 동기화됩니다.",
                    ["ExpectedRadius"] = "예상 원 반지름 (픽셀). 이 반지름을 기준으로 캘리퍼가 방사형으로 배치됩니다.\nROI 설정 시 자동으로 동기화됩니다.",
                    ["NumCalipers"] = "원주를 따라 배치할 캘리퍼 수.\n• 많을수록 정확하지만 처리 시간 증가\n• 권장: 8~36 (원 크기에 따라 조정)",
                    ["SearchLength"] = "각 캘리퍼의 검색 길이 (반경 방향). 예상 반지름을 중심으로 안쪽/바깥쪽으로 탐색하는 거리입니다.",
                    ["SearchWidth"] = "각 캘리퍼의 프로파일 투영 폭 (원주 접선 방향). 이 범위 내 픽셀을 평균하여 노이즈를 줄입니다.",
                    ["StartAngle"] = "검색 시작 각도 (도). 0°=오른쪽, 90°=아래쪽.\n전체 원: 0°, 부분 원호: 시작 위치 지정.",
                    ["EndAngle"] = "검색 끝 각도 (도). 0°=오른쪽, 90°=아래쪽.\n전체 원: 360°, 부분 원호: 끝 위치 지정.",
                    ["SearchDirection"] = "캘리퍼 탐색 방향:\n• InwardToOutward: 원 중심에서 바깥쪽으로 탐색\n• OutwardToInward: 바깥쪽에서 원 중심으로 탐색\n\n객체 안에서 바깥 엣지를 찾을 때는 InwardToOutward,\n바깥에서 안쪽 엣지를 찾을 때는 OutwardToInward를 사용합니다.",
                    ["Polarity"] = "검출할 엣지의 극성 (탐색 방향 기준):\n• DarkToLight: 어두움→밝음 방향 엣지\n• LightToDark: 밝음→어두움 방향 엣지\n• Any: 양방향 모두 검출",
                    ["EdgeThreshold"] = "엣지 검출 임계값. 그래디언트 크기가 이 값을 초과해야 엣지로 인정됩니다.",
                    ["FitMethod"] = "원 피팅 방법:\n• LeastSquares: 최소자승법 — 빠르고 일반적\n• RANSAC: 이상치에 강건함 — 부분적으로 가려진 원에 효과적"
                }
            },

            // 3D Analysis
            ["HeightSlicerTool"] = new ToolHelp
            {
                Name = "Height Slicer (높이 슬라이서)",
                Description = "3D 포인트 클라우드 데이터에서 지정된 높이 범위의 영역을 추출하여 2D 이미지로 변환합니다.",
                Usage = "3D 검사에서 특정 높이 범위의 객체만 분리할 때 사용됩니다. 높이 기반 결함 검출, 볼륨 측정의 전처리 단계로 활용됩니다.",
                CognexEquivalent = "CogIPOneImageTool (3D Height Slice)",
                Parameters = new Dictionary<string, string>
                {
                    ["MinZ"] = "최소 높이값 (mm). 이 높이 이하의 포인트는 제외됩니다.\n• 배경(바닥면)을 제거할 때 사용\n• 기준면 높이보다 약간 높게 설정",
                    ["MaxZ"] = "최대 높이값 (mm). 이 높이 이상의 포인트는 제외됩니다.\n• 관심 영역의 상한 설정\n• MinZ~MaxZ 범위의 포인트만 결과에 포함"
                }
            },

            // Judgment
            ["ResultTool"] = new ToolHelp
            {
                Name = "Result (결과 판정)",
                Description = "연결된 도구들의 결과를 종합하여 최종 OK/NG 판정을 수행합니다.",
                Usage = "검사 파이프라인의 마지막 단계에서 사용됩니다. 여러 도구의 성공/실패 결과를 논리 연산으로 결합하여 최종 판정을 내립니다.",
                CognexEquivalent = "CogResultAnalysisTool",
                Parameters = new Dictionary<string, string>
                {
                    ["JudgmentMode"] = "판정 모드:\n• AllPass: 연결된 모든 도구가 성공해야 OK (AND 논리)\n• AnyPass: 연결된 도구 중 하나라도 성공하면 OK (OR 논리)"
                }
            },

            // Code Reading
            ["CodeReaderTool"] = new ToolHelp
            {
                Name = "Code Reader (코드 리더)",
                Description = "이미지에서 QR 코드, 1D 바코드, DataMatrix, PDF417 등 다양한 코드를 인식하고 디코딩합니다.\nZXing.Net 엔진 기반이며, 저대비 코드(레이저 마킹 등)에 대해 CLAHE 자동 보정을 지원합니다.\n회전된 ROI(RectangleAffine) 사용 시 WarpAffine으로 이미지를 정규화하여 인식률을 극대화합니다.",
                Usage = "PCB 마킹 검사, 부품 추적, 물류 바코드 인식, 레이저 마킹 판독에 사용됩니다.\n• ROI를 코드 영역에 맞게 설정하면 인식 속도와 정확도가 향상됩니다.\n• 코드가 기울어진 경우 RectangleAffine ROI를 사용하여 회전 각도에 맞추세요.",
                CognexEquivalent = "CogIDTool (DataMatrix), CogBarcodeTool (1D Barcode)",
                Parameters = new Dictionary<string, string>
                {
                    // Detection
                    ["CodeReaderMode"] = "코드 인식 모드:\n• Auto: 모든 코드 타입 자동 인식 (느리지만 범용)\n• QRCode: QR 코드 전용 (빠름)\n• Barcode1D: 1D 바코드 전용 (CODE_128, CODE_39, EAN_13 등)\n• DataMatrix: DataMatrix 전용 (PCB 마킹에 주로 사용)\n• PDF417: PDF417 전용",
                    ["MaxCodeCount"] = "최대 인식 코드 수 (1~50).\n하나의 이미지에서 여러 코드를 동시에 인식할 때 결과 수를 제한합니다.\n• 기본값: 10",
                    ["TryHarder"] = "정밀 검출 모드. 활성화하면 더 많은 시간을 들여 코드를 찾습니다.\n• 활성화 (권장): 인식률 향상, 속도 약간 저하\n• 비활성화: 빠르지만 흐릿하거나 작은 코드를 놓칠 수 있음",

                    // Verification
                    ["EnableVerification"] = "텍스트 검증 활성화. 활성화하면 디코딩된 텍스트를 ExpectedText와 비교하여 PASS/FAIL을 판정합니다.\n비활성화 시 코드가 1개 이상 검출되면 항상 Success입니다.",
                    ["ExpectedText"] = "기대 텍스트. 디코딩된 코드 중 이 텍스트와 일치하는 코드가 있으면 PASS.\n• UseRegexMatch 비활성화: 정확히 일치해야 합격\n• UseRegexMatch 활성화: 정규식 패턴으로 매칭\n\n예시: \"ABC-12345\", \"^LOT-\\d{6}$\"",
                    ["UseRegexMatch"] = "정규식 매칭 사용 여부.\n• 비활성화: 디코딩 텍스트 == ExpectedText 정확히 일치\n• 활성화: Regex.IsMatch(디코딩 텍스트, ExpectedText)로 패턴 매칭\n\n정규식 예시:\n• ^SN\\d{8}$: \"SN\" + 숫자 8자리\n• ^(OK|PASS): \"OK\" 또는 \"PASS\"로 시작",

                    // Display
                    ["DrawOverlay"] = "검출 결과 오버레이 표시. 활성화하면 검출된 코드 위치에 폴리곤과 디코딩 텍스트를 그립니다.\n• 녹색: 인식 성공 (PASS)\n• 빨간색: 인식 실패 (FAIL)"
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
