using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Identification
{
    /// <summary>
    /// OCR 엔진 타입
    /// </summary>
    public enum OcrEngineType
    {
        /// <summary>Tesseract 5 LSTM 기반</summary>
        Tesseract,
        /// <summary>PP-OCRv4 ONNX 기반 (한글/산업용 텍스트에 강력)</summary>
        PPOcrOnnx
    }

    /// <summary>
    /// OCR 엔진 모드
    /// </summary>
    public enum OcrEngineMode
    {
        /// <summary>LSTM 신경망 (기본, 정확도 우선)</summary>
        LstmOnly,
        /// <summary>Legacy + LSTM 결합</summary>
        Combined,
        /// <summary>Legacy Tesseract (속도 우선)</summary>
        LegacyOnly
    }

    /// <summary>
    /// 페이지 분할 모드 (Tesseract PSM)
    /// </summary>
    public enum OcrPageSegMode
    {
        /// <summary>자동 감지</summary>
        Auto,
        /// <summary>단일 텍스트 블록</summary>
        SingleBlock,
        /// <summary>단일 라인</summary>
        SingleLine,
        /// <summary>단일 단어</summary>
        SingleWord,
        /// <summary>단일 문자</summary>
        SingleChar,
        /// <summary>세로 텍스트 (단일 블록)</summary>
        VerticalBlock
    }

    /// <summary>
    /// OCR 언어
    /// </summary>
    public enum OcrLanguage
    {
        /// <summary>영어</summary>
        English,
        /// <summary>한국어</summary>
        Korean,
        /// <summary>일본어</summary>
        Japanese,
        /// <summary>중국어 (간체)</summary>
        ChineseSimplified,
        /// <summary>영어 + 한국어</summary>
        EnglishKorean
    }

    /// <summary>
    /// OCRTool — Tesseract 5 LSTM 기반 문자 인식 도구.
    /// 유통기한, 시리얼 번호, 로트 번호 등 산업용 문자 인식에 사용됩니다.
    /// Cognex OcrMaxTool 대체.
    /// </summary>
    public class OCRTool : VisionToolBase
    {
        // ── Engine Cache ──
        private TesseractEngine? _cachedEngine;
        private string _cachedEngineKey = string.Empty;
        private PaddleOcrOnnxEngine? _onnxEngine;

        // ── Engine Type ──

        private OcrEngineType _ocrEngineType = OcrEngineType.Tesseract;
        /// <summary>
        /// OCR 엔진 선택 (Tesseract 또는 PP-OCR ONNX)
        /// </summary>
        public OcrEngineType OcrEngine
        {
            get => _ocrEngineType;
            set => SetProperty(ref _ocrEngineType, value);
        }

        // ── PP-OCR ONNX Settings ──

        private string _customDetModelPath = string.Empty;
        /// <summary>
        /// 커스텀 Detection 모델 경로 (.onnx). 빈 문자열이면 기본 모델 사용.
        /// Fine-tuning된 모델을 지정하면 기존 엔진 캐시가 무효화됩니다.
        /// </summary>
        public string CustomDetModelPath
        {
            get => _customDetModelPath;
            set { if (SetProperty(ref _customDetModelPath, value ?? string.Empty)) InvalidateOnnxEngine(); }
        }

        private string _customRecModelPath = string.Empty;
        /// <summary>
        /// 커스텀 Recognition 모델 경로 (.onnx). 빈 문자열이면 기본 모델 사용.
        /// </summary>
        public string CustomRecModelPath
        {
            get => _customRecModelPath;
            set { if (SetProperty(ref _customRecModelPath, value ?? string.Empty)) InvalidateOnnxEngine(); }
        }

        private string _customDictPath = string.Empty;
        /// <summary>
        /// 커스텀 딕셔너리 파일 경로 (.txt). 빈 문자열이면 기본 딕셔너리 사용.
        /// </summary>
        public string CustomDictPath
        {
            get => _customDictPath;
            set { if (SetProperty(ref _customDictPath, value ?? string.Empty)) InvalidateOnnxEngine(); }
        }

        /// <summary>커스텀 모델이 하나라도 지정되었는지 여부</summary>
        public bool HasCustomModel =>
            !string.IsNullOrEmpty(CustomDetModelPath) ||
            !string.IsNullOrEmpty(CustomRecModelPath) ||
            !string.IsNullOrEmpty(CustomDictPath);

        private void InvalidateOnnxEngine()
        {
            _onnxEngine?.Dispose();
            _onnxEngine = null;
        }

        private int _maxSideLen = 960;
        /// <summary>
        /// PP-OCR ONNX 검출 시 이미지 최대 변 길이 (px).
        /// 클수록 작은 글자 인식에 유리하나 속도가 저하됩니다.
        /// 산업용 기본: 960, 조밀한 텍스트(신문/표): 1600~2000
        /// </summary>
        public int MaxSideLen
        {
            get => _maxSideLen;
            set => SetProperty(ref _maxSideLen, Math.Clamp(value, 320, 4096));
        }

        // ── Detection Settings ──

        private OcrLanguage _language = OcrLanguage.English;
        public OcrLanguage Language
        {
            get => _language;
            set { if (SetProperty(ref _language, value)) InvalidateEngineCache(); }
        }

        private OcrPageSegMode _pageSegMode = OcrPageSegMode.SingleLine;
        public OcrPageSegMode PageSegMode
        {
            get => _pageSegMode;
            set => SetProperty(ref _pageSegMode, value);
        }

        private OcrEngineMode _engineMode = OcrEngineMode.LstmOnly;
        public OcrEngineMode EngineMode
        {
            get => _engineMode;
            set { if (SetProperty(ref _engineMode, value)) InvalidateEngineCache(); }
        }

        private string _characterWhitelist = string.Empty;
        /// <summary>
        /// 인식 허용 문자 제한 (빈 문자열 = 제한 없음).
        /// 예: "0123456789" → 숫자만 인식
        /// </summary>
        public string CharacterWhitelist
        {
            get => _characterWhitelist;
            set => SetProperty(ref _characterWhitelist, value);
        }

        private double _confidenceThreshold = 40.0;
        /// <summary>
        /// 최소 신뢰도 임계값 (0~100). 이 값 이하의 결과는 무시됩니다.
        /// </summary>
        public double ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => SetProperty(ref _confidenceThreshold, Math.Clamp(value, 0, 100));
        }

        // ── Preprocessing Settings ──

        private bool _autoPreprocess = true;
        /// <summary>
        /// 자동 전처리 (이진화, 노이즈 제거) 활성화
        /// </summary>
        public bool AutoPreprocess
        {
            get => _autoPreprocess;
            set => SetProperty(ref _autoPreprocess, value);
        }

        private bool _invertImage;
        /// <summary>
        /// 이미지 반전 (흰 배경에 검은 글씨 → 검은 배경에 흰 글씨)
        /// Tesseract는 흰 배경에 검은 글씨를 선호합니다.
        /// </summary>
        public bool InvertImage
        {
            get => _invertImage;
            set => SetProperty(ref _invertImage, value);
        }

        private int _targetTextHeight = 40;
        /// <summary>
        /// 목표 문자 높이(px). 이미지의 문자가 이 높이보다 작으면 자동 스케일업합니다.
        /// Tesseract는 30~40px 이상의 문자 높이에서 최적 성능을 발휘합니다.
        /// 0이면 스케일업을 비활성화합니다.
        /// </summary>
        public int TargetTextHeight
        {
            get => _targetTextHeight;
            set => SetProperty(ref _targetTextHeight, Math.Clamp(value, 0, 200));
        }

        private int _denoiseLevel = 1;
        /// <summary>
        /// 노이즈 제거 강도 (0=없음, 1=약, 2=중, 3=강).
        /// GaussianBlur 커널 크기: 0→없음, 1→3x3, 2→5x5, 3→7x7
        /// </summary>
        public int DenoiseLevel
        {
            get => _denoiseLevel;
            set => SetProperty(ref _denoiseLevel, Math.Clamp(value, 0, 3));
        }

        private bool _dotMatrixMode;
        /// <summary>
        /// 도트 매트릭스 모드. 도트 프린트로 인쇄된 끊어진 문자를 팽창(Dilation)으로 연결합니다.
        /// </summary>
        public bool DotMatrixMode
        {
            get => _dotMatrixMode;
            set => SetProperty(ref _dotMatrixMode, value);
        }

        // ── Verification Settings ──

        private bool _enableVerification;
        public bool EnableVerification
        {
            get => _enableVerification;
            set => SetProperty(ref _enableVerification, value);
        }

        private string _expectedText = string.Empty;
        public string ExpectedText
        {
            get => _expectedText;
            set => SetProperty(ref _expectedText, value);
        }

        private bool _useRegexMatch;
        public bool UseRegexMatch
        {
            get => _useRegexMatch;
            set => SetProperty(ref _useRegexMatch, value);
        }

        // ── Display Settings ──

        private bool _drawOverlay = true;
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        // ── Tessdata Path ──

        private string _tessdataPath = string.Empty;
        /// <summary>
        /// tessdata 폴더 경로. 비어있으면 실행 파일 기준 ./tessdata/ 사용.
        /// </summary>
        public string TessdataPath
        {
            get => _tessdataPath;
            set { if (SetProperty(ref _tessdataPath, value)) InvalidateEngineCache(); }
        }

        public OCRTool()
        {
            Name = "OCR";
            ToolType = "OCRTool";
        }

        private void InvalidateEngineCache()
        {
            _cachedEngine?.Dispose();
            _cachedEngine = null;
            _cachedEngineKey = string.Empty;

            _onnxEngine?.Dispose();
            _onnxEngine = null;
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // 회전된 ROI 여부 판단
                bool useAffineROI = UseROI && Math.Abs(ROIAngle) > 0.1
                    && ROIWidth > 0 && ROIHeight > 0;

                Mat workImage;
                double affineAngle = 0;
                double affineCenterX = 0, affineCenterY = 0;
                int affineW = 0, affineH = 0;

                if (useAffineROI)
                {
                    affineCenterX = ROICenterX;
                    affineCenterY = ROICenterY;
                    affineW = ROIWidth;
                    affineH = ROIHeight;
                    affineAngle = ROIAngle;
                    workImage = ExtractAffineROI(inputImage, affineCenterX, affineCenterY,
                        affineW, affineH, affineAngle);
                }
                else
                {
                    workImage = GetROIImage(inputImage);
                }

                try
                {
                    OcrResultData ocrResult;

                    if (OcrEngine == OcrEngineType.PPOcrOnnx)
                    {
                        ocrResult = ExecutePaddleOCR(workImage);
                    }
                    else
                    {
                        ocrResult = ExecuteTesseract(workImage);
                    }

                    string recognizedText = ocrResult.Text?.Trim() ?? string.Empty;
                    float meanConfidence = ocrResult.MeanConfidence;

                    // 결과 데이터 저장
                    result.Data["RecognizedText"] = recognizedText;
                    result.Data["Confidence"] = (double)meanConfidence;
                    result.Data["WordCount"] = ocrResult.Words.Count;
                    result.Data["Engine"] = OcrEngine.ToString();

                    if (ocrResult.Words.Count > 0)
                    {
                        result.Data["Words"] = string.Join(", ", ocrResult.Words.Select(w => w.Text));
                        result.Data["WordConfidences"] = string.Join(", ",
                            ocrResult.Words.Select(w => $"{w.Confidence:F1}"));
                    }

                    // 신뢰도 검사
                    bool confidencePass = meanConfidence >= ConfidenceThreshold;

                    // 검증 로직
                    if (EnableVerification && !string.IsNullOrEmpty(ExpectedText))
                    {
                        bool textMatch;
                        if (UseRegexMatch)
                        {
                            try { textMatch = Regex.IsMatch(recognizedText, ExpectedText); }
                            catch { textMatch = false; }
                        }
                        else
                        {
                            textMatch = string.Equals(recognizedText, ExpectedText,
                                StringComparison.OrdinalIgnoreCase);
                        }

                        result.Data["VerificationPass"] = textMatch;
                        result.Success = confidencePass && textMatch;
                        result.Message = textMatch
                            ? $"OCR PASS: \"{recognizedText}\" (신뢰도: {meanConfidence:F1}%)"
                            : $"OCR FAIL: \"{recognizedText}\" ≠ \"{ExpectedText}\" (신뢰도: {meanConfidence:F1}%)";
                    }
                    else
                    {
                        result.Success = confidencePass && !string.IsNullOrWhiteSpace(recognizedText);
                        result.Message = result.Success
                            ? $"OCR: \"{recognizedText}\" (신뢰도: {meanConfidence:F1}%)"
                            : string.IsNullOrWhiteSpace(recognizedText)
                                ? "문자를 인식하지 못했습니다."
                                : $"신뢰도 부족: {meanConfidence:F1}% < {ConfidenceThreshold:F1}%";
                    }

                    // 오버레이 그리기
                    if (DrawOverlay)
                    {
                        Mat overlay = GetColorOverlayBase(inputImage);
                        DrawOCROverlay(overlay, inputImage, ocrResult, result.Success,
                            useAffineROI, affineCenterX, affineCenterY, affineW, affineH, affineAngle);
                        result.OverlayImage = overlay;
                    }

                    result.OutputImage = inputImage.Clone();
                }
                finally
                {
                    if (workImage != inputImage)
                        workImage.Dispose();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"OCR 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        /// <summary>
        /// Tesseract 엔진으로 OCR 수행
        /// </summary>
        private OcrResultData ExecuteTesseract(Mat workImage)
        {
            string tessdataDir = ResolveTessdataPath();
            string langCode = GetTesseractLanguageCode();

            string trainedDataFile = Path.Combine(tessdataDir, $"{langCode.Split('+')[0]}.traineddata");
            if (!File.Exists(trainedDataFile))
                throw new FileNotFoundException(
                    $"tessdata 파일을 찾을 수 없습니다: {trainedDataFile}\n" +
                    $"tessdata 폴더에 '{langCode}.traineddata' 파일을 배치하세요.");

            using var preprocessed = PreprocessForOCR(workImage);

            double scaleRatio = 1.0;
            Mat ocrInput;
            if (TargetTextHeight > 0 && preprocessed.Height < TargetTextHeight * 2)
            {
                scaleRatio = (double)(TargetTextHeight * 2) / preprocessed.Height;
                scaleRatio = Math.Min(scaleRatio, 4.0);
                if (scaleRatio > 1.2)
                {
                    ocrInput = new Mat();
                    Cv2.Resize(preprocessed, ocrInput, new Size(0, 0), scaleRatio, scaleRatio,
                        InterpolationFlags.Cubic);
                }
                else
                {
                    ocrInput = preprocessed;
                    scaleRatio = 1.0;
                }
            }
            else
            {
                ocrInput = preprocessed;
            }

            const int padding = 10;
            using var padded = new Mat();
            Cv2.CopyMakeBorder(ocrInput, padded, padding, padding, padding, padding,
                BorderTypes.Constant, new Scalar(255));

            if (ocrInput != preprocessed)
                ocrInput.Dispose();

            var engine = GetOrCreateEngine(tessdataDir, langCode);
            return RunTesseract(padded, engine, padding, scaleRatio);
        }

        /// <summary>
        /// PP-OCRv4 ONNX 엔진으로 OCR 수행.
        /// Microsoft.ML.OnnxRuntime 기반 — PaddlePaddle 네이티브 DLL 없이 안전하게 인프로세스 실행.
        /// </summary>
        private OcrResultData ExecutePaddleOCR(Mat workImage)
        {
            _onnxEngine ??= HasCustomModel
                ? new PaddleOcrOnnxEngine(
                    string.IsNullOrEmpty(CustomDetModelPath) ? null : CustomDetModelPath,
                    string.IsNullOrEmpty(CustomRecModelPath) ? null : CustomRecModelPath,
                    string.IsNullOrEmpty(CustomDictPath) ? null : CustomDictPath)
                : new PaddleOcrOnnxEngine();
            _onnxEngine.MaxSideLen = MaxSideLen;

            var ocrResults = _onnxEngine.Run(workImage);

            if (ocrResults.Count == 0)
                return new OcrResultData(string.Empty, 0f, new List<OcrWord>());

            var words = new List<OcrWord>();
            float totalScore = 0f;

            foreach (var block in ocrResults)
            {
                float score = block.Confidence * 100f; // 0~1 → 0~100
                totalScore += score;

                // Polygon → BoundingBox
                if (block.Polygon.Length >= 4)
                {
                    int minX = (int)block.Polygon.Min(p => p.X);
                    int minY = (int)block.Polygon.Min(p => p.Y);
                    int maxX = (int)block.Polygon.Max(p => p.X);
                    int maxY = (int)block.Polygon.Max(p => p.Y);
                    words.Add(new OcrWord(block.Text, score,
                        new OpenCvSharp.Rect(minX, minY, maxX - minX, maxY - minY)));
                }
                else
                {
                    words.Add(new OcrWord(block.Text, score, new OpenCvSharp.Rect()));
                }
            }

            string fullText = string.Join(" ", words.Select(w => w.Text));
            float meanConfidence = words.Count > 0 ? totalScore / words.Count : 0f;

            return new OcrResultData(fullText, meanConfidence, words);
        }

        #region Preprocessing

        /// <summary>
        /// OCR용 이미지 전처리 파이프라인 (Cognex OCRMax 수준 목표):
        /// 그레이스케일 → CLAHE → 노이즈 제거 → 이진화 → (반전) → 형태학 처리
        /// </summary>
        private Mat PreprocessForOCR(Mat image)
        {
            // 1단계: 그레이스케일 변환
            Mat current;
            if (image.Channels() > 1)
            {
                current = new Mat();
                Cv2.CvtColor(image, current, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                current = image.Clone();
            }

            if (!AutoPreprocess)
            {
                if (InvertImage) Cv2.BitwiseNot(current, current);
                return current;
            }

            // 2단계: CLAHE 대비 향상 (저대비 마킹, 불균일 조명 보정)
            using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8)))
            {
                var enhanced = new Mat();
                clahe.Apply(current, enhanced);
                current.Dispose();
                current = enhanced;
            }

            // 3단계: 노이즈 제거 (GaussianBlur — 이진화 전에 적용하여 문자 외곽선 안정화)
            if (DenoiseLevel > 0)
            {
                int ksize = DenoiseLevel * 2 + 1; // 1→3, 2→5, 3→7
                Cv2.GaussianBlur(current, current, new Size(ksize, ksize), 0);
            }

            // 4단계: 이진화 (적응형 + Otsu 자동 선택)
            Mat binary;
            {
                var adaptive = new Mat();
                int blockSize = Math.Max(11, (Math.Min(current.Width, current.Height) / 15) | 1);
                Cv2.AdaptiveThreshold(current, adaptive, 255,
                    AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, blockSize, 5);

                var otsu = new Mat();
                Cv2.Threshold(current, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                current.Dispose();

                // 검은 픽셀(문자) 비율이 이상적 범위(~20%)에 가까운 쪽 선택
                double totalPixels = adaptive.Width * adaptive.Height;
                double adaptiveRatio = 1.0 - Cv2.CountNonZero(adaptive) / totalPixels;
                double otsuRatio = 1.0 - Cv2.CountNonZero(otsu) / totalPixels;
                const double idealRatio = 0.20;

                if (Math.Abs(adaptiveRatio - idealRatio) <= Math.Abs(otsuRatio - idealRatio))
                {
                    binary = adaptive;
                    otsu.Dispose();
                }
                else
                {
                    binary = otsu;
                    adaptive.Dispose();
                }
            }

            // 5단계: 반전 처리 (이진화 후 적용 — Tesseract는 흰 배경+검은 글씨 선호)
            if (InvertImage)
            {
                Cv2.BitwiseNot(binary, binary);
            }

            // 6단계: 형태학적 처리
            if (DotMatrixMode)
            {
                // 도트 매트릭스: 팽창으로 끊어진 도트 문자 연결 → 오프닝으로 잔여 노이즈 제거
                using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
                Cv2.Dilate(binary, binary, dilateKernel, iterations: 1);

                using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
                var cleaned = new Mat();
                Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, openKernel);
                binary.Dispose();
                return cleaned;
            }
            else
            {
                // 일반: 오프닝으로 작은 노이즈 제거
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
                var cleaned = new Mat();
                Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
                binary.Dispose();
                return cleaned;
            }
        }

        #endregion

        #region Tesseract Engine

        private record OcrWord(string Text, float Confidence, OpenCvSharp.Rect BoundingBox);

        private record OcrResultData(string Text, float MeanConfidence, List<OcrWord> Words);

        /// <summary>
        /// 엔진 캐시 — 동일 설정이면 재사용 (초기화 200~500ms 절약)
        /// </summary>
        private TesseractEngine GetOrCreateEngine(string tessdataPath, string langCode)
        {
            var engineMode = EngineMode switch
            {
                OcrEngineMode.LstmOnly => Tesseract.EngineMode.LstmOnly,
                OcrEngineMode.Combined => Tesseract.EngineMode.TesseractAndLstm,
                OcrEngineMode.LegacyOnly => Tesseract.EngineMode.TesseractOnly,
                _ => Tesseract.EngineMode.LstmOnly
            };

            string key = $"{tessdataPath}|{langCode}|{engineMode}";
            if (_cachedEngine != null && _cachedEngineKey == key)
                return _cachedEngine;

            _cachedEngine?.Dispose();
            _cachedEngine = new TesseractEngine(tessdataPath, langCode, engineMode);
            _cachedEngineKey = key;
            return _cachedEngine;
        }

        /// <summary>
        /// Tesseract OCR 수행 (캐시된 엔진 사용, 직접 픽셀 전달)
        /// </summary>
        private OcrResultData RunTesseract(Mat image, TesseractEngine engine, int padding, double scaleRatio)
        {
            // PSM 설정
            engine.DefaultPageSegMode = PageSegMode switch
            {
                OcrPageSegMode.Auto => Tesseract.PageSegMode.Auto,
                OcrPageSegMode.SingleBlock => Tesseract.PageSegMode.SingleBlock,
                OcrPageSegMode.SingleLine => Tesseract.PageSegMode.SingleLine,
                OcrPageSegMode.SingleWord => Tesseract.PageSegMode.SingleWord,
                OcrPageSegMode.SingleChar => Tesseract.PageSegMode.SingleChar,
                OcrPageSegMode.VerticalBlock => Tesseract.PageSegMode.SingleBlockVertText,
                _ => Tesseract.PageSegMode.Auto
            };

            // 문자 화이트리스트
            engine.SetVariable("tessedit_char_whitelist",
                string.IsNullOrEmpty(CharacterWhitelist) ? "" : CharacterWhitelist);

            // OpenCV Mat → Tesseract Pix 직접 변환 (PNG 인코딩 우회)
            using var pix = MatToPix(image);

            using var page = engine.Process(pix);
            string text = page.GetText();
            float confidence = page.GetMeanConfidence() * 100f; // 0~1 → 0~100

            // 단어별 결과 추출 (패딩/스케일 역변환 적용)
            var words = new List<OcrWord>();
            using var iter = page.GetIterator();
            iter.Begin();
            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                {
                    string? wordText = iter.GetText(PageIteratorLevel.Word);
                    float wordConf = iter.GetConfidence(PageIteratorLevel.Word);
                    if (!string.IsNullOrWhiteSpace(wordText))
                    {
                        // 패딩/스케일 역변환
                        int x = (int)((bounds.X1 - padding) / scaleRatio);
                        int y = (int)((bounds.Y1 - padding) / scaleRatio);
                        int w = (int)(bounds.Width / scaleRatio);
                        int h = (int)(bounds.Height / scaleRatio);

                        words.Add(new OcrWord(
                            wordText.Trim(),
                            wordConf,
                            new OpenCvSharp.Rect(Math.Max(0, x), Math.Max(0, y), w, h)));
                    }
                }
            } while (iter.Next(PageIteratorLevel.Word));

            return new OcrResultData(text, confidence, words);
        }

        /// <summary>
        /// OpenCV Mat → Tesseract Pix 변환 (BMP 인코딩 — PNG보다 10배 빠름)
        /// </summary>
        private static Pix MatToPix(Mat image)
        {
            Cv2.ImEncode(".bmp", image, out byte[] imageBytes);
            return Pix.LoadFromMemory(imageBytes);
        }

        #endregion

        #region Overlay

        private void DrawOCROverlay(Mat overlay, Mat inputImage, OcrResultData ocrResult, bool success,
            bool useAffineROI, double centerX, double centerY, int roiW, int roiH, double angle)
        {
            var color = success ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

            foreach (var word in ocrResult.Words)
            {
                if (word.Confidence < ConfidenceThreshold) continue;

                // 바운딩 박스 좌표 변환
                var pts = new[]
                {
                    new Point2d(word.BoundingBox.X, word.BoundingBox.Y),
                    new Point2d(word.BoundingBox.X + word.BoundingBox.Width, word.BoundingBox.Y),
                    new Point2d(word.BoundingBox.X + word.BoundingBox.Width, word.BoundingBox.Y + word.BoundingBox.Height),
                    new Point2d(word.BoundingBox.X, word.BoundingBox.Y + word.BoundingBox.Height)
                };

                OpenCvSharp.Point[] drawPts;
                if (useAffineROI)
                {
                    drawPts = pts.Select(p => TransformPointToOriginal(
                        p.X, p.Y, roiW, roiH, centerX, centerY, angle)).ToArray();
                }
                else
                {
                    var roi = GetAdjustedROI(inputImage);
                    drawPts = pts.Select(p => new OpenCvSharp.Point(
                        (int)(p.X + roi.X), (int)(p.Y + roi.Y))).ToArray();
                }

                // 바운딩 박스 그리기
                Cv2.Polylines(overlay, new[] { drawPts }, true, color, 2);

                // 텍스트 라벨
                var labelPos = new OpenCvSharp.Point(drawPts[0].X, drawPts[0].Y - 5);
                Cv2.PutText(overlay, $"{word.Text} ({word.Confidence:F0}%)",
                    labelPos, HersheyFonts.HersheySimplex, 0.5, color, 1);
            }

            // 전체 인식 결과 표시 (이미지 좌상단)
            string displayText = ocrResult.Text?.Trim().Replace("\n", " ") ?? "";
            if (displayText.Length > 60) displayText = displayText[..57] + "...";
            Cv2.PutText(overlay, displayText,
                new OpenCvSharp.Point(10, 30),
                HersheyFonts.HersheySimplex, 0.7, color, 2);
        }

        /// <summary>
        /// 전처리된 ROI 좌표를 원본 이미지 좌표로 역변환 (회전 ROI용)
        /// </summary>
        private static OpenCvSharp.Point TransformPointToOriginal(
            double px, double py, int roiW, int roiH,
            double centerX, double centerY, double angle)
        {
            double localX = px - roiW / 2.0;
            double localY = py - roiH / 2.0;

            double rad = angle * Math.PI / 180.0;
            double cosA = Math.Cos(rad);
            double sinA = Math.Sin(rad);

            double origX = localX * cosA - localY * sinA + centerX;
            double origY = localX * sinA + localY * cosA + centerY;

            return new OpenCvSharp.Point((int)origX, (int)origY);
        }

        #endregion

        #region Helpers

        private string ResolveTessdataPath()
        {
            if (!string.IsNullOrEmpty(TessdataPath) && Directory.Exists(TessdataPath))
                return TessdataPath;

            // 실행 파일 기준 tessdata 폴더
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string defaultPath = Path.Combine(appDir, "tessdata");
            if (Directory.Exists(defaultPath))
                return defaultPath;

            // 프로젝트 루트 기준
            string projectPath = Path.Combine(appDir, "..", "..", "..", "tessdata");
            if (Directory.Exists(projectPath))
                return Path.GetFullPath(projectPath);

            return defaultPath;
        }

        private string GetTesseractLanguageCode()
        {
            return Language switch
            {
                OcrLanguage.English => "eng",
                OcrLanguage.Korean => "kor",
                OcrLanguage.Japanese => "jpn",
                OcrLanguage.ChineseSimplified => "chi_sim",
                OcrLanguage.EnglishKorean => "eng+kor",
                _ => "eng"
            };
        }

        /// <summary>
        /// WarpAffine을 사용하여 회전된 ROI 영역을 수평 정규화된 이미지로 추출.
        /// </summary>
        private static Mat ExtractAffineROI(Mat image, double centerX, double centerY,
            int width, int height, double angle)
        {
            var center = new Point2f((float)centerX, (float)centerY);
            using var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            using var rotated = new Mat();
            Cv2.WarpAffine(image, rotated, rotMat, image.Size(),
                InterpolationFlags.Linear, BorderTypes.Replicate);

            int x = (int)(centerX - width / 2.0);
            int y = (int)(centerY - height / 2.0);
            int x1 = Math.Clamp(x, 0, rotated.Width);
            int y1 = Math.Clamp(y, 0, rotated.Height);
            int x2 = Math.Clamp(x + width, 0, rotated.Width);
            int y2 = Math.Clamp(y + height, 0, rotated.Height);

            if (x2 - x1 <= 0 || y2 - y1 <= 0)
                return image.Clone();

            return rotated[new OpenCvSharp.Rect(x1, y1, x2 - x1, y2 - y1)].Clone();
        }

        #endregion

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "RecognizedText", "Confidence", "WordCount",
                "Words", "WordConfidences", "VerificationPass"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new OCRTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                OcrEngine = this.OcrEngine,
                MaxSideLen = this.MaxSideLen,
                Language = this.Language,
                PageSegMode = this.PageSegMode,
                EngineMode = this.EngineMode,
                CharacterWhitelist = this.CharacterWhitelist,
                ConfidenceThreshold = this.ConfidenceThreshold,
                AutoPreprocess = this.AutoPreprocess,
                InvertImage = this.InvertImage,
                TargetTextHeight = this.TargetTextHeight,
                DenoiseLevel = this.DenoiseLevel,
                DotMatrixMode = this.DotMatrixMode,
                EnableVerification = this.EnableVerification,
                ExpectedText = this.ExpectedText,
                UseRegexMatch = this.UseRegexMatch,
                DrawOverlay = this.DrawOverlay,
                TessdataPath = this.TessdataPath,
                CustomDetModelPath = this.CustomDetModelPath,
                CustomRecModelPath = this.CustomRecModelPath,
                CustomDictPath = this.CustomDictPath
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
