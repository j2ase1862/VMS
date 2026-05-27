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

            ["YoloSegTool"] = new ToolHelp
            {
                Name = "YOLOv8-seg (인스턴스 분할)",
                Description = "Ultralytics YOLOv8/v11 인스턴스 분할 ONNX 추론. 한 이미지에서 객체마다 박스 + 클래스 + 픽셀 마스크를 동시 출력.\n출력 두 텐서: output0(detections + 32 mask coefs) + output1(32 prototype masks). 후처리: NMS → coef×prototypes → sigmoid → threshold.",
                Usage = "1) Ultralytics에서 학습한 .pt를 .onnx로 export(yolo export model=best.pt format=onnx). 2) Model Path 지정. 3) Confidence / IoU / Mask Threshold 조정. 4) Run → 결과 Data의 Inst{i}_* 키로 각 인스턴스 정보 확인.",
                CognexEquivalent = "ViDi Blue Locate (인스턴스 모드) / Red Supervised",
                Parameters = new Dictionary<string, string>
                {
                    ["ModelPath"] = "YOLOv8-seg ONNX 모델 파일 경로 (.onnx). Ultralytics export 형식.",
                    ["InputSize"] = "추론 입력 크기. 학습 시 사용한 imgsz와 일치 권장.\n• 320: 빠름, 정확도 ↓\n• 640: 기본\n• 1280: 정밀, 느림",
                    ["ConfidenceThreshold"] = "객체 confidence 임계값. 이하 박스는 버림.\n• 0.1~0.2: 많이 검출 (오탐 ↑)\n• 0.25: 기본\n• 0.5+: 보수적 (놓침 ↑)",
                    ["IouThreshold"] = "NMS IoU 임계값. 같은 클래스의 중복 박스 억제 기준.\n• 0.3: 엄격 (가까운 객체도 분리)\n• 0.45: 기본\n• 0.7+: 관대 (중복 잘 허용)",
                    ["MaskThreshold"] = "인스턴스 마스크 sigmoid 후 binary 임계값.\n• 0.3: 마스크가 객체보다 약간 크게\n• 0.5: 기본\n• 0.7: 마스크가 객체보다 약간 작게 (코어 영역만)",
                    ["ShowOverlay"] = "각 인스턴스 마스크를 컬러 반투명 오버레이로 표시.",
                    ["OverlayOpacity"] = "마스크 오버레이 투명도 (0~1).",
                    ["DrawBoxes"] = "박스 + 클래스명 + 점수 라벨 표시 여부."
                }
            },

            ["ColorExtractTool"] = new ToolHelp
            {
                Name = "Color Extract (HSV 다중 모델 추출)",
                Description = "입력 컬러 이미지를 HSV로 변환한 후, 학습된 컬러 모델들의 inRange 결과를 OR로 합산해 마스크를 생성.\n같은 색의 미묘한 변종(조명·그라데이션·질감)을 여러 모델로 등록해 견고하게 추출.\n특정 컬러 객체 검출 / 결함 영역 분할 / 후속 BlobTool 입력에 사용.",
                Usage = "1) Models에서 모델 선택(기본 'Model 1' 자동 생성). 2) 'Use Training Region' 체크 + ROI를 색 영역에 그림. 3) 'Train Selected Model' 클릭하면 H/S/V 범위 자동 설정. 4) 변종이 있으면 '+ Add'로 새 모델 추가 후 다른 영역 학습. 5) 결과 마스크를 BlobTool 등에 연결.",
                CognexEquivalent = "CogColorExtractorTool",
                Parameters = new Dictionary<string, string>
                {
                    ["HueMin"] = "선택된 모델의 색상(Hue) 최소값 (0-179, OpenCV HSV 기준).\n• 빨강: 0~10 / 160~179 (Min>Max로 입력해 두 구간 자동 합산)\n• 주황: 10~20\n• 노랑: 20~30\n• 초록: 35~85\n• 파랑: 100~130\n• 보라: 130~160",
                    ["HueMax"] = "선택된 모델의 색상(Hue) 최대값 (0-179).\nHueMin > HueMax 면 빨간색처럼 원형 구간(예: 170→10)을 자동으로 두 번 inRange 합니다.",
                    ["SaturationMin"] = "채도 최소값 (0-255). 낮으면 옅은 색·회색까지 포함. 산업 영상에서는 보통 50 이상 권장.",
                    ["SaturationMax"] = "채도 최대값 (0-255). 보통 255.",
                    ["ValueMin"] = "명도 최소값 (0-255). 너무 어두운 영역 제외용. 보통 50 이상.",
                    ["ValueMax"] = "명도 최대값 (0-255). 너무 밝은 반사·할레이션 제외용. 보통 255.",
                    ["MorphKernelSize"] = "후처리 모폴로지 커널 크기 (0이면 비활성).\n• 0: 처리 안 함 (가장 빠름)\n• 3~5: 작은 노이즈 제거 (Open) + 작은 구멍 채우기 (Close)\n• 7+: 강한 후처리, 결과 형상이 부풀어 보일 수 있음",
                    ["InvertMask"] = "마스크 반전. true면 범위 밖 픽셀이 추출됨 (배경 추출용).",
                    ["ShowOverlay"] = "결과 오버레이에 마스크 영역을 자홍색 반투명으로 표시.",
                    ["OverlayOpacity"] = "오버레이 투명도 (0~1).",
                    ["UseSearchRegion"] = "Search Region 사용 여부. 활성화하면 지정된 영역 안에서만 컬러 추출 → 속도 향상 + 외부 노이즈 차단.\n주의: Training Region(UseROI)과 별개. UseROI는 학습용, UseSearchRegion은 Execute 검색용.",
                    ["SearchRegionX"] = "Search Region의 X 좌표 (픽셀).",
                    ["SearchRegionY"] = "Search Region의 Y 좌표 (픽셀).",
                    ["SearchRegionWidth"] = "Search Region의 너비 (픽셀).",
                    ["SearchRegionHeight"] = "Search Region의 높이 (픽셀)."
                }
            },

            ["PointCloudRegistrationTool"] = new ToolHelp
            {
                Name = "PointCloud Registration (ICP 정합)",
                Description = "Iterative Closest Point 알고리즘으로 Reference 점군과 Source 점군(VisionService.CurrentPointCloud)을 정합.\n4x4 변환 행렬을 산출하고 ApplyTransformToSource가 true이면 Source에 적용해 정합된 점군으로 갱신.\n다중 시야각 스캔 병합, 부품 위치 측정, 정렬 오차 보정에 사용.",
                Usage = "1) 기준 자세에서 점군을 획득 → 'Save Current as Reference'로 .vpc 파일에 저장. 2) 이후 새 점군 획득 시 이 도구를 Run하면 ICP가 정합 변환을 계산 → 결과 행렬은 Data에 노출, 정합된 점군은 후속 3D 도구가 사용.",
                CognexEquivalent = "(PCL/Open3D ICP)",
                Parameters = new Dictionary<string, string>
                {
                    ["ReferencePath"] = ".vpc 파일 경로 (Reference 점군). 'Save Current as Reference' 버튼으로 현재 점군을 저장하면 자동 설정.",
                    ["MaxIterations"] = "ICP 반복 최대 횟수. 수렴 안 되어도 이 횟수에서 중단.\n• 20~30: 빠름, 거친 정합\n• 50: 기본 (균형)\n• 100~200: 정밀, 느림",
                    ["Tolerance"] = "수렴 임계 (mm). 반복 간 변환 변화량이 이 값보다 작으면 수렴 판정 후 종료.\n• 0.001~0.005: 매우 정밀\n• 0.01: 기본\n• 0.05~0.1: 빠른 수렴, 정밀도 ↓",
                    ["ApplyTransformToSource"] = "true: 산출된 변환을 Source에 적용 후 VisionService.CurrentPointCloud 갱신 (후속 도구가 정합된 점군 사용).\nfalse: 변환 행렬만 산출, Source는 그대로 유지."
                }
            },

            ["PointCloudClusterTool"] = new ToolHelp
            {
                Name = "PointCloud Cluster (유클리드 클러스터링)",
                Description = "거리 tolerance 이내 점들을 BFS로 연결해 클러스터를 형성. 그리드 해싱으로 O(N) 평균 복잡도.\n분리된 객체 검출, 노이즈 클러스터 제거, 부품 개수 카운트 등에 사용.",
                Usage = "1) Tolerance를 두 점이 같은 객체로 묶일 만큼의 거리로 설정. 2) Min/MaxPoints로 노이즈/배경 필터링. 3) OutputMode 선택: LargestOnly(가장 큰 객체만 남김), AllMerged(노이즈 제거 합산), KeepOriginal(메트릭만).\n결과 Data에서 ClusterCount + 각 Cluster{i}_Points/CenterX/Y/Z 확인 가능.",
                CognexEquivalent = "(PCL EuclideanClusterExtraction)",
                Parameters = new Dictionary<string, string>
                {
                    ["Tolerance"] = "동일 클러스터 판정 거리 (mm). 두 점이 이 거리 이내면 같은 클러스터.\n• 1~3: 매우 가까운 점만 (조밀한 객체)\n• 5: 기본\n• 10~20: 느슨한 묶음 (희소 점군)",
                    ["MinPoints"] = "클러스터 최소 점 수. 이하면 노이즈로 간주하고 무시.\n• 20~50: 작은 객체 검출\n• 100: 기본\n• 500+: 큰 객체만",
                    ["MaxPoints"] = "클러스터 최대 점 수. 이상이면 배경/큰 덩어리로 간주하고 무시.\n• 점군 전체 크기 - 1 (큰 값): 사실상 제한 없음\n• 점군의 50%: 배경 제외",
                    ["MaxReportedClusters"] = "결과 Data에 노출할 상위 클러스터 수 (실제로는 모두 처리, 표시 제한만).",
                    ["OutputMode"] = "결과 처리 모드:\n• LargestOnly: VisionService.CurrentPointCloud를 가장 큰 클러스터로 교체 (객체 분리용)\n• AllMerged: 활성 클러스터들을 모두 합산 (작은 노이즈 제거)\n• KeepOriginal: CurrentPointCloud 유지, 메트릭만"
                }
            },

            ["PointCloudFilterTool"] = new ToolHelp
            {
                Name = "PointCloud Filter (점군 필터링)",
                Description = "3D 카메라로 획득한 점군을 다운샘플(VoxelGrid)하고 통계적 outlier(SOR)를 제거.\n결과는 VisionService.CurrentPointCloud에 갱신되어 후속 도구(HeightSlicer, PlaneFit, Geometry3D)가 사용.\n이미지 출력은 입력 그대로 pass-through.",
                Usage = "3D 카메라 grab 후 첫 단계로 배치 권장. VoxelGrid는 거의 항상 활성화(점 수 감소 → 후속 속도 향상). SOR은 노이즈가 많을 때만 (KNN 검색이라 비용 큼).",
                CognexEquivalent = "(PCL VoxelGrid + StatisticalOutlierRemoval)",
                Parameters = new Dictionary<string, string>
                {
                    ["EnableVoxelGrid"] = "VoxelGrid 다운샘플 활성화. 같은 voxel 안의 모든 점을 1개로 합쳐 점 수를 크게 줄임.",
                    ["VoxelSize"] = "Voxel 한 변의 길이 (mm). 작을수록 정밀도 ↑, 점 수 ↑, 속도 ↓.\n• 0.1~0.5: 정밀 (작은 부품 측정)\n• 1.0: 기본 (일반)\n• 2.0~5.0: 거친 다운샘플 (속도 우선)",
                    ["EnableSor"] = "Statistical Outlier Removal 활성화. 통계적 outlier(노이즈/스파이크) 제거. KNN 검색이라 큰 점군에서는 시간 비용이 큼.",
                    ["SorK"] = "이웃 점 개수 (K-Nearest Neighbors). 각 점의 K개 이웃과의 평균 거리를 계산해 통계 분포 산출.\n• 10~20: 빠름, 거친 판정\n• 30: 기본 (균형)\n• 50~100: 정밀, 느림",
                    ["SorStddev"] = "표준편차 배수. 평균거리 ± 이 값 × σ 밖의 점은 outlier로 제거.\n• 1.0: 엄격 (많이 제거)\n• 2.0: 기본 (균형)\n• 3.0~5.0: 느슨 (강한 outlier만 제거)"
                }
            },

            ["ColorMatchTool"] = new ToolHelp
            {
                Name = "Color Match (Lab ΔE 거리 매칭)",
                Description = "BGR을 Lab 색공간으로 변환한 후, 학습된 컬러 패치의 평균 Lab과 픽셀별 ΔE76 거리(유클리드)를 계산.\n거리가 Tolerance 이내인 픽셀을 매칭으로 표시. 활성 모델들의 매칭 결과는 OR로 합산.\nLab는 인지 균일 색공간이라 HSV inRange보다 조명 변화에 강건.",
                Usage = "1) Models에서 모델 선택(기본 'Model 1' 자동 생성). 2) 'Use Training Region' 체크 + ROI를 학습 색 영역에 그림. 3) 'Train Selected Model' 클릭 — 평균 Lab(L, a, b) 자동 계산. 4) ColorTolerance(ΔE 임계)로 매칭 영역 조정. 5) 변종이 있으면 '+ Add'로 새 모델 추가 후 학습.",
                CognexEquivalent = "CogColorMatchTool",
                Parameters = new Dictionary<string, string>
                {
                    ["MeanL"] = "선택 모델의 학습 평균 L (밝기 채널, OpenCV 8-bit Lab 0-255).\n• 어두운 색: 50 이하\n• 중간 밝기: 100~180\n• 밝은 색/흰색: 200 이상",
                    ["MeanA"] = "선택 모델의 학습 평균 a (적-녹 축, OpenCV 8-bit 0-255, 중성=128).\n• 128 미만: 녹 계열\n• 128 초과: 적 계열",
                    ["MeanB"] = "선택 모델의 학습 평균 b (황-청 축, OpenCV 8-bit 0-255, 중성=128).\n• 128 미만: 청 계열\n• 128 초과: 황 계열",
                    ["ColorTolerance"] = "ΔE76 거리 임계값. 픽셀의 Lab가 모델 평균에서 이 거리 이내면 매칭.\n• 5~10: 매우 엄격 (정밀한 색 일치)\n• 15~25: 일반 (기본 25, 권장)\n• 30~50: 느슨 (조명 변동 큰 환경)\n• 50+: 매우 느슨 (다른 색까지 잡힐 수 있음)",
                    ["MorphKernelSize"] = "후처리 모폴로지 커널 크기 (0이면 비활성).\n• 0: 처리 안 함 (가장 빠름)\n• 3~5: 작은 노이즈 제거 (Open) + 작은 구멍 채우기 (Close)\n• 7+: 강한 후처리",
                    ["ShowOverlay"] = "결과 오버레이에 매칭 영역을 초록색 반투명으로 표시.",
                    ["OverlayOpacity"] = "오버레이 투명도 (0~1).",
                    ["UseSearchRegion"] = "Search Region 사용 여부. 활성화하면 지정된 영역 안에서만 매칭 → 속도 향상 + 외부 노이즈 차단.\n주의: Training Region(UseROI)과 별개. UseROI는 학습용, UseSearchRegion은 Execute 검색용.",
                    ["SearchRegionX"] = "Search Region의 X 좌표 (픽셀).",
                    ["SearchRegionY"] = "Search Region의 Y 좌표 (픽셀).",
                    ["SearchRegionWidth"] = "Search Region의 너비 (픽셀).",
                    ["SearchRegionHeight"] = "Search Region의 높이 (픽셀)."
                }
            },

            ["ShapeMatchTool"] = new ToolHelp
            {
                Name = "Shape Match (NCC + 피라미드 형상 매칭)",
                Description = "정규화 상관(NCC) + 다중 해상도 피라미드 + Coarse-to-Fine 회전·스케일 탐색으로 학습된 형상을 검출합니다.\n1단계 Coarse: 1/2^N 해상도에서 큰 각도 스텝으로 모든 후보 평가\n2단계 Fine: 풀 해상도에서 상위 N개 후보 주변만 정밀화\nFeatureMatchTool의 에지 기반 매칭에 비해 단순하지만, 텍스처 없는 단색 형상·로고·인쇄 마크에 안정적입니다.",
                Usage = "Training Region에 패턴을 학습(Train Template) → Search Region으로 검색 범위 제한 → Run. 회전 범위는 ±180° 고정이며 AngleStep으로 정밀도를 조절합니다.",
                CognexEquivalent = "CogPMAlignTool (PatMax) — 단순화된 NCC 버전",
                Parameters = new Dictionary<string, string>
                {
                    ["AngleStep"] = "회전 각도 검색 간격 (도). 작을수록 정밀하지만 매칭 횟수 증가.\n• 5°: 기본 (속도/정확도 균형)\n• 2°: 정밀, 느림\n• 10° 이상: 빠름, 정밀도 ↓",
                    ["MinScale"] = "검색할 최소 스케일 배수.\n• 0.8: 학습 대비 80% 크기까지 허용\n• 1.0: 스케일 변화 없음 (가장 빠름)",
                    ["MaxScale"] = "검색할 최대 스케일 배수.\n• 1.2: 학습 대비 120% 크기까지 허용\n• 1.0: 스케일 변화 없음",
                    ["ScaleStep"] = "스케일 검색 간격.\n• 0.05~0.1 권장\n• 너무 작으면 매칭 횟수 폭증",
                    ["ScoreThreshold"] = "매칭 점수 임계값 (0~1). 이 값 이상이면 Pass.\n• 0.5: 느슨한 매칭\n• 0.7: 기본\n• 0.85: 엄격한 매칭\nNCC 특성상 조명 변화에는 강건하지만 부분 가림에는 약합니다.",
                    ["NumPyramidLevels"] = "피라미드 단계 수 (1~4).\n• 1: 풀 해상도만 (단순, 느림)\n• 2: 1/2 다운샘플 (기본)\n• 3: 1/4 다운샘플 (빠름, 작은 객체에선 위험)\n• 4: 1/8 다운샘플 (매우 빠름, 대형 객체용)",
                    ["TopCandidates"] = "Fine pass에서 정밀화할 상위 후보 수.\n• 1: 가장 빠르나 최적해를 놓칠 수 있음\n• 3: 기본 (균형)\n• 5+: 안전, 느림",
                    ["MaxInstances"] = "찾을 최대 인스턴스 수.\n• 1: 단일 매칭 (기존 동작, 가장 빠름)\n• 2~20: 한 이미지에 같은 패턴이 여러 개 있을 때 (예: PCB의 동일 부품 N개)\n다중 모드에서는 매칭맵의 로컬 피크를 여러 개 추출 + NMS로 중복 제거.",
                    ["NmsDistanceFactor"] = "NMS(중복 억제) 중심 거리 임계 배수. 매칭 박스의 짧은 변 × 이 값보다 가까운 매칭들은 중복으로 간주, 점수 높은 것만 살림.\n• 0.3: 매우 좁게 (박스가 거의 겹쳐도 별개)\n• 0.5: 기본\n• 1.0: 박스 폭만큼 떨어져야 별개\n• 1.5~2.0: 매우 넓게 (정말 떨어진 것만)",
                    ["UseSearchRegion"] = "Search Region 사용 여부. 활성화하면 지정 영역에서만 검색하므로 속도가 영역 크기에 비례해 빨라집니다.",
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

            // Identification
            ["OCRTool"] = new ToolHelp
            {
                Name = "OCR (문자 인식)",
                Description = "듀얼 엔진 OCR 문자 인식 도구입니다.\nTesseract 5 LSTM 또는 PP-OCRv4 ONNX 엔진을 선택할 수 있습니다.\n유통기한, 시리얼 번호, 로트 번호 등 산업용 문자를 인식합니다.\n폰트 학습 없이 범용적으로 영어, 한국어, 일본어, 중국어를 인식할 수 있습니다.",
                Usage = "PCB 마킹 판독, 부품 라벨 검사, 유통기한 확인, 시리얼 번호 검증에 사용됩니다.\n• ROI를 문자 영역에 맞게 설정하면 인식 속도와 정확도가 향상됩니다.\n• 기울어진 문자는 RectangleAffine ROI로 WarpAffine 정규화를 적용하세요.\n• tessdata 폴더에 언어별 .traineddata 파일이 필요합니다.",
                CognexEquivalent = "CogOCRMaxTool, CogOCRMaxFontTool",
                Parameters = new Dictionary<string, string>
                {
                    // Engine
                    ["OcrEngine"] = "OCR 엔진 선택:\n• Tesseract: Tesseract 5 LSTM 기반 (tessdata 필요, 세밀한 전처리/PSM 제어)\n• PPOcrOnnx: PP-OCRv4 ONNX Runtime 기반 (모델 자동 다운로드, 자체 전처리, 산업용 우수)\n\n──────────────────\n엔진별 강약 (산업 라벨 기준)\n──────────────────\n\n[PPOcrOnnx 권장 — 대부분의 산업 케이스]\n• DB Detection 모델이 텍스트 영역을 먼저 자동으로 찾고 → CRNN 인식 → 노이즈/배경 면역\n• PP-OCRv4는 SynthText, ICDAR, MTWI 등 대규모 산업/다국어 데이터셋으로 학습\n• 회전, 원근, 저대비, 이질 폰트(스텐실, 도트 매트릭스, 임프린트)에 강건\n• 흰배경/검은배경 자동 처리 (Invert 불필요)\n• 자체 전처리 수행 — Auto Preprocess/Invert/Denoise/Target Height 설정 무시\n• Char Whitelist + Output Format으로 산업용 검증 완성\n\n[Tesseract — 제한적 사용]\n• 학습 분포: 주로 문서 스캔 (책, 신문, 폼). 산업 폰트는 학습 분포 밖\n• Detection 단계 없음 — 입력 전체를 텍스트로 가정. ROI에 테두리/잡음 섞이면 line finder 실패\n• 검은 글자 + 흰 배경 + 30~40px 이상 글자 높이가 사실상 전제\n• Otsu 이진화가 ideal ratio(20%)에 못 맞추는 백/흑 반전 라벨에 약함\n• 유리한 케이스: 깨끗한 영문 문서 스캔, Times/Arial/Courier 단순 폰트, 단일 단어 (PSM=SingleWord) + 매우 tight한 ROI\n• 한글/일본어 일부 케이스에서 PP-OCR 사전 부족 시 대안\n\n[더 강력한 검증이 필요할 때]\n• OCV (Optical Character Verification) 도구 — 학습된 폰트 라이브러리로 문자 단위 NCC 매칭. 정해진 폰트의 PASS/FAIL 검증에 최적.",

                    // Detection
                    ["Language"] = "OCR 인식 언어:\n• English: 영어 (eng)\n• Korean: 한국어 (kor)\n• Japanese: 일본어 (jpn)\n• ChineseSimplified: 중국어 간체 (chi_sim)\n• EnglishKorean: 영어+한국어 동시 인식 (eng+kor)",
                    ["PageSegMode"] = "페이지 분할 모드 (텍스트 레이아웃 해석 방법):\n• Auto: 자동 감지\n• SingleBlock: 단일 텍스트 블록 (기본 문서)\n• SingleLine: 단일 라인 (시리얼 번호 등)\n• SingleWord: 단일 단어\n• SingleChar: 단일 문자\n• VerticalBlock: 세로 텍스트",
                    ["EngineMode"] = "OCR 엔진 모드:\n• LstmOnly: LSTM 신경망만 사용 (기본, 정확도 우선)\n• Combined: Legacy + LSTM 결합 (호환성)\n• LegacyOnly: 기존 Tesseract 엔진 (속도 우선)",
                    ["CharacterWhitelist"] = "인식 허용 문자 제한. 빈 문자열이면 모든 문자를 인식합니다.\nTesseract와 PP-OCR 모두 지원.\n\n예시:\n• \"0123456789\": 숫자만 인식\n• \"0123456789ABCDEF\": 16진수 문자만\n• \"0123456789-/\": 날짜 형식 (2024-01/15)\n\n주요 효과:\n• '/' ↔ '7', 'O' ↔ '0', 'l' ↔ '1' 같은 시각적 혼동 차단\n• 산업용 시리얼/LOT/날짜에 strongly 추천",
                    ["ConfidenceThreshold"] = "최소 신뢰도 임계값 (0~100%).\n이 값 미만의 인식 결과는 무시됩니다.\n• 기본값: 40%\n• 높일수록 오인식 감소, 미인식 증가",

                    // Preprocessing
                    ["AutoPreprocess"] = "자동 전처리 활성화.\n• CLAHE 대비 향상 → Otsu 이진화 → 모폴로지 노이즈 제거\n• 비활성화: 원본 그레이스케일 이미지 그대로 OCR 수행\n• 이미 전처리된 이미지를 입력받는 경우 비활성화하세요.",
                    ["InvertImage"] = "이미지 반전.\nTesseract는 흰 배경에 검은 글씨를 선호합니다.\n• 검은 배경에 흰 글씨인 경우 활성화하세요.\n• 레이저 마킹(밝은 각인)에 유용합니다.",
                    ["TargetTextHeight"] = "목표 문자 높이(px). 이미지의 문자가 이 높이보다 작으면 자동으로 확대합니다.\nTesseract는 30~40px 이상의 문자 높이에서 최적 성능을 발휘합니다.\n• 기본값: 40\n• 0: 스케일업 비활성화\n• 최대 4배까지 확대",
                    ["DenoiseLevel"] = "노이즈 제거 강도. 이진화 전에 GaussianBlur를 적용하여 픽셀 노이즈를 제거합니다.\n문자 외곽선이 부드러워져 인식률이 향상됩니다.\n• 0: 없음\n• 1: 약 (3x3 커널, 기본값)\n• 2: 중 (5x5 커널)\n• 3: 강 (7x7 커널)\n\n강한 노이즈 제거는 얇은 문자를 훼손할 수 있으므로 주의하세요.",
                    ["DotMatrixMode"] = "도트 매트릭스 모드. 도트 프린트로 인쇄된 끊어진 문자를 연결합니다.\n• 활성화: 팽창(Dilation)으로 인접 도트를 연결 → 오프닝으로 잔여 노이즈 제거\n• 유통기한 도트 마킹, 잉크젯 인쇄 등에 효과적입니다.\n• 일반 인쇄 문자에는 비활성화하세요 (문자가 두꺼워져 인식률 저하 가능).",

                    // Output Format
                    ["FormatPreset"] = "결과 형식 프리셋. 산업 라벨에서 자주 쓰는 날짜/시간/LOT 패턴을 자동 적용해 모델 오인식 (예: '/' → '7')을 위치 기반으로 강제 보정합니다.\n\n토큰 규칙:\n• D/M/Y/H/S/f = 숫자 자리 (해당 자리가 숫자가 아니면 매칭 실패)\n• L = 영문자, A = 영숫자, ? = 임의\n• 그 외 모든 char (/, -, :, ., 공백 등) = 리터럴. OCR이 무엇을 봤든 그 자리에 강제 치환\n\n예: DD/MM/YYYY 프리셋 + OCR 결과 '31703/2099' → 위치 2의 '7'이 '/'로 강제 치환 → '31/03/2099'.\n\n매칭 실패 시 원본 텍스트 그대로 반환 (RawText 키에도 노출). 자유 텍스트는 'None' 선택 또는 'Custom' + 빈 입력.",
                    ["CustomOutputFormat"] = "Custom 선택 시 직접 입력하는 패턴 목록 (한 줄에 하나).\n여러 패턴이 있으면 길이 긴 순으로 시도되어 첫 매칭 성공 패턴이 채택됩니다.\n\n예시:\nDD/MM/YYYY HH:MM:SS.fff\nDD/MM/YYYY HH:MM:SS\nDD/MM/YYYY HH:MM\nDD/MM/YYYY",

                    // Verification
                    ["EnableVerification"] = "텍스트 검증 활성화. 인식된 텍스트를 ExpectedText와 비교하여 PASS/FAIL 판정합니다.\n비활성화 시 문자가 인식되고 신뢰도가 충분하면 항상 Success입니다.",
                    ["ExpectedText"] = "기대 텍스트. 인식 결과와 비교할 문자열입니다.\n• UseRegexMatch 비활성화: 대소문자 무시 정확히 일치\n• UseRegexMatch 활성화: 정규식 패턴 매칭\n\n예시: \"LOT-12345\", \"^SN\\d{8}$\"",
                    ["UseRegexMatch"] = "정규식 매칭 사용 여부.\n• 비활성화: 인식 텍스트 == ExpectedText (대소문자 무시)\n• 활성화: Regex.IsMatch(인식 텍스트, ExpectedText)\n\n예시: ^\\d{4}[-/]\\d{2}[-/]\\d{2}$ → 날짜 형식 검증",

                    // Display
                    ["DrawOverlay"] = "인식 결과 오버레이 표시. 단어별 바운딩 박스와 신뢰도를 이미지 위에 그립니다.\n• 녹색: 인식 성공 (PASS)\n• 빨간색: 인식 실패 (FAIL)",

                    // Advanced
                    ["TessdataPath"] = "tessdata 폴더 경로. 비어있으면 실행 파일 기준 ./tessdata/ 를 자동 탐색합니다.\n\ntessdata 파일 다운로드:\n• LSTM (권장): github.com/tesseract-ocr/tessdata_best\n• Fast: github.com/tesseract-ocr/tessdata_fast",

                    // PP-OCR ONNX
                    ["MaxSideLen"] = "PP-OCR ONNX 엔진의 입력 이미지 최대 변 길이 (320~4096 픽셀).\n이미지의 긴 변이 이 값을 초과하면 비율을 유지하며 축소합니다.\n작은 이미지는 이 값까지 확대됩니다.\n• 960 (기본): 일반적인 텍스트 인식에 적합\n• 1600~2000: 신문, 조밀한 표 등 작은 글씨가 많은 이미지에 권장\n• 높을수록 정확하지만 처리 시간과 메모리 사용량 증가",
                    ["CustomDetModelPath"] = "커스텀 Detection 모델 파일 경로 (.onnx).\nFine-tuning된 텍스트 검출 모델을 사용하려면 ONNX 파일의 전체 경로를 입력하세요.\n비어있으면 기본 PP-OCRv4 Detection 모델을 사용합니다.\n\n예: C:\\Models\\custom_det.onnx",
                    ["CustomRecModelPath"] = "커스텀 Recognition 모델 파일 경로 (.onnx).\nFine-tuning된 텍스트 인식 모델을 사용하려면 ONNX 파일의 전체 경로를 입력하세요.\n비어있으면 기본 PP-OCRv4 Recognition 모델을 사용합니다.\n\n예: C:\\Models\\custom_rec.onnx",
                    ["CustomDictPath"] = "커스텀 딕셔너리 파일 경로 (.txt).\nFine-tuning 시 사용한 문자 사전 파일을 지정합니다.\n한 줄에 하나의 문자가 기록된 텍스트 파일이어야 합니다.\n비어있으면 기본 ppocr_keys_v1.txt를 사용합니다."
                }
            },

            // Geometry
            ["GeometryTool"] = new ToolHelp
            {
                Name = "Geometry (기하 연산)",
                Description = "연결된 도구들에서 추출한 기하 요소(점, 직선, 원) 사이의 관계를 계산합니다.\nCaliper, LineFit, CircleFit 등의 결과를 입력으로 받아 거리, 각도, 교차점 등 고차원 치수를 도출합니다.",
                Usage = "Measurement 도구들의 결과를 조합하여 정밀 치수를 측정할 때 사용합니다.\n• Result 연결 타입으로 소스 도구를 연결하세요.\n• 2개 이상의 기하 요소가 연결되어야 연산이 가능합니다.\n• CaliperTool → Point, LineFitTool → Line, CircleFitTool → Circle로 자동 변환됩니다.",
                CognexEquivalent = "CogDistancePointLineTool, CogIntersectLineLineTool, CogAngleLineLineTool",
                Parameters = new Dictionary<string, string>
                {
                    ["Operation"] = "기하 연산 종류:\n• PointPointDistance: 두 점 사이의 유클리드 거리\n• PointLineDistance: 점에서 직선까지의 수직 거리 (수선의 발 좌표도 출력)\n• LineLineDistance: 두 직선 사이의 수직 거리 (폭/갭 측정, 평행도 검사에 활용)\n• LineLineAngle: 두 직선 사이의 각도 (0°~90° 및 부호 있는 각도)\n• LineLineIntersection: 두 직선의 교차점 좌표\n• LineCircleIntersection: 직선과 원의 교차점 (접선인 경우 1개, 관통 시 2개)\n• CircleCircleDistance: 두 원의 중심 간 거리 및 엣지 간 거리"
                }
            },

            // Deep Learning
            ["DetectionTool"] = new ToolHelp
            {
                Name = "Detection (객체 검출)",
                Description = "YOLO ONNX 기반 객체 검출 도구입니다.\nYOLOv8/v11 모델을 로드하여 이미지에서 객체의 위치와 클래스를 검출합니다.",
                Usage = "부품 유무 검사, 다종 객체 검출, 결함 검출에 사용됩니다.\n• VMS.DeepLearning 앱에서 데이터셋을 라벨링하고 YOLO 형식으로 학습한 ONNX 모델을 사용합니다.\n• Class Names에 학습 시 사용한 클래스 이름을 콤마로 구분하여 입력하세요.",
                CognexEquivalent = "Cognex ViDi Blue Locate",
                Parameters = new Dictionary<string, string>
                {
                    ["ModelPath"] = "YOLO ONNX 모델 파일 경로 (.onnx).\nVMS.DeepLearning에서 학습한 best.onnx 파일을 지정합니다.",
                    ["InputSize"] = "모델 입력 이미지 크기 (픽셀).\n학습 시 설정한 imgsz와 동일하게 설정하세요.\n• YOLOv8 기본: 640",
                    ["ConfidenceThreshold"] = "검출 신뢰도 임계값 (0~1). 이 값 이상의 신뢰도를 가진 객체만 검출됩니다.\n• 낮은 값 (0.25): 많은 객체 검출, 오검출 증가\n• 높은 값 (0.7): 확실한 객체만 검출",
                    ["IouThreshold"] = "NMS IoU 임계값 (0~1). 겹치는 검출 박스를 제거하는 기준입니다.\n• 낮은 값 (0.3): 강한 중복 제거\n• 높은 값 (0.7): 약한 중복 제거 (밀집 객체에 적합)",
                    ["ClassNamesText"] = "클래스 이름 목록 (콤마 구분).\n학습 시 사용한 클래스 순서대로 입력합니다.\n예: good,defect,crack",
                    ["DrawOverlay"] = "검출 결과 오버레이 표시. 바운딩 박스, 클래스 이름, 신뢰도를 이미지 위에 그립니다."
                }
            },

            ["ClassifyTool"] = new ToolHelp
            {
                Name = "Classify (이미지 분류)",
                Description = "ONNX 기반 이미지 분류 도구입니다.\nResNet, MobileNet 등 분류 모델의 ONNX를 로드하여 이미지를 분류합니다.",
                Usage = "양품/불량 판정, 부품 종류 분류, 외관 등급 판정에 사용됩니다.\n• VMS.DeepLearning 앱에서 ImageFolder 형식으로 학습한 ONNX 모델을 사용합니다.\n• Class Names에 학습 시 사용한 폴더명(클래스)을 콤마로 구분하여 입력하세요.",
                CognexEquivalent = "Cognex ViDi Green Classify",
                Parameters = new Dictionary<string, string>
                {
                    ["ModelPath"] = "분류 ONNX 모델 파일 경로 (.onnx).\nVMS.DeepLearning에서 학습한 classifier.onnx 파일을 지정합니다.",
                    ["InputWidth"] = "모델 입력 이미지 너비 (픽셀). 학습 시 imgsz와 동일하게 설정.\n• 기본값: 224",
                    ["InputHeight"] = "모델 입력 이미지 높이 (픽셀). 학습 시 imgsz와 동일하게 설정.\n• 기본값: 224",
                    ["ConfidenceThreshold"] = "분류 신뢰도 임계값 (0~1). Top-1 클래스의 신뢰도가 이 값 이상이면 PASS.\n• 기본값: 0.5",
                    ["ClassNamesText"] = "클래스 이름 목록 (콤마 구분).\n학습 데이터의 폴더명 순서대로 입력합니다.\n예: good,scratch,dent",
                    ["UseImageNetNormalization"] = "ImageNet 정규화 사용 여부.\n• 활성화 (기본): mean=[0.485,0.456,0.406], std=[0.229,0.224,0.225]\n• 비활성화: 0~1 단순 정규화\n\ntorchvision pretrained 모델은 활성화를 권장합니다.",
                    ["DrawOverlay"] = "분류 결과 오버레이 표시. 클래스 이름과 신뢰도를 이미지 위에 표시하고, PASS/FAIL에 따라 녹색/빨간색 테두리를 그립니다."
                }
            },

            ["AnomalyTool"] = new ToolHelp
            {
                Name = "Anomaly (이상 탐지)",
                Description = "ONNX 기반 이상 탐지 도구입니다.\nPatchCore, FastFlow, EfficientAD 등 anomalib 호환 모델을 로드하여 정상/이상 판정 및 히트맵을 생성합니다.",
                Usage = "학습 데이터에 정상 이미지만 사용하여 이상(결함)을 탐지합니다.\n• VMS.DeepLearning 앱에서 MVTec 형식으로 학습한 ONNX 모델을 사용합니다.\n• Anomaly Threshold를 조정하여 정상/이상 경계를 설정합니다.\n• 히트맵으로 이상 영역을 시각적으로 확인할 수 있습니다.",
                CognexEquivalent = "Cognex ViDi Red Analyze",
                Parameters = new Dictionary<string, string>
                {
                    ["ModelPath"] = "이상 탐지 ONNX 모델 파일 경로 (.onnx).\nanomalib 또는 VMS.DeepLearning에서 학습한 모델을 지정합니다.",
                    ["InputSize"] = "모델 입력 이미지 크기 (픽셀). 학습 시 image_size와 동일하게 설정.\n• 기본값: 224",
                    ["AnomalyThreshold"] = "이상 판정 임계값 (0~1). Anomaly Score가 이 값 미만이면 정상, 이상이면 이상으로 판정합니다.\n• 낮은 값 (0.3): 엄격한 판정 (약간의 이상도 검출)\n• 높은 값 (0.7): 느슨한 판정 (확실한 이상만 검출)",
                    ["DrawOverlay"] = "판정 결과 오버레이 표시. NORMAL/ANOMALY 텍스트와 점수를 표시하고, 정상=녹색/이상=빨간색 테두리를 그립니다.",
                    ["ShowHeatmap"] = "이상 히트맵 표시. 모델이 anomaly_map을 출력하는 경우 Jet 컬러맵으로 이상 영역을 시각화합니다.",
                    ["HeatmapOpacity"] = "히트맵 투명도 (0~1). 원본 이미지 위에 히트맵을 알파 블렌딩하는 비율입니다.\n• 0: 히트맵 미표시\n• 0.4 (기본): 적당한 투명도\n• 1.0: 히트맵만 표시"
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
                    ["UseLocalization"] = "DataMatrix 후보 영역 사전 탐색 (DataMatrix/Auto 모드에서만 동작).\nROI가 넓고 텍스트/잡음이 섞여 있어 ZXing 직접 디코딩이 실패하는 경우, OpenCV 휴리스틱(adaptive threshold + morph close + contour 정사각/고밀도 필터)으로 DM 후보 bbox를 먼저 찾아 영역별로 디코딩합니다.\n• DataMatrix 위치가 이미지 내에서 이동하고 주변에 다른 텍스트가 있을 때 활성화 권장\n• 깨끗한 단일 코드 이미지는 추가 시간 미미 (후보 0~1개)\n• QR/1D 바코드에는 영향 없음 (DM/Auto 모드 한정)",

                    // Verification
                    ["EnableVerification"] = "텍스트 검증 활성화. 활성화하면 디코딩된 텍스트를 ExpectedText와 비교하여 PASS/FAIL을 판정합니다.\n비활성화 시 코드가 1개 이상 검출되면 항상 Success입니다.",
                    ["ExpectedText"] = "기대 텍스트. 디코딩된 코드 중 이 텍스트와 일치하는 코드가 있으면 PASS.\n• UseRegexMatch 비활성화: 정확히 일치해야 합격\n• UseRegexMatch 활성화: 정규식 패턴으로 매칭\n\n예시: \"ABC-12345\", \"^LOT-\\d{6}$\"",
                    ["UseRegexMatch"] = "정규식 매칭 사용 여부.\n• 비활성화: 디코딩 텍스트 == ExpectedText 정확히 일치\n• 활성화: Regex.IsMatch(디코딩 텍스트, ExpectedText)로 패턴 매칭\n\n정규식 예시:\n• ^SN\\d{8}$: \"SN\" + 숫자 8자리\n• ^(OK|PASS): \"OK\" 또는 \"PASS\"로 시작",

                    // GS1
                    ["ParseGs1"] = "GS1 AI(Application Identifier) 파싱.\nFNC1 구분자가 포함되거나 GS1 심볼 식별자(]C1, ]d2 등)가 있는 데이터를 (01)GTIN, (10)Batch, (17)Expiry, (21)Serial 등으로 분리합니다.\n\n결과 키:\n• Gs1Formatted: \"(01)08801234567890 (17)260101 (10)LOT123\" 형식\n• Gs1ElementCount: 추출된 AI 개수\n• Gs1_01, Gs1_17, Gs1_10 등: 각 AI 값 (PLC 매핑 가능)",

                    // Quality Grading
                    ["EnableQualityGrading"] = "ISO/IEC 15415 간소화 품질 등급 계산 (DataMatrix 한정).\n활성화 시 다음 결과 키가 노출됩니다:\n• OverallGrade (A~F): 최종 등급\n• SymbolContrast: 명/암 모듈 대비 (0~1)\n• Modulation: 모듈 균일도\n• FixedPatternDamage: L-finder/클럭 트랙 무결성\n• AxialNonuniformity: 가로/세로 모듈 폭 차이\n• PixelsPerModule: 모듈당 픽셀 수\n• SymbolSize: 추정 심볼 크기 (n×n 모듈)",
                    ["MinPassGrade"] = "품질 등급 활성화 시 PASS 판정의 최소 OverallGrade.\n계산된 등급이 이 값보다 낮으면 Success=false로 처리됩니다.\n• A (4.0): 매우 엄격, 신규 마킹 검증용\n• C (2.0, 기본): 산업 표준 최저 허용\n• F: 등급 게이팅 비활성화 (디코딩만 성공하면 PASS)",

                    // Display
                    ["DrawOverlay"] = "검출 결과 오버레이 표시. 활성화하면 검출된 코드 위치에 폴리곤과 디코딩 텍스트를 그립니다.\n• 녹색: 인식 성공 (PASS)\n• 빨간색: 인식 실패 (FAIL)\n품질 등급 활성화 시 상단에 SC/MOD/FPD/AN/PPM 요약 배지, GS1 활성화 시 파싱된 AI 문자열 배지가 추가됩니다."
                }
            },

            // Image Enhance
            ["ImageEnhanceTool"] = new ToolHelp
            {
                Name = "Image Enhance (선명도 강화)",
                Description = "Sharpen 또는 Unsharp Mask로 이미지 선명도를 강화합니다. OCR/CodeReader 전단 전처리에 유용.",
                Usage = "PCB 마킹, 라벨 인쇄 등 미세한 글자/패턴을 더 선명하게.\n• Sharpen: 단순 Laplacian 3x3 — 빠르지만 노이즈 동시 증폭\n• Unsharp Mask (산업 표준): Gaussian blur 후 원본−blur 차이를 amount로 강조. Threshold로 잡음 영역은 sharp 회피.",
                CognexEquivalent = "CogIPOneImageTool — Sharpen / Unsharp 연산자",
                Parameters = new Dictionary<string, string>
                {
                    ["Mode"] = "Sharpen: 3x3 라플라시안 커널 컨볼루션 (단순/빠름)\nUnsharpMask: 원본 + amount×(원본 − Gaussian blur). 더 자연스럽고 제어 가능 — 권장.",
                    ["Amount"] = "강화 강도. 0.5~2.0이 일반적. 너무 크면 halo/ringing 아티팩트 발생.",
                    ["BlurKernelSize"] = "(Unsharp Mask 전용) Gaussian 커널 크기. 홀수 (3, 5, 7, 11...). 클수록 큰 구조물 강조, 작은 디테일 보존.",
                    ["Threshold"] = "(Unsharp Mask 전용) 원본과 blur의 차이가 이 값 미만이면 변경 안 함. 잡음 영역 sharpening 회피.\n• 0: 모든 픽셀 강화 (잡음도 증폭)\n• 5~15: 잡음 억제 + 글자 강화 균형\n• 30+: 강한 엣지만 강화"
                }
            },

            // Polar Unwrap
            ["PolarUnwrapTool"] = new ToolHelp
            {
                Name = "Polar Unwrap (원형 → 직사각형)",
                Description = "원형/원통 표면의 라벨을 극좌표 변환으로 직사각형으로 펼칩니다.\n캡/약병/파이프 라벨 OCR이나 코드 인식의 표준 전처리.",
                Usage = "Center는 수동 입력 또는 CircleFit Tool 결과를 Coordinates 연결로 자동 주입.\n1) 라벨 영역의 내반경(병목)과 외반경(병몸)을 추정해서 InnerRadius/OuterRadius 입력\n2) 시작 각도 + 방향 설정 (라벨 텍스트가 좌→우 정렬되도록)\n3) 출력 너비/높이는 0이면 자동 (둘레 × 반경 차)\n4) 출력을 OCRTool/CodeReaderTool에 연결",
                CognexEquivalent = "CogPolarUnwrapTool",
                Parameters = new Dictionary<string, string>
                {
                    ["CenterX"] = "원 중심 X 좌표 (픽셀). CircleFit 도구의 CenterX를 Coordinates 연결로 자동 주입 가능.",
                    ["CenterY"] = "원 중심 Y 좌표 (픽셀).",
                    ["InnerRadius"] = "내반경 (픽셀). 라벨이 시작되는 안쪽 경계. 0이면 중심부터 펼침.",
                    ["OuterRadius"] = "외반경 (픽셀). 라벨 바깥 경계. InnerRadius보다 커야 함.",
                    ["StartAngleDeg"] = "펼침 시작 각도(°). 0 = 오른쪽(3시), 90 = 위, 180 = 왼쪽, 270 = 아래.\n출력 이미지의 좌측 첫 컬럼이 이 각도에서 시작.",
                    ["Direction"] = "회전 방향:\n• Clockwise: 시계방향 — 출력 가로축이 시계방향으로 진행\n• Counterclockwise: 반시계방향 (기본, Cognex 동일)",
                    ["OutputWidth"] = "출력 너비 (px). 0이면 자동: 2π × OuterRadius (둘레 근사).\n후속 도구의 해상도를 고정하려면 명시.",
                    ["OutputHeight"] = "출력 높이 (px). 0이면 자동: OuterRadius − InnerRadius."
                }
            },

            // OCV
            ["OCVTool"] = new ToolHelp
            {
                Name = "OCV (문자 검증)",
                Description = "학습된 폰트 라이브러리와의 NCC 매칭으로 각 문자가 학습 폰트와 일치하는지 검증합니다.\nOCR이 '읽기'라면 OCV는 '대조 검증' — 인쇄 결함(찍힘, 번짐, 누락)을 문자 단위로 검출합니다.\n동일 문자에 여러 폰트/스타일 템플릿을 등록 가능 (매칭은 최고 점수 사용).",
                Usage = "PCB 마킹 PASS/FAIL 검증, 정해진 폰트의 시리얼 번호 OK 판정, 도트 마킹 일관성 검사에 사용됩니다.\n1) ROI를 학습용 텍스트에 맞추고 Known String 입력 후 Train 클릭.\n2) 분할 개수와 Known String 길이가 일치해야 학습 성공.\n3) 학습 후 새 이미지에서 자동으로 분할/매칭하여 GoodChar/BadChar 카운트.",
                CognexEquivalent = "CogOCRMaxTool (Verify Mode), CogOCRMaxFontTool",
                Parameters = new Dictionary<string, string>
                {
                    ["KnownString"] = "Train 시 사용할 정답 문자열. Training Region 내부 분할 segments와 1:1 대응됩니다.\n예: \"ABC123\" → 6개 문자로 분할되어야 학습 성공.\n분할 개수가 다르면 분할 파라미터(Min/Max Height/Width) 조정 또는 Training Region 재설정.",
                    ["UseSearchRegion"] = "검색 영역 사용 여부. Execute 단계에서 문자를 검색할 영역을 Training Region과 분리해서 지정.\n• 비활성: 전체 이미지에서 검색\n• 활성: SearchRegion(X/Y/W/H)으로 지정한 사각형만 검색\n\n학습은 정해진 위치(라벨 인쇄 표본)에서 하고, 검증은 매번 다른 위치의 라벨(컨베이어 위 등)에서 하는 워크플로우에 필수. FeatureMatchTool로 fixture 보정 후 SearchRegion을 위치 보정해도 좋음.",
                    ["SearchRegionX"] = "검색 영역 좌상단 X 좌표 (픽셀).",
                    ["SearchRegionY"] = "검색 영역 좌상단 Y 좌표 (픽셀).",
                    ["SearchRegionWidth"] = "검색 영역 너비 (픽셀).",
                    ["SearchRegionHeight"] = "검색 영역 높이 (픽셀).",
                    ["InvertImage"] = "이진화 후 흰=배경/검=문자인 경우 활성화.\n기본은 흰 배경 + 검은 문자. 통계로 자동 판정하므로 대부분 비활성화로 동작합니다.",
                    ["MinCharHeight"] = "분할된 컴포넌트 최소 높이(px). 이 미만은 노이즈로 간주하여 무시.",
                    ["MaxCharHeight"] = "분할된 컴포넌트 최대 높이(px). 이를 초과하면 배경 영역으로 간주하여 무시.",
                    ["MinCharWidth"] = "분할된 컴포넌트 최소 너비(px). 점선/얇은 줄 노이즈 제거용.",
                    ["MatchThreshold"] = "NCC 매칭 임계값 (0~1). 각 문자의 최고 매칭 점수가 이 미만이면 BadChar로 판정.\n• 0.65 (기본): 산업 인쇄 문자 기본값\n• 0.80+: 엄격한 일치 요구 (동일 폰트만 통과)\n• 0.50 이하: 매우 관대 (오인식 위험)",
                    ["ExpectedText"] = "(선택) 예상 텍스트. 비어있지 않으면 위치별 char를 비교하여 추가 검증.\n분할 개수와 길이가 다르면 LengthMatch=false로 FAIL 처리.\n비어있으면 NCC 점수만으로 판정.",
                    ["DrawOverlay"] = "분할 박스 + 인식 문자 + 점수 오버레이.\n• 녹색: 매칭 PASS\n• 빨간색: BadChar (점수 < Threshold 또는 ExpectedText 불일치)"
                }
            },

            // ─── Sequence Editor Nodes — 노드 팔레트 호버 도움말 ───
            // key 규칙: "SequenceNode_{SequenceNodeType}" — NodePaletteItem.HelpKey 와 일치.
            // HelpIcon 의 ToolType binding 으로 lookup.
            ["SequenceNode_Start"] = new ToolHelp
            {
                Name = "Start (시작)",
                Description = "시퀀스의 진입점입니다. 외부 트리거(PLC 신호 / 수동 실행 / 타이머)가 발생하면 이 노드부터 흐름이 시작됩니다.\n\n모든 시퀀스에 정확히 1개의 Start 노드가 존재해야 하며, 여기서 출력된 흐름이 하위 노드들로 전달됩니다. Start 가 없거나 둘 이상이면 시퀀스는 실행되지 않습니다.",
                Usage = "시퀀스 캔버스에 가장 먼저 배치합니다. Start 의 출력 포트를 InputCheck / Inspection / Delay 등 첫 동작 노드에 연결하세요.\n\n전형적 흐름:\nStart → InputCheck(제품 도착 신호) → Inspection → Branch → End"
            },

            ["SequenceNode_End"] = new ToolHelp
            {
                Name = "End (종료)",
                Description = "시퀀스 한 사이클의 종료점입니다. End 노드에 도달하면 사이클이 완료된 것으로 간주하고 결과를 외부(PLC / Web) 로 통보합니다.\n\n정상 종료(OK)와 NG 종료를 구분하려면 Branch 의 각 가지 끝에 End 를 여러 개 배치하는 패턴이 일반적입니다.",
                Usage = "Inspection 결과 분기 끝마다 End:\nBranch.True → OutputAction(OK 램프) → End(OK)\nBranch.False → OutputAction(NG 분배) → End(NG)\n\nRepeat 안에서 End 는 사이클을 빠져나가는 break 역할로도 사용 가능."
            },

            ["SequenceNode_InputCheck"] = new ToolHelp
            {
                Name = "Input Check (입력 신호 검사)",
                Description = "PLC 입력 어드레스(비트 / 워드) 의 값을 읽어 조건을 검사합니다. 검사 결과(True / False) 에 따라 다음 노드로 분기하거나 대기합니다.\n\nBranch 와의 차이: Branch 가 직전 노드 결과(주로 Inspection PASS/FAIL) 를 기준으로 한다면, InputCheck 는 외부 신호(센서·광커튼·작업자 버튼 등) 가 기준입니다.",
                Usage = "예: '제품 도착 신호 X0 가 ON 인가?' 검사 후\n• True 가지 → Inspection (실제 검사 시작)\n• False 가지 → Delay(50ms) → 다시 InputCheck (폴링 루프)\n\n타임아웃 옵션으로 무한 대기 방지."
            },

            ["SequenceNode_OutputAction"] = new ToolHelp
            {
                Name = "Output Action (출력 신호 발생)",
                Description = "PLC 출력 어드레스에 비트 / 워드 값을 씁니다. 검사 결과 알림(OK / NG 램프), 분배기 액추에이터 트리거, 컨베이어 정지·재가동 등 외부 장비 제어에 사용됩니다.",
                Usage = "결과 분기 직후 신호 발생:\nBranch.True  → OutputAction(Y0 = ON, OK 램프)\nBranch.False → OutputAction(Y1 = ON, NG 분배기)\n\n펄스 출력이 필요한 경우:\nOutputAction(ON) → Delay(100ms) → OutputAction(OFF) 으로 폭 제어."
            },

            ["SequenceNode_Inspection"] = new ToolHelp
            {
                Name = "Inspection (비전 검사)",
                Description = "현재 로드된 레시피의 비전 도구 시퀀스를 실행하고 결과(PASS / FAIL + 측정값) 를 반환합니다.\n\n카메라 그랩 → 도구 파이프라인(전처리 → 패턴매칭 → 측정 등) → 판정의 한 사이클이 이 노드 안에서 완결됩니다. 결과는 후속 Branch / OutputAction 에서 변수로 사용 가능.",
                Usage = "표준 흐름:\nStart → InputCheck(제품 도착) → Inspection → Branch(결과 PASS/FAIL) → End\n\n멀티-스텝 검사:\nRepeat(N=4) { StepChange(idx) → Inspection } 로 한 제품의 여러 위치를 순회."
            },

            ["SequenceNode_Branch"] = new ToolHelp
            {
                Name = "Branch (조건 분기)",
                Description = "이전 노드의 결과(주로 Inspection 의 PASS / FAIL 또는 사용자 정의 변수) 를 기준으로 흐름을 두 가지로 나눕니다.\n\nTrue / False 출력 포트가 각각 다른 하위 노드로 연결됩니다. 외부 신호 기반 분기가 필요하면 InputCheck 를 대신 사용하세요.",
                Usage = "Inspection 직후 표준 패턴:\nInspection → Branch\n  ├ True  → OutputAction(OK) → End\n  └ False → OutputAction(NG) → End\n\n다중 조건은 Branch 를 직렬로 연결하거나 InputCheck 와 조합."
            },

            ["SequenceNode_Delay"] = new ToolHelp
            {
                Name = "Delay (시간 지연)",
                Description = "지정한 시간(밀리초) 동안 흐름을 멈췄다가 다음 노드로 진행합니다.\n\n액추에이터 동작 완료 대기, 카메라 그랩 후 진동 안정화, 폴링 간격 확보 등에 사용됩니다.",
                Usage = "진동 안정화:\nOutputAction(컨베이어 정지) → Delay(200ms) → Inspection\n\n폴링 간격:\nInputCheck → Delay(50ms) → 다시 InputCheck (루프)\n\n튜닝 가이드: 너무 짧으면 흔들림에 의한 검사 노이즈, 너무 길면 택트 타임 손실 — 현장에서 측정 후 결정."
            },

            ["SequenceNode_Repeat"] = new ToolHelp
            {
                Name = "Repeat (반복 루프)",
                Description = "내부 노드 그룹을 지정 횟수(N회) 만큼 또는 조건이 만족될 때까지 반복 실행합니다.\n\n다중 위치 검사, 재시도 로직, 폴링 루프(신호가 들어올 때까지 대기) 등에 사용됩니다. 무한 루프 방지를 위해 항상 최대 반복 횟수 또는 타임아웃을 설정하세요.",
                Usage = "다중 위치 순회 검사:\nRepeat(N=4) {\n  StepChange(idx=loopIndex) → Inspection\n}\n\n재시도 로직:\nRepeat(N=3) {\n  Inspection → Branch(PASS) → break\n  Branch(FAIL) → Delay(500ms) → 재시도\n}"
            },

            ["SequenceNode_RecipeChange"] = new ToolHelp
            {
                Name = "Recipe Change (레시피 전환)",
                Description = "현재 로드된 비전 레시피를 다른 레시피로 교체합니다.\n\n동일 라인에서 다품종 생산 시 제품 변경(작업지시 변경) 시점에 사용합니다. 전환 시 도구 인스턴스가 재초기화되므로 Fixture 기준점도 새로 설정됩니다 — 첫 검사는 reference 캡처용으로 활용.",
                Usage = "품종 변경 자동화:\nStart → InputCheck(품종 변경 신호 X10) → RecipeChange(RecipeId=2) → Inspection\n\nWeb WorkOrder 연동 환경:\n작업지시 선택 시 시스템이 자동으로 권장 레시피로 RecipeChange 트리거 — 작업자 수동 개입 불필요."
            },

            ["SequenceNode_StepChange"] = new ToolHelp
            {
                Name = "Step Change (단계 변경)",
                Description = "현재 레시피 내에서 다음 검사 단계(InspectionStep) 로 전환합니다.\n\n한 제품에 대해 다중 위치 / 다중 카메라 검사를 순차 수행할 때 단계 인덱스를 명시적으로 변경합니다. RecipeChange 와 달리 레시피는 유지되고 단계만 이동하므로 도구 인스턴스 / Fixture 기준이 보존됩니다.",
                Usage = "멀티-스텝 순회:\nRepeat(N=3) {\n  StepChange(idx=0) → Inspection\n  StepChange(idx=1) → Inspection\n  StepChange(idx=2) → Inspection\n}\n\n분배 기반 다음 위치 이동:\nOutputAction(분배 액추에이터) → Delay(300ms) → StepChange → Inspection"
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
