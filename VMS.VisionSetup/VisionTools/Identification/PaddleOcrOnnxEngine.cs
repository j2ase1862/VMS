using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace VMS.VisionSetup.VisionTools.Identification
{
    /// <summary>
    /// PP-OCRv4 ONNX 기반 텍스트 검출 + 인식 엔진.
    /// PaddlePaddle 네이티브 런타임 없이 Microsoft.ML.OnnxRuntime만 사용합니다.
    /// </summary>
    public sealed class PaddleOcrOnnxEngine : IDisposable
    {
        private InferenceSession? _detSession;
        private InferenceSession? _recSession;
        private string[] _dictionary = Array.Empty<string>();
        private bool _disposed;

        // ── 모델 파일명 ──
        private const string DetModelFile = "ch_PP-OCRv4_det_infer.onnx";
        private const string RecModelFile = "ch_PP-OCRv4_rec_infer.onnx";
        private const string DictFile = "ppocr_keys_v1.txt";

        // ── 모델 다운로드 URL ──
        private static readonly Dictionary<string, string> ModelUrls = new()
        {
            [DetModelFile] = "https://huggingface.co/SWHL/RapidOCR/resolve/main/PP-OCRv4/ch_PP-OCRv4_det_infer.onnx",
            [RecModelFile] = "https://huggingface.co/SWHL/RapidOCR/resolve/main/PP-OCRv4/ch_PP-OCRv4_rec_infer.onnx",
            [DictFile] = "https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/ppocr/utils/ppocr_keys_v1.txt",
        };

        // ── Detection 파라미터 ──
        private int _maxSideLen = 960;
        /// <summary>
        /// 검출 시 이미지 최대 변 길이 (px). 클수록 작은 글자 인식에 유리하나 속도 저하.
        /// 산업용 기본: 960, 조밀한 텍스트(신문/표): 1600~2000
        /// </summary>
        public int MaxSideLen
        {
            get => _maxSideLen;
            set => _maxSideLen = Math.Clamp(value, 320, 4096);
        }
        private const float DetDbThresh = 0.3f;
        private const float DetDbBoxThresh = 0.5f;
        private const float DetDbUnclipRatio = 1.6f;
        private const int MinBoxSideLen = 5;

        // ── Recognition 파라미터 ──
        private const int RecImageHeight = 48;

        private string _whitelist = string.Empty;
        private HashSet<int>? _whitelistIndices;
        /// <summary>
        /// 인식 허용 문자 화이트리스트. 비어있으면 전체 사전 사용.
        /// CTC 디코딩 시 사전 인덱스 중 화이트리스트에 없는 char는 argmax 후보에서 제외 →
        /// 산업용 문자(숫자/날짜) 인식 시 '/' ↔ '7' 같은 시각적 혼동 차단.
        /// blank(idx 0)는 항상 허용.
        /// </summary>
        public string WhitelistChars
        {
            get => _whitelist;
            set
            {
                _whitelist = value ?? string.Empty;
                _whitelistIndices = null; // 사전 빌드는 첫 호출 시 lazy
            }
        }

        private HashSet<int>? GetWhitelistIndices()
        {
            if (string.IsNullOrEmpty(_whitelist)) return null;
            if (_whitelistIndices != null) return _whitelistIndices;
            var set = new HashSet<int> { 0 }; // blank 항상 허용
            for (int i = 1; i < _dictionary.Length; i++)
            {
                if (!string.IsNullOrEmpty(_dictionary[i]) && _whitelist.Contains(_dictionary[i]))
                    set.Add(i);
            }
            _whitelistIndices = set;
            return set;
        }

        // ── ImageNet normalization (Detection) ──
        private static readonly float[] DetMean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] DetStd = { 0.229f, 0.224f, 0.225f };

        /// <summary>
        /// 엔진 초기화. 모델 파일이 없으면 자동 다운로드를 시도합니다.
        /// </summary>
        /// <param name="modelDir">모델 파일 디렉토리 (null이면 기본 경로 사용)</param>
        public PaddleOcrOnnxEngine(string? modelDir = null)
        {
            modelDir ??= GetDefaultModelDir();
            Directory.CreateDirectory(modelDir);

            string detPath = Path.Combine(modelDir, DetModelFile);
            string recPath = Path.Combine(modelDir, RecModelFile);
            string dictPath = Path.Combine(modelDir, DictFile);

            // 모델 파일 존재 확인 — 없으면 다운로드 시도
            EnsureModelFiles(modelDir, detPath, recPath, dictPath);

            InitializeSessions(detPath, recPath, dictPath);
        }

        /// <summary>
        /// 커스텀 모델 파일로 엔진 초기화 (Fine-tuning된 모델 사용).
        /// 지정되지 않은 파일은 기본 모델을 사용합니다.
        /// </summary>
        public PaddleOcrOnnxEngine(string? customDetPath, string? customRecPath, string? customDictPath)
        {
            string defaultDir = GetDefaultModelDir();
            Directory.CreateDirectory(defaultDir);

            string detPath = ResolveModelPath(customDetPath, defaultDir, DetModelFile);
            string recPath = ResolveModelPath(customRecPath, defaultDir, RecModelFile);
            string dictPath = ResolveModelPath(customDictPath, defaultDir, DictFile);

            // 기본 모델이 없으면 다운로드
            EnsureModelFiles(defaultDir,
                string.IsNullOrEmpty(customDetPath) ? detPath : Path.Combine(defaultDir, DetModelFile),
                string.IsNullOrEmpty(customRecPath) ? recPath : Path.Combine(defaultDir, RecModelFile),
                string.IsNullOrEmpty(customDictPath) ? dictPath : Path.Combine(defaultDir, DictFile));

            InitializeSessions(detPath, recPath, dictPath);
        }

        private void InitializeSessions(string detPath, string recPath, string dictPath)
        {
            // ONNX Runtime 세션 생성
            var opts = new SessionOptions
            {
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };

            _detSession = new InferenceSession(detPath, opts);
            _recSession = new InferenceSession(recPath, opts);

            // 사전 로드: index 0 = blank (CTC), index 1..N = 문자
            var lines = File.ReadAllLines(dictPath);
            _dictionary = new string[lines.Length + 1];
            _dictionary[0] = ""; // CTC blank
            for (int i = 0; i < lines.Length; i++)
                _dictionary[i + 1] = lines[i];
        }

        private static string ResolveModelPath(string? customPath, string defaultDir, string defaultFileName)
        {
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                return customPath;
            return Path.Combine(defaultDir, defaultFileName);
        }

        /// <summary>
        /// 전체 OCR 파이프라인 실행: 텍스트 검출 → 인식.
        /// </summary>
        public List<OnnxOcrResult> Run(Mat image)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (image.Empty())
                return new List<OnnxOcrResult>();

            // 1) 텍스트 영역 검출
            var regions = DetectTextRegions(image);
            if (regions.Count == 0)
                return new List<OnnxOcrResult>();

            // 2) 각 영역에서 텍스트 인식
            var results = new List<OnnxOcrResult>();
            foreach (var region in regions)
            {
                using var crop = CropTextRegion(image, region);
                if (crop.Empty() || crop.Width < 2 || crop.Height < 2)
                    continue;

                var (text, confidence) = RecognizeText(crop);
                if (!string.IsNullOrWhiteSpace(text))
                    results.Add(new OnnxOcrResult(text.Trim(), confidence, region));
            }

            return results;
        }

        #region Detection

        /// <summary>
        /// DB (Differentiable Binarization) 기반 텍스트 영역 검출.
        /// </summary>
        private List<Point2f[]> DetectTextRegions(Mat image)
        {
            // 전처리: 리사이즈 + 정규화 + NCHW 텐서 변환
            int origH = image.Rows, origW = image.Cols;
            GetResizeInfo(origH, origW, out int resizedH, out int resizedW);

            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(resizedW, resizedH));

            using var rgb = new Mat();
            if (resized.Channels() == 1)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
            else
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            var inputTensor = CreateDetectionTensor(rgb, resizedH, resizedW);

            // 추론 실행
            var inputName = _detSession!.InputNames[0];
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            using var outputs = _detSession.Run(inputs);
            var outputTensor = outputs.First().AsTensor<float>();

            // 후처리: 확률맵 → 바이너리 → 윤곽선 → 바운딩 박스
            float ratioH = (float)origH / resizedH;
            float ratioW = (float)origW / resizedW;

            // DEBUG: 확률맵 통계 (Dimension/max/mean) — 모델 출력 검증용
            if (DebugLogger != null)
            {
                var dims = outputTensor.Dimensions.ToArray();
                float maxV = float.MinValue, sumV = 0f;
                int count = 0;
                foreach (var v in outputTensor) { if (v > maxV) maxV = v; sumV += v; count++; }
                DebugLogger($"  [det] tensor dims=[{string.Join(",", dims)}] max={maxV:F4} mean={sumV / count:F4} count={count}");
            }

            return PostprocessDetection(outputTensor, resizedH, resizedW, ratioH, ratioW);
        }

        /// <summary>(테스트용) 검출 단계 진단 로그 callback.</summary>
        public static Action<string>? DebugLogger { get; set; }

        private void GetResizeInfo(int origH, int origW, out int resizedH, out int resizedW)
        {
            float ratio = 1.0f;
            int maxLen = Math.Max(origH, origW);
            if (maxLen > _maxSideLen)
                ratio = (float)_maxSideLen / maxLen;
            else if (maxLen < _maxSideLen)
                ratio = (float)_maxSideLen / maxLen;  // 작은 이미지 확대

            resizedH = (int)(origH * ratio);
            resizedW = (int)(origW * ratio);

            // 32의 배수로 맞춤
            resizedH = Math.Max(32, ((resizedH + 31) / 32) * 32);
            resizedW = Math.Max(32, ((resizedW + 31) / 32) * 32);
        }

        private static DenseTensor<float> CreateDetectionTensor(Mat rgb, int h, int w)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });

            unsafe
            {
                byte* ptr = (byte*)rgb.Data;
                int stride = (int)rgb.Step();

                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = x * 3;
                        // RGB 순서 (이미 BGR→RGB 변환 완료)
                        tensor[0, 0, y, x] = (row[idx] / 255.0f - DetMean[0]) / DetStd[0];
                        tensor[0, 1, y, x] = (row[idx + 1] / 255.0f - DetMean[1]) / DetStd[1];
                        tensor[0, 2, y, x] = (row[idx + 2] / 255.0f - DetMean[2]) / DetStd[2];
                    }
                }
            }

            return tensor;
        }

        private static List<Point2f[]> PostprocessDetection(
            Tensor<float> output, int h, int w, float ratioH, float ratioW)
        {
            // 확률맵 → OpenCV Mat
            using var probMap = new Mat(h, w, MatType.CV_32FC1);
            unsafe
            {
                float* ptr = (float*)probMap.Data;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        ptr[y * w + x] = output[0, 0, y, x];
            }

            // 바이너리맵 생성
            using var binaryMap = new Mat();
            Cv2.Threshold(probMap, binaryMap, DetDbThresh, 255, ThresholdTypes.Binary);
            binaryMap.ConvertTo(binaryMap, MatType.CV_8UC1);

            // 윤곽선 찾기
            Cv2.FindContours(binaryMap, out Point[][] contours, out _,
                RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            var results = new List<Point2f[]>();
            int rejTooShort = 0, rejScore = 0, rejSize = 0, rejPerim = 0;

            foreach (var contour in contours)
            {
                if (contour.Length < 4) { rejTooShort++; continue; }

                // 박스 점수 계산 (윤곽선 내부 확률 평균)
                float score = ComputeBoxScore(probMap, contour);
                if (score < DetDbBoxThresh) { rejScore++; continue; }

                // 최소 면적 회전 사각형 → 언클립(확장)
                var rect = Cv2.MinAreaRect(contour);
                if (rect.Size.Width < MinBoxSideLen || rect.Size.Height < MinBoxSideLen)
                { rejSize++; continue; }

                // 언클립: 폴리곤 확장
                double area = Cv2.ContourArea(contour);
                double perimeter = Cv2.ArcLength(contour, true);
                if (perimeter < 1.0) { rejPerim++; continue; }

                float distance = (float)(area * DetDbUnclipRatio / perimeter);
                rect.Size = new Size2f(
                    rect.Size.Width + distance * 2,
                    rect.Size.Height + distance * 2);

                // 원본 좌표로 스케일 변환
                var points = rect.Points();
                for (int i = 0; i < 4; i++)
                {
                    points[i].X = Math.Clamp(points[i].X * ratioW, 0, float.MaxValue);
                    points[i].Y = Math.Clamp(points[i].Y * ratioH, 0, float.MaxValue);
                }

                results.Add(points);
            }

            DebugLogger?.Invoke($"  [post] contours={contours.Length} kept={results.Count} " +
                $"rej(short/score/size/perim)={rejTooShort}/{rejScore}/{rejSize}/{rejPerim}");
            return results;
        }

        private static float ComputeBoxScore(Mat probMap, Point[] contour)
        {
            var boundingRect = Cv2.BoundingRect(contour);

            int xMin = Math.Max(0, boundingRect.X);
            int yMin = Math.Max(0, boundingRect.Y);
            int xMax = Math.Min(probMap.Cols, boundingRect.X + boundingRect.Width);
            int yMax = Math.Min(probMap.Rows, boundingRect.Y + boundingRect.Height);
            if (xMax <= xMin || yMax <= yMin) return 0f;

            int w = xMax - xMin, h = yMax - yMin;

            using var mask = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));
            var shifted = contour.Select(p => new Point(p.X - xMin, p.Y - yMin)).ToArray();
            Cv2.FillPoly(mask, new[] { shifted }, new Scalar(255));

            // 직접 픽셀 합산 (Cv2.Mean이 CV_32FC1 ROI + CV_8UC1 mask 조합에서 0 반환 이슈 회피)
            double sum = 0;
            int count = 0;
            unsafe
            {
                float* probPtr = (float*)probMap.Data;
                long probStride = probMap.Step() / sizeof(float);
                byte* maskPtr = (byte*)mask.Data;
                long maskStride = mask.Step();
                for (int y = 0; y < h; y++)
                {
                    float* probRow = probPtr + (yMin + y) * probStride + xMin;
                    byte* maskRow = maskPtr + y * maskStride;
                    for (int x = 0; x < w; x++)
                    {
                        if (maskRow[x] > 0)
                        {
                            sum += probRow[x];
                            count++;
                        }
                    }
                }
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        #endregion

        #region Recognition

        /// <summary>
        /// 텍스트 영역 이미지에서 문자 인식 (CRNN + CTC 디코딩).
        /// </summary>
        private (string text, float confidence) RecognizeText(Mat crop)
        {
            // 세로가 더 긴 경우 90도 회전 (세로 텍스트 처리)
            Mat input = crop;
            bool rotated = false;
            if (crop.Height > crop.Width * 1.5f)
            {
                input = new Mat();
                Cv2.Rotate(crop, input, RotateFlags.Rotate90Counterclockwise);
                rotated = true;
            }

            try
            {
                // 높이 48로 리사이즈 (비율 유지)
                int targetW = (int)Math.Ceiling((float)input.Width * RecImageHeight / input.Height);
                targetW = Math.Max(targetW, 10);

                using var resized = new Mat();
                Cv2.Resize(input, resized, new Size(targetW, RecImageHeight));

                using var rgb = new Mat();
                if (resized.Channels() == 1)
                    Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
                else
                    Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

                // 텐서 생성: (pixel / 255.0 - 0.5) / 0.5
                var tensor = CreateRecognitionTensor(rgb, RecImageHeight, targetW);

                // 추론
                var inputName = _recSession!.InputNames[0];
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputName, tensor)
                };

                using var outputs = _recSession.Run(inputs);
                var outputTensor = outputs.First().AsTensor<float>();

                // CTC 디코딩
                return CtcGreedyDecode(outputTensor);
            }
            finally
            {
                if (rotated)
                    input.Dispose();
            }
        }

        private static DenseTensor<float> CreateRecognitionTensor(Mat rgb, int h, int w)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });

            unsafe
            {
                byte* ptr = (byte*)rgb.Data;
                int stride = (int)rgb.Step();

                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = x * 3;
                        tensor[0, 0, y, x] = (row[idx] / 255.0f - 0.5f) / 0.5f;
                        tensor[0, 1, y, x] = (row[idx + 1] / 255.0f - 0.5f) / 0.5f;
                        tensor[0, 2, y, x] = (row[idx + 2] / 255.0f - 0.5f) / 0.5f;
                    }
                }
            }

            return tensor;
        }

        private (string text, float confidence) CtcGreedyDecode(Tensor<float> output)
        {
            // output shape: [1, T, C] where T=time steps, C=num classes
            int timeSteps = output.Dimensions[1];
            int numClasses = output.Dimensions[2];

            var chars = new List<string>();
            var confidences = new List<float>();
            int lastIdx = 0; // blank
            var allowed = GetWhitelistIndices();

            for (int t = 0; t < timeSteps; t++)
            {
                // argmax (whitelist 적용 시 허용 인덱스만 후보)
                int bestIdx = 0;
                float bestVal = float.MinValue;
                for (int c = 0; c < numClasses; c++)
                {
                    if (allowed != null && !allowed.Contains(c)) continue;
                    if (output[0, t, c] > bestVal)
                    {
                        bestVal = output[0, t, c];
                        bestIdx = c;
                    }
                }

                // PP-OCRv4 ONNX export는 softmax가 모델 내장 → output은 이미 확률.
                // 추가 softmax 적용 시 1/sumExp ≈ 1/numClasses로 값이 뭉개짐.
                // bestVal이 이미 해당 클래스의 확률. 안전을 위해 [0,1] clamp.
                float prob = Math.Clamp(bestVal, 0f, 1f);

                // CTC: 연속 중복 제거 + blank(0) 스킵
                if (bestIdx != 0 && bestIdx != lastIdx)
                {
                    if (bestIdx < _dictionary.Length)
                    {
                        chars.Add(_dictionary[bestIdx]);
                        confidences.Add(prob);
                    }
                }

                lastIdx = bestIdx;
            }

            if (chars.Count == 0)
                return (string.Empty, 0f);

            string text = string.Join("", chars);
            float meanConf = confidences.Average();

            return (text, meanConf);
        }

        #endregion

        #region Crop

        /// <summary>
        /// 검출된 4점 폴리곤 영역을 투시 변환(Perspective Transform)으로 수평 이미지로 추출.
        /// </summary>
        private static Mat CropTextRegion(Mat image, Point2f[] polygon)
        {
            var ordered = OrderPoints(polygon);

            float w1 = Distance(ordered[0], ordered[1]);
            float w2 = Distance(ordered[2], ordered[3]);
            float h1 = Distance(ordered[0], ordered[3]);
            float h2 = Distance(ordered[1], ordered[2]);

            int width = (int)Math.Max(w1, w2);
            int height = (int)Math.Max(h1, h2);

            if (width < 2 || height < 2)
                return new Mat();

            var dst = new Point2f[]
            {
                new(0, 0),
                new(width - 1, 0),
                new(width - 1, height - 1),
                new(0, height - 1)
            };

            using var matrix = Cv2.GetPerspectiveTransform(ordered, dst);
            var cropped = new Mat();
            Cv2.WarpPerspective(image, cropped, matrix, new Size(width, height),
                InterpolationFlags.Linear, BorderTypes.Replicate);

            return cropped;
        }

        /// <summary>
        /// 4점을 [좌상, 우상, 우하, 좌하] 순서로 정렬.
        /// </summary>
        private static Point2f[] OrderPoints(Point2f[] pts)
        {
            // x+y가 가장 작은 점 = 좌상, 가장 큰 점 = 우하
            // y-x가 가장 작은 점 = 우상, 가장 큰 점 = 좌하
            var sorted = pts.OrderBy(p => p.X + p.Y).ToArray();
            var tl = sorted[0];
            var br = sorted[3];

            var mid = new[] { sorted[1], sorted[2] };
            var tr = mid.OrderBy(p => p.Y - p.X).First();
            var bl = mid.OrderByDescending(p => p.Y - p.X).First();

            return new[] { tl, tr, br, bl };
        }

        private static float Distance(Point2f a, Point2f b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        #endregion

        #region Model Management

        private static string GetDefaultModelDir()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "ppocr");
        }

        private static void EnsureModelFiles(string modelDir, string detPath, string recPath, string dictPath)
        {
            var missing = new List<(string path, string file)>();
            if (!File.Exists(detPath)) missing.Add((detPath, DetModelFile));
            if (!File.Exists(recPath)) missing.Add((recPath, RecModelFile));
            if (!File.Exists(dictPath)) missing.Add((dictPath, DictFile));

            if (missing.Count == 0) return;

            // 자동 다운로드 시도
            try
            {
                DownloadModelsSync(modelDir, missing);
            }
            catch (Exception ex)
            {
                string fileList = string.Join("\n", missing.Select(m => $"  - {m.file}"));
                throw new FileNotFoundException(
                    $"PP-OCRv4 ONNX 모델 파일을 찾을 수 없습니다.\n" +
                    $"경로: {modelDir}\n" +
                    $"필요한 파일:\n{fileList}\n\n" +
                    $"자동 다운로드 실패: {ex.Message}\n\n" +
                    $"수동 다운로드:\n" +
                    $"  https://github.com/RapidAI/PaddleOCRModelConvert");
            }
        }

        private static void DownloadModelsSync(string modelDir, List<(string path, string file)> missing)
        {
            Directory.CreateDirectory(modelDir);

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(5);

            foreach (var (path, file) in missing)
            {
                if (!ModelUrls.TryGetValue(file, out string? url))
                    throw new InvalidOperationException($"다운로드 URL을 알 수 없습니다: {file}");

                System.Diagnostics.Debug.WriteLine($"[PaddleOcrOnnx] Downloading {file}...");

                var response = http.GetAsync(url).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                using var fs = File.Create(path);
                response.Content.CopyToAsync(fs).GetAwaiter().GetResult();

                System.Diagnostics.Debug.WriteLine($"[PaddleOcrOnnx] Downloaded {file} ({new FileInfo(path).Length} bytes)");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _detSession?.Dispose();
            _recSession?.Dispose();
            _detSession = null;
            _recSession = null;
        }
    }

    /// <summary>
    /// ONNX OCR 인식 결과.
    /// </summary>
    public record OnnxOcrResult(string Text, float Confidence, Point2f[] Polygon);
}
