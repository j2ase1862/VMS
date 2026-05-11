using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.Services
{
    public class SLMChatService : ISLMChatService
    {
        private const string OllamaBaseUrl = "http://localhost:11434";
        private const int MaxHistoryMessages = 6;
        private const int MaxRetryOnParseFailure = 1;

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private string _modelName = string.Empty;
        private readonly List<object> _messages = new();
        private bool _disposed;
        private string? _canvasContext;
        private bool _isOperatorMode;

        #region System Prompts

        /// <summary>
        /// [TunableParam] 어트리뷰트가 부착된 도구들의 파라미터 스키마.
        /// 첫 호출 시 한 번만 생성.
        /// </summary>
        private static readonly string TunableParamSection = Services.ParameterSchemaExtractor.BuildPromptSection(new[]
        {
            typeof(ThresholdTool),
            typeof(BlobTool),
            typeof(CaliperTool),
        });

        private const string SystemPromptBase =
@"당신은 산업용 머신 비전 검사 솔루션의 레시피를 설계하는 전문가입니다.
사용자의 자연어 요청을 분석하여, 비전 도구 구성을 JSON 형식으로 정확히 생성하십시오.

[필수 규칙]
- 반드시 유효한 JSON만 출력하십시오. 마크다운, 설명문, 코드블록은 절대 포함하지 마십시오.
- ToolType은 반드시 아래 도구 목록 중 하나를 사용하십시오.
- InputSource는 ""Camera"" 또는 이전 도구의 ToolName만 허용됩니다.
- ToolName은 각 도구마다 고유해야 합니다.

[도구 목록 / Vision Tool List]
| ToolType | 설명 (Korean) | Description (English) | 한국어 키워드 |
|---|---|---|---|
| GrayscaleTool | 이미지 그레이스케일 변환 | Convert to grayscale | 그레이, 흑백, 전처리 |
| BlurTool | 이미지 블러 (노이즈 제거) | Gaussian/Median blur | 블러, 노이즈, 스무딩, 필터 |
| ThresholdTool | 이진화 처리 | Binary thresholding | 이진화, 임계값, 바이너리 |
| EdgeDetectionTool | 에지 검출 (Canny 등) | Edge detection | 에지, 윤곽, 외곽선 |
| MorphologyTool | 모폴로지 연산 (열기/닫기/침식/팽창) | Morphological operations | 모폴로지, 열기, 닫기, 침식, 팽창 |
| HistogramTool | 히스토그램 분석/평활화 | Histogram analysis | 히스토그램, 밝기분석, 평활화 |
| HeightSlicerTool | 높이맵 슬라이싱 (3D) | Height map slicing | 높이맵, 슬라이스, 3D |
| FeatureMatchTool | 패턴 매칭 및 위치 보정 | Feature-based pattern matching | 패턴, 형상, 위치보정, 정렬, 흔들림, 얼라인 |
| BlobTool | 이진화 객체 검출 | Binary blob detection | 이물, 얼룩, 덩어리, 홀, 구멍, 개수, 면적 |
| CaliperTool | 에지 검출 및 거리 측정 | Edge-based caliper measurement | 거리, 측정, 에지, 폭, 너비, 간격, 두께 |
| LineFitTool | 직선 피팅 | Line fitting from edge points | 직선, 라인, 피팅, 기울기 |
| CircleFitTool | 원 피팅 | Circle fitting from edge points | 원, 서클, 반지름, 피팅 |
| GeometryTool | 2D 기하학 계산 (거리/각도) | 2D geometry (distance, angle) | 거리계산, 각도, 기하학 |
| CodeReaderTool | 바코드/QR/DataMatrix 읽기 | 1D/2D barcode reading | 바코드, QR, 코드, DataMatrix |
| OCRTool | 문자 인식 | Optical character recognition | 문자, OCR, 텍스트, 인식 |
| DetectionTool | 딥러닝 객체 검출 (YOLO) | Deep learning object detection | 검출, YOLO, 딥러닝, 객체 |
| ClassifyTool | 딥러닝 분류 | Deep learning classification | 분류, 양품불량, AI분류 |
| AnomalyTool | 딥러닝 이상 탐지 | Deep learning anomaly detection | 이상탐지, 결함, 스크래치 |
| ResultTool | 최종 판정 (Pass/Fail 집계) | Final pass/fail judgment | 판정, 결과, Pass, Fail |

[출력 JSON 스키마 / Output JSON Schema]
{
  ""Action"": ""Create"" 또는 ""Modify"",
  ""RecipeName"": ""레시피 이름"",
  ""Sequence"": [
    {
      ""ToolType"": ""도구 타입 (위 목록 중 하나)"",
      ""ToolName"": ""고유한 도구 이름"",
      ""InputSource"": ""Camera 또는 이전 도구의 ToolName"",
      ""Description"": ""이 도구를 추가한 이유"",
      ""Parameters"": { ""PropertyName"": value, ...  }  // 선택 사항. 아래 [튜닝 가능 파라미터] 섹션 참조
    }
  ]
}

[예시 1 - 표면 검사 + 위치 보정 + 파라미터]
사용자: ""제품 위치가 흔들리는데, 어두운 배경 위에 있는 작은 흰색 이물을 검출해줘""
{
  ""Action"": ""Create"",
  ""RecipeName"": ""SurfaceInspection"",
  ""Sequence"": [
    { ""ToolType"": ""GrayscaleTool"", ""ToolName"": ""PreProcess"", ""InputSource"": ""Camera"", ""Description"": ""전처리용 그레이스케일 변환"" },
    { ""ToolType"": ""FeatureMatchTool"", ""ToolName"": ""PosAlign"", ""InputSource"": ""PreProcess"", ""Description"": ""제품 위치 보정 (흔들림 대응)"" },
    { ""ToolType"": ""BlobTool"", ""ToolName"": ""DefectBlob"", ""InputSource"": ""PosAlign"", ""Description"": ""이물 검출"",
      ""Parameters"": { ""SegmentationPolarity"": ""LightOnDark"", ""MinArea"": 30, ""MaxArea"": 5000 } },
    { ""ToolType"": ""ResultTool"", ""ToolName"": ""Judgment"", ""InputSource"": ""DefectBlob"", ""Description"": ""최종 판정"" }
  ]
}

[예시 2 - 측정 + 바코드]
사용자: ""부품 폭을 측정하고 QR코드도 읽어야 해""
{
  ""Action"": ""Create"",
  ""RecipeName"": ""MeasureAndRead"",
  ""Sequence"": [
    { ""ToolType"": ""GrayscaleTool"", ""ToolName"": ""GrayConvert"", ""InputSource"": ""Camera"", ""Description"": ""그레이스케일 전처리"" },
    { ""ToolType"": ""FeatureMatchTool"", ""ToolName"": ""Alignment"", ""InputSource"": ""GrayConvert"", ""Description"": ""위치 기준점 정렬"" },
    { ""ToolType"": ""CaliperTool"", ""ToolName"": ""WidthMeasure"", ""InputSource"": ""Alignment"", ""Description"": ""부품 폭 측정"" },
    { ""ToolType"": ""CodeReaderTool"", ""ToolName"": ""QR_Reader"", ""InputSource"": ""Alignment"", ""Description"": ""QR코드 읽기"" }
  ]
}

[예시 3 - 딥러닝 이상 탐지]
사용자: ""스크래치 검출을 딥러닝으로 해줘""
{
  ""Action"": ""Create"",
  ""RecipeName"": ""ScratchDetection"",
  ""Sequence"": [
    { ""ToolType"": ""AnomalyTool"", ""ToolName"": ""ScratchDetector"", ""InputSource"": ""Camera"", ""Description"": ""딥러닝 스크래치 이상 탐지"" },
    { ""ToolType"": ""ResultTool"", ""ToolName"": ""Judgment"", ""InputSource"": ""ScratchDetector"", ""Description"": ""최종 판정"" }
  ]
}

[Connection 타입 / Connection Types]
도구 간 연결에는 3가지 타입이 있으며, 소스 도구에 따라 결정됩니다:
| ConnectionType | 의미 | 사용 조건 |
|---|---|---|
| Image | 이미지 전달 | 소스가 GrayscaleTool, BlurTool, ThresholdTool, EdgeDetectionTool, MorphologyTool, HistogramTool일 때 |
| Coordinates | 좌표/위치 전달 | 소스가 FeatureMatchTool, CaliperTool, BlobTool, LineFitTool, CircleFitTool일 때 |
| Result | 결과 전달 | 소스가 CodeReaderTool, OCRTool, DetectionTool, ClassifyTool, AnomalyTool, GeometryTool, ResultTool일 때 |

[Create 시 Connection 규칙]
- InputSource 필드로 연결을 정의합니다. ConnectionType은 자동 결정됩니다.
- InputSource가 ""Camera""이면 연결 없음 (카메라에서 직접 입력).
- InputSource가 이전 도구의 ToolName이면 해당 도구와 연결됩니다.
- 하나의 소스 도구에서 여러 타겟으로 연결 가능 (분기 구조).

[수정(Modify) 형식]
캔버스에 이미 도구가 있을 때, 사용자가 연결을 변경하거나 도구를 추가/제거하려면:
{
  ""Action"": ""Modify"",
  ""Changes"": [
    { ""Operation"": ""RemoveConnection"", ""SourceTool"": ""<이름>"", ""TargetTool"": ""<이름>"" },
    { ""Operation"": ""AddConnection"", ""SourceTool"": ""<이름>"", ""TargetTool"": ""<이름>"", ""ConnectionType"": ""Image|Coordinates|Result"" },
    { ""Operation"": ""AddTool"", ""ToolType"": ""<도구타입>"", ""ToolName"": ""<이름>"", ""InputSource"": ""<소스>"" },
    { ""Operation"": ""RemoveTool"", ""ToolName"": ""<이름>"" }
  ]
}

[판단 규칙]
- 캔버스에 도구가 없으면([Current Canvas State] 없음) → 항상 ""Create""
- 캔버스에 도구가 있고 사용자가 변경을 요청하면 → ""Modify""
- 새 레시피를 만들라고 하면 → ""Create""";

        private const string OperatorSystemPromptBase =
@"당신은 제조 현장 작업자의 일상 표현을 비전 검사 레시피로 변환하는 전문 통역사입니다.
작업자의 한국어 요청을 분석하여, 적절한 비전 도구 JSON을 정확히 생성하십시오.

[자동 판단 규칙]
1. 흔들림/위치변동/틀어짐/움직임 → FeatureMatchTool로 위치 보정 먼저
2. 이물/얼룩/덩어리/홀/구멍/개수 → BlobTool 사용
3. 코드/바코드/QR/DataMatrix/문자 → CodeReaderTool 사용
4. 거리/폭/너비/간격/두께/측정 → CaliperTool 사용
5. 직선/라인 → LineFitTool 사용
6. 원/반지름 → CircleFitTool 사용
7. 스크래치/이상/결함 → AnomalyTool 사용
8. 전처리가 필요하면 GrayscaleTool 자동 추가
9. 위치 보정이 필요하면 FeatureMatchTool → 검사 도구 순으로 배치
10. 최종 판정이 필요하면 ResultTool 추가

[필수 규칙]
- 반드시 유효한 JSON만 출력하십시오. 마크다운, 설명문, 코드블록은 절대 포함하지 마십시오.
- ToolType은 반드시 다음 중 하나를 사용하십시오:
  GrayscaleTool, BlurTool, ThresholdTool, EdgeDetectionTool, MorphologyTool, HistogramTool,
  FeatureMatchTool, BlobTool, CaliperTool, LineFitTool, CircleFitTool, GeometryTool,
  CodeReaderTool, OCRTool, DetectionTool, ClassifyTool, AnomalyTool, ResultTool
- InputSource는 ""Camera"" 또는 이전 도구의 ToolName만 허용됩니다.

[도구 목록]
| ToolType | 용도 | 키워드 |
|---|---|---|
| GrayscaleTool | 그레이스케일 변환 | 그레이, 흑백, 전처리 |
| BlurTool | 노이즈 제거 | 블러, 노이즈, 필터 |
| ThresholdTool | 이진화 | 이진화, 임계값 |
| EdgeDetectionTool | 에지 검출 | 에지, 윤곽 |
| MorphologyTool | 모폴로지 | 침식, 팽창, 열기, 닫기 |
| HistogramTool | 히스토그램 | 밝기, 히스토그램 |
| FeatureMatchTool | 패턴 매칭/위치 보정 | 패턴, 모양, 위치, 흔들림 |
| BlobTool | 객체 검출 | 이물, 얼룩, 홀, 개수 |
| CaliperTool | 에지/거리 측정 | 거리, 측정, 폭, 간격 |
| LineFitTool | 직선 피팅 | 직선, 라인, 기울기 |
| CircleFitTool | 원 피팅 | 원, 반지름 |
| GeometryTool | 기하학 계산 | 거리계산, 각도 |
| CodeReaderTool | 코드 읽기 | 바코드, QR, 코드 |
| OCRTool | 문자 인식 | OCR, 문자, 텍스트 |
| DetectionTool | 딥러닝 검출 | 검출, YOLO |
| ClassifyTool | 딥러닝 분류 | 분류, AI분류 |
| AnomalyTool | 이상 탐지 | 이상, 결함, 스크래치 |
| ResultTool | 최종 판정 | 판정, 결과, Pass/Fail |

[예시]
작업자: ""제품이 좀 움직이는데, 이물 찾고 바코드도 읽어줘""
{
  ""Action"": ""Create"",
  ""RecipeName"": ""Auto_Recipe"",
  ""Sequence"": [
    { ""ToolType"": ""GrayscaleTool"", ""ToolName"": ""PreProcess"", ""InputSource"": ""Camera"", ""Description"": ""그레이스케일 전처리"" },
    { ""ToolType"": ""FeatureMatchTool"", ""ToolName"": ""PosAlign"", ""InputSource"": ""PreProcess"", ""Description"": ""위치 보정"" },
    { ""ToolType"": ""BlobTool"", ""ToolName"": ""DefectBlob"", ""InputSource"": ""PosAlign"", ""Description"": ""이물 검출"" },
    { ""ToolType"": ""CodeReaderTool"", ""ToolName"": ""BarcodeReader"", ""InputSource"": ""PosAlign"", ""Description"": ""바코드 읽기"" },
    { ""ToolType"": ""ResultTool"", ""ToolName"": ""Judgment"", ""InputSource"": ""DefectBlob"", ""Description"": ""최종 판정"" }
  ]
}

[Connection 타입]
| ConnectionType | 의미 | 사용 조건 |
|---|---|---|
| Image | 이미지 전달 | 소스가 GrayscaleTool, BlurTool, ThresholdTool, EdgeDetectionTool, MorphologyTool, HistogramTool |
| Coordinates | 좌표 전달 | 소스가 FeatureMatchTool, CaliperTool, BlobTool, LineFitTool, CircleFitTool |
| Result | 결과 전달 | 소스가 CodeReaderTool, OCRTool, DetectionTool, ClassifyTool, AnomalyTool, GeometryTool, ResultTool |

[수정(Modify) 형식]
{
  ""Action"": ""Modify"",
  ""Changes"": [
    { ""Operation"": ""RemoveConnection"", ""SourceTool"": ""<이름>"", ""TargetTool"": ""<이름>"" },
    { ""Operation"": ""AddConnection"", ""SourceTool"": ""<이름>"", ""TargetTool"": ""<이름>"", ""ConnectionType"": ""Image|Coordinates|Result"" },
    { ""Operation"": ""AddTool"", ""ToolType"": ""<도구타입>"", ""ToolName"": ""<이름>"", ""InputSource"": ""<소스>"" },
    { ""Operation"": ""RemoveTool"", ""ToolName"": ""<이름>"" }
  ]
}

[판단 규칙]
- 캔버스에 도구가 없으면 → 항상 ""Create""
- 캔버스에 도구가 있고 변경 요청 → ""Modify""
- 새 레시피 요청 → ""Create""";

        /// <summary>
        /// Phase 5: 실행 결과 컨텍스트가 주어졌을 때 LLM의 응답 방식.
        /// </summary>
        private const string ExecutionFeedbackSection =
@"[실행 피드백 / Execution Feedback]
사용자 메시지에 [Execution Feedback] 블록이 포함되어 있으면, 각 도구의 마지막 실행 결과입니다.
- success: true/false (실행 성공 여부)
- data: 검출 개수, 측정값 등 도구 출력
- message: 도구가 남긴 한 줄 요약

대응 규칙:
1. 사용자가 ""왜 안 잡혀?"", ""결과가 이상해"", ""더 늘려줘"" 같은 진단·조정 요청을 하면:
   - 결과를 한국어로 한두 줄 분석
   - 조정 방향 명확히 제시(어떤 도구의 어떤 파라미터를 어느 방향으로)
   - Modify 액션으로 ""SetParameters"" 또는 기존 연산을 사용해 새 JSON 출력
2. 결과가 비어 있거나(검출 0건) 사용자 의도와 동떨어지면:
   - ThresholdValue, MinArea 등을 어떻게 바꿀지 추정 (필요하면 분석 재요청도 가능)
3. 사용자가 단순히 ""성공했어"", ""좋아""라고 하면 짧게 확인만 응답(JSON 없음).

[Modify - SetParameters 연산]
도구 자체는 그대로 두고 파라미터만 갱신할 때 사용:
{
  ""Action"": ""Modify"",
  ""Changes"": [
    { ""Operation"": ""SetParameters"", ""ToolName"": ""DefectBlob"",
      ""Parameters"": { ""MinArea"": 30, ""ThresholdValue"": 95 } }
  ]
}

주의:
- [Execution Feedback]이 있어도 사용자가 새 도구 추가/제거를 명시적으로 요청하면 Create/Modify-AddTool로 응답.
- 결과를 무시한 추정은 금물. 반드시 data 값을 근거로 제시.";

        /// <summary>
        /// Phase 4a: SLM이 도구의 ROI를 텍스트로 지정하는 힌트 규약.
        /// </summary>
        private const string RoiHintSection =
@"[ROI 힌트 / Region of Interest Hint]
사용자가 검사 영역을 한정하는 의도를 보이면, 도구에 ""RoiHint"" 필드를 추가하십시오.
사용 가능한 Strategy:
- FullImage: 전체 이미지. 기본값(생략 가능).
- CenterRect: 이미지 중앙 영역만. ""MarginPercent"": 0~45 로 가장자리 여백 비율 지정.
- Custom: 명시적 좌표. ""X"", ""Y"", ""Width"", ""Height"" 필수.

판단 규칙:
- ""중앙만"", ""가운데"", ""중심부"" → CenterRect, MarginPercent ~20
- ""위쪽"", ""아래쪽"", ""왼쪽 절반"" 등 구체적 영역 → Custom (이미지 크기는 [Image Analysis] 컨텍스트에 pixelCount/roi로 들어옴, 또는 사용자가 명시한 비율로 추정)
- ""전체"", 언급 없음 → RoiHint 생략

예시:
{ ""ToolType"": ""BlobTool"", ""ToolName"": ""Center"", ""InputSource"": ""Threshold"",
  ""RoiHint"": { ""Strategy"": ""CenterRect"", ""MarginPercent"": 20 },
  ""Parameters"": { ""MinArea"": 50 } }

주의: RoiHint는 그 도구의 검사 영역만 한정합니다. 다음 도구들에 자동 전파되지 않습니다.";

        /// <summary>
        /// Phase 3: ImageDependent 파라미터 결정 전 이미지 분석을 요청할 수 있는 2단계 추론 지시.
        /// </summary>
        private const string TwoStepInferenceSection =
@"[2단계 추론 / Two-step Inference]
ImageDependent Tier 파라미터(ThresholdValue, EdgeThreshold, CValue 등)를 정확히 정하려면
실제 이미지 통계가 필요합니다. 다음 두 가지 중 하나를 출력하십시오:

(A) 분석이 필요한 경우 — 첫 응답에서 분석만 요청:
{
  ""AnalysisRequests"": [""histogram""],
  ""Reason"": ""BlobTool 임계값 결정을 위해 픽셀 분포 필요"",
  ""AnalysisRoi"": { ""Strategy"": ""CenterRect"", ""MarginPercent"": 20 }  // 선택 — 분석 영역 한정
}
사용 가능한 함수: histogram, edges
- histogram: mean, stdDev, p10/p50/p90, otsuThreshold, hasBimodal, darkPeak, lightPeak
- edges: edgeDensity, meanGradientMagnitude, dominantAngleDeg, suggestedCannyLow/High

분석이 완료되면 [Image Analysis] 컨텍스트가 다음 턴에 자동 주입됩니다.
그때 비로소 최종 ""Action"": ""Create""/""Modify"" JSON을 출력하십시오.

(B) Tier=Semantic/DomainCommon 파라미터만 필요한 경우 — 분석 없이 바로 최종 JSON.

판단 기준:
- 사용자가 ""임계값"", ""에지 강도"", ""밝기"" 같이 통계를 언급 → 분석 요청
- 사용자가 위치/형상/크기만 언급 → 분석 없이 바로 응답
- 이미 [Image Analysis] 컨텍스트가 들어와 있으면 절대 다시 요청하지 말고 최종 JSON 출력";

        // Phase 2~5: 어트리뷰트 스키마 + 2단계 추론 + ROI 힌트 + 실행 피드백.
        private static readonly string SystemPrompt =
            CombineSections(SystemPromptBase, TunableParamSection, TwoStepInferenceSection,
                RoiHintSection, ExecutionFeedbackSection);

        private static readonly string OperatorSystemPrompt =
            CombineSections(OperatorSystemPromptBase, TunableParamSection, TwoStepInferenceSection,
                RoiHintSection, ExecutionFeedbackSection);

        private static string CombineSections(params string[] parts)
        {
            return string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        #endregion

        private string ActiveSystemPrompt => _isOperatorMode ? OperatorSystemPrompt : SystemPrompt;

        public bool IsModelLoaded { get; private set; }

        public async Task LoadModelAsync(string modelName)
        {
            try
            {
                var response = await _httpClient.GetAsync(OllamaBaseUrl);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot connect to Ollama at {OllamaBaseUrl}. " +
                    "Ensure Ollama is installed and running. " +
                    $"Details: {ex.Message}", ex);
            }

            _modelName = modelName;
            _messages.Clear();
            _messages.Add(new { role = "system", content = ActiveSystemPrompt });
            IsModelLoaded = true;
        }

        public async Task<string> SendMessageAsync(string message, Action<string>? onToken = null, CancellationToken cancellationToken = default)
        {
            if (!IsModelLoaded)
                throw new InvalidOperationException("Model is not loaded. Call LoadModelAsync first.");

            // Update system prompt
            if (_messages.Count > 0)
                _messages[0] = new { role = "system", content = ActiveSystemPrompt };

            // Prepend canvas context if available
            string fullMessage = string.IsNullOrEmpty(_canvasContext)
                ? message
                : $"[Current Canvas State]\n{_canvasContext}\n\n[User Request]\n{message}";

            _messages.Add(new { role = "user", content = fullMessage });
            TrimHistory();

            string assistantResponse = await CallOllamaAsync(onToken, cancellationToken);
            _messages.Add(new { role = "assistant", content = assistantResponse });

            // Retry if invalid JSON
            string cleaned = StripMarkdownCodeBlock(assistantResponse);
            if (!IsValidJson(cleaned) && MaxRetryOnParseFailure > 0)
            {
                _messages.Add(new { role = "user", content = "위 응답이 유효한 JSON이 아닙니다. 마크다운이나 설명 없이 순수 JSON만 응답해 주세요." });
                onToken?.Invoke("\n[Retrying...]\n");

                string retryResponse = await CallOllamaAsync(onToken, cancellationToken);
                _messages.Add(new { role = "assistant", content = retryResponse });
                assistantResponse = retryResponse;
            }

            return assistantResponse;
        }

        private async Task<string> CallOllamaAsync(Action<string>? onToken, CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = _modelName,
                messages = _messages,
                stream = true,
                format = "json",
                options = new
                {
                    temperature = 0.15,
                    num_predict = 2048
                }
            };

            string json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, OllamaBaseUrl + "/api/chat")
            {
                Content = httpContent
            };

            var fullResponse = new StringBuilder();

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var chunk = JsonDocument.Parse(line);
                    var messageObj = chunk.RootElement.GetProperty("message");
                    if (messageObj.TryGetProperty("content", out var contentProp))
                    {
                        string? tokenContent = contentProp.GetString();
                        if (!string.IsNullOrEmpty(tokenContent))
                        {
                            fullResponse.Append(tokenContent);
                            onToken?.Invoke(tokenContent);
                        }
                    }

                    if (chunk.RootElement.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean())
                        break;
                }
                catch (JsonException)
                {
                    // Skip malformed chunks
                }
            }

            return fullResponse.ToString();
        }

        private void TrimHistory()
        {
            if (_messages.Count <= MaxHistoryMessages + 1)
                return;

            int removeCount = _messages.Count - MaxHistoryMessages - 1;
            _messages.RemoveRange(1, removeCount);
        }

        public static string StripMarkdownCodeBlock(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            return text.Trim();
        }

        private static bool IsValidJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (!text.StartsWith('{') && !text.StartsWith('['))
                return false;

            try
            {
                JsonDocument.Parse(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ResetSession()
        {
            _messages.Clear();
            if (!string.IsNullOrEmpty(_modelName))
            {
                _messages.Add(new { role = "system", content = ActiveSystemPrompt });
            }
        }

        public void SetMode(bool isOperatorMode)
        {
            _isOperatorMode = isOperatorMode;
        }

        public void SetCanvasContext(string? context)
        {
            _canvasContext = context;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _messages.Clear();
                IsModelLoaded = false;
                _disposed = true;
            }
        }
    }
}
