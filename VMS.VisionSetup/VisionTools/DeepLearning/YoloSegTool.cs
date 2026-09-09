using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// YOLOv8-seg 인스턴스 분할 도구 (Cognex ViDi Blue Locate / Red Supervised 인스턴스 모드 대응).
    /// ONNX 출력 2개 가정:
    ///   • output0: [1, 4+nc+32, N] (또는 [1, N, 4+nc+32]) — detection + 32 mask 계수
    ///   • output1: [1, 32, mh, mw] — prototype masks
    /// 후처리: NMS → coef × prototypes → sigmoid → resize → threshold → 인스턴스별 마스크.
    /// </summary>
    public class YoloSegTool : VisionToolBase
    {
        private string _modelPath = string.Empty;
        public string ModelPath
        {
            get => _modelPath;
            set
            {
                if (!SetProperty(ref _modelPath, value ?? string.Empty)) return;
                // 모델 경로가 바뀌면 들고 있던 엔진을 버린다. 안 버리면 새 모델을 골라도 옛 모델이
                // 계속 추론한다 — 화면에는 새 경로가 보이므로 아무도 눈치채지 못한다.
                // Dispose 하지 않는 것은 캐시가 생명주기를 쥐고 있어서다 (같은 경로를 다른 도구가 쓸 수 있다).
                _engine = null;
            }
        }

        private int _inputSize = 640;
        public int InputSize
        {
            get => _inputSize;
            set => SetProperty(ref _inputSize, Math.Clamp(value, 64, 2048));
        }

        private float _confidenceThreshold = 0.25f;
        public float ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => SetProperty(ref _confidenceThreshold, Math.Clamp(value, 0.01f, 1.0f));
        }

        private float _iouThreshold = 0.45f;
        public float IouThreshold
        {
            get => _iouThreshold;
            set => SetProperty(ref _iouThreshold, Math.Clamp(value, 0.1f, 1.0f));
        }

        private float _maskThreshold = 0.5f;
        public float MaskThreshold
        {
            get => _maskThreshold;
            set => SetProperty(ref _maskThreshold, Math.Clamp(value, 0.1f, 0.99f));
        }

        private bool _showOverlay = true;
        public bool ShowOverlay { get => _showOverlay; set => SetProperty(ref _showOverlay, value); }

        private double _overlayOpacity = 0.4;
        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set => SetProperty(ref _overlayOpacity, Math.Clamp(value, 0.0, 1.0));
        }

        private bool _drawBoxes = true;
        public bool DrawBoxes { get => _drawBoxes; set => SetProperty(ref _drawBoxes, value); }

        private bool _outputMaskImage;
        /// <summary>
        /// true면 OutputImage를 인스턴스 합집합 이진 마스크(CV_8UC1, 255=인스턴스)로 출력 —
        /// PointCloudMaskCropTool 등 마스크 소비 도구와 Image 연결용.
        /// 화면 오버레이(OverlayImage)는 그대로 유지.
        /// </summary>
        public bool OutputMaskImage { get => _outputMaskImage; set => SetProperty(ref _outputMaskImage, value); }

        private string[] _classNames = Array.Empty<string>();
        public string[] ClassNames
        {
            get => _classNames;
            private set => SetProperty(ref _classNames, value);
        }

        private YoloSegOnnxEngine? _engine;

        public YoloSegTool()
        {
            Name = "YOLOv8-seg";
            ToolType = "YoloSegTool";
        }

        private void EnsureEngine()
        {
            _engine ??= OnnxEngineCache.GetYoloSeg(ModelPath, InputSize);
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrEmpty(ModelPath) || !System.IO.File.Exists(ModelPath))
                {
                    result.Success = false;
                    result.Message = "Model path not set or file missing.";
                    return result;
                }

                using var roi = GetROIImage(inputImage);
                EnsureEngine();

                var instances = _engine!.Segment(roi, InputSize, ConfidenceThreshold, IouThreshold, MaskThreshold);
                if (instances.Count > 0 && _engine.LastClassNames?.Length > 0)
                    ClassNames = _engine.LastClassNames;

                result.Data["InstanceCount"] = instances.Count;
                result.Data["ExecutionProvider"] = _engine.ActiveProvider;

                for (int i = 0; i < instances.Count; i++)
                {
                    var ins = instances[i];
                    result.Data[$"Inst{i}_Class"] = ins.ClassId;
                    result.Data[$"Inst{i}_ClassName"] = ins.ClassName ?? $"Class{ins.ClassId}";
                    result.Data[$"Inst{i}_Score"] = ins.Score;
                    result.Data[$"Inst{i}_X"] = ins.Box.X;
                    result.Data[$"Inst{i}_Y"] = ins.Box.Y;
                    result.Data[$"Inst{i}_Width"] = ins.Box.Width;
                    result.Data[$"Inst{i}_Height"] = ins.Box.Height;
                    result.Data[$"Inst{i}_MaskPixels"] = ins.MaskPixelCount;
                }

                Mat overlay = ShowOverlay
                    ? BuildOverlay(inputImage, instances)
                    : GetColorOverlayBase(inputImage);

                result.OutputImage = OutputMaskImage
                    ? BuildUnionMask(inputImage, instances)
                    : overlay.Clone();
                result.OverlayImage = overlay;
                // 0건 검출 = 실패 — 자매 툴 DetectionTool(successBase = detections.Count > 0)과
                // 판정 기준 통일 (2026-08-18 전수 검토). ResultTool 집계에서 "아무것도 못 찾음"이
                // OK 로 전파되지 않는다.
                result.Success = instances.Count > 0;
                result.Message = $"Detected {instances.Count} instance(s) (ConfThr={ConfidenceThreshold:F2}, IoU={IouThreshold:F2})";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"YOLOv8-seg failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        private Mat BuildOverlay(Mat inputImage, List<YoloSegInstance> instances)
        {
            var overlay = GetColorOverlayBase(inputImage);
            var palette = BuildPalette(Math.Max(instances.Count, 8));

            // 비사각형 ROI 좌표 보정 — UseROI 활성 시 ROI 좌상단 오프셋
            var adjROI = GetAdjustedROI(inputImage);
            int dx = UseROI ? adjROI.X : 0;
            int dy = UseROI ? adjROI.Y : 0;

            for (int i = 0; i < instances.Count; i++)
            {
                var ins = instances[i];
                var c = palette[i % palette.Length];
                var bgr = new Scalar(c.B, c.G, c.R);

                // 마스크는 ROI 좌표계 기준 → 원본 위치에 alpha blend
                if (ins.Mask != null && !ins.Mask.Empty())
                {
                    using var colorLayer = new Mat(ins.Mask.Size(), MatType.CV_8UC3, bgr);
                    using var slot = new Mat(overlay,
                        new Rect(dx + ins.Box.X, dy + ins.Box.Y,
                            Math.Min(ins.Mask.Width, overlay.Width - dx - ins.Box.X),
                            Math.Min(ins.Mask.Height, overlay.Height - dy - ins.Box.Y)));
                    if (slot.Width > 0 && slot.Height > 0)
                    {
                        using var blended = new Mat();
                        Cv2.AddWeighted(slot, 1.0 - OverlayOpacity,
                            new Mat(colorLayer, new Rect(0, 0, slot.Width, slot.Height)),
                            OverlayOpacity, 0, blended);
                        using var maskCrop = new Mat(ins.Mask, new Rect(0, 0, slot.Width, slot.Height));
                        blended.CopyTo(slot, maskCrop);
                    }
                }

                if (DrawBoxes)
                {
                    Cv2.Rectangle(overlay,
                        new Point(dx + ins.Box.X, dy + ins.Box.Y),
                        new Point(dx + ins.Box.X + ins.Box.Width, dy + ins.Box.Y + ins.Box.Height),
                        bgr, 2);
                    string label = $"{ins.ClassName ?? $"C{ins.ClassId}"} {ins.Score:F2}";
                    Cv2.PutText(overlay, label,
                        new Point(dx + ins.Box.X + 4, dy + ins.Box.Y + 16),
                        HersheyFonts.HersheySimplex, 0.5, bgr, 1);
                }
            }
            return overlay;
        }

        /// <summary>모든 인스턴스 마스크의 합집합을 원본 크기 CV_8UC1(0/255)로 생성.</summary>
        private Mat BuildUnionMask(Mat inputImage, List<YoloSegInstance> instances)
        {
            var maskOut = new Mat(inputImage.Size(), MatType.CV_8UC1, Scalar.Black);
            var adjROI = GetAdjustedROI(inputImage);
            int dx = UseROI ? adjROI.X : 0;
            int dy = UseROI ? adjROI.Y : 0;

            foreach (var ins in instances)
            {
                if (ins.Mask == null || ins.Mask.Empty()) continue;
                int x = dx + ins.Box.X, y = dy + ins.Box.Y;
                if (x < 0 || y < 0) continue;
                int w = Math.Min(ins.Mask.Width, maskOut.Width - x);
                int h = Math.Min(ins.Mask.Height, maskOut.Height - y);
                if (w <= 0 || h <= 0) continue;

                using var slot = new Mat(maskOut, new Rect(x, y, w, h));
                using var maskCrop = new Mat(ins.Mask, new Rect(0, 0, w, h));
                slot.SetTo(255, maskCrop);
            }
            return maskOut;
        }

        private static (byte R, byte G, byte B)[] BuildPalette(int n)
        {
            var palette = new (byte, byte, byte)[Math.Max(n, 1)];
            for (int i = 0; i < palette.Length; i++)
            {
                double hue = (i * 47.0) % 180.0;
                using var hsv = new Mat(1, 1, MatType.CV_8UC3, new Scalar(hue, 220, 240));
                using var bgr = new Mat();
                Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
                var p = bgr.Get<Vec3b>(0, 0);
                palette[i] = (p.Item2, p.Item1, p.Item0);
            }
            return palette;
        }

        public override List<string> GetAvailableResultKeys()
        {
            var keys = new List<string>
            {
                "Success", "InstanceCount", "ExecutionProvider"
            };
            int slots = 10;
            for (int i = 0; i < slots; i++)
            {
                keys.Add($"Inst{i}_Class");
                keys.Add($"Inst{i}_ClassName");
                keys.Add($"Inst{i}_Score");
                keys.Add($"Inst{i}_X");
                keys.Add($"Inst{i}_Y");
                keys.Add($"Inst{i}_Width");
                keys.Add($"Inst{i}_Height");
                keys.Add($"Inst{i}_MaskPixels");
            }
            return keys;
        }

        public override VisionToolBase Clone()
        {
            var clone = new YoloSegTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
                ModelPath = this.ModelPath,
                InputSize = this.InputSize,
                ConfidenceThreshold = this.ConfidenceThreshold,
                IouThreshold = this.IouThreshold,
                MaskThreshold = this.MaskThreshold,
                ShowOverlay = this.ShowOverlay,
                OverlayOpacity = this.OverlayOpacity,
                DrawBoxes = this.DrawBoxes
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    /// <summary>단일 인스턴스 분할 결과 — 박스 + 클래스 + 점수 + 마스크 (8UC1 binary).</summary>
    public class YoloSegInstance
    {
        public Rect Box { get; set; }   // 원본 이미지 좌표 (정수)
        public int ClassId { get; set; }
        public string? ClassName { get; set; }
        public float Score { get; set; }
        public Mat? Mask { get; set; }  // Box 크기 (Box.Width × Box.Height), CV_8UC1
        public long MaskPixelCount { get; set; }
    }

    /// <summary>YOLOv8-seg ONNX 추론 엔진.</summary>
    public class YoloSegOnnxEngine : OnnxModelBase
    {
        public string[]? LastClassNames { get; private set; }

        public YoloSegOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
            LastClassNames = ReadClassNamesFromMetadata();
        }

        /// <summary>
        /// YOLOv8-seg 추론. output0/output1 모두 처리해 인스턴스별 마스크 생성.
        /// </summary>
        public List<YoloSegInstance> Segment(
            Mat image, int inputSize, float confThr, float iouThr, float maskThr)
        {
            var results = new List<YoloSegInstance>();
            if (_session == null) return results;

            // Letterbox 전처리
            float scale = Math.Min((float)inputSize / image.Width, (float)inputSize / image.Height);
            int newW = (int)(image.Width * scale);
            int newH = (int)(image.Height * scale);
            int padX = (inputSize - newW) / 2;
            int padY = (inputSize - newH) / 2;

            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(newW, newH));
            using var letterbox = new Mat(inputSize, inputSize, image.Type(), new Scalar(114, 114, 114));
            resized.CopyTo(letterbox[new Rect(padX, padY, newW, newH)]);

            var tensor = PreprocessImageSimple(letterbox, inputSize, inputSize);
            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);
            if (outputs.Count < 2) return results;

            var output0 = (DenseTensor<float>)outputs.ElementAt(0).AsTensor<float>();
            var output1 = (DenseTensor<float>)outputs.ElementAt(1).AsTensor<float>();

            // output0: [1, C, N] 또는 [1, N, C], C = 4 + nc + 32
            // output1: [1, 32, mh, mw]
            var d0 = output0.Dimensions;
            var d1 = output1.Dimensions;
            if (d0.Length != 3 || d1.Length != 4) return results;
            if (d1[1] != 32) return results; // mask coef = 32 (YOLOv8 standard)

            int numChannels, numDetections;
            bool transposed;
            if (d0[1] < d0[2]) { numChannels = d0[1]; numDetections = d0[2]; transposed = true; }
            else { numDetections = d0[1]; numChannels = d0[2]; transposed = false; }

            int maskCoefCount = 32;
            int numClasses = numChannels - 4 - maskCoefCount;
            if (numClasses <= 0) return results;

            int mh = d1[2], mw = d1[3];
            int protoSize = mh * mw;

            var buf = output0.Buffer.Span;
            var protoBuf = output1.Buffer.Span;

            // 1) detection 파싱 + per-class max score
            var candidates = new List<(int idx, float score, int classId, float cx, float cy, float w, float h)>();
            for (int i = 0; i < numDetections; i++)
            {
                float cx, cy, w, h;
                int classBase, classStride;
                if (transposed)
                {
                    cx = buf[0 * numDetections + i];
                    cy = buf[1 * numDetections + i];
                    w  = buf[2 * numDetections + i];
                    h  = buf[3 * numDetections + i];
                    classBase = 4 * numDetections + i;
                    classStride = numDetections;
                }
                else
                {
                    int rb = i * numChannels;
                    cx = buf[rb];
                    cy = buf[rb + 1];
                    w  = buf[rb + 2];
                    h  = buf[rb + 3];
                    classBase = rb + 4;
                    classStride = 1;
                }

                float bestScore = 0;
                int bestClass = -1;
                for (int c = 0; c < numClasses; c++)
                {
                    float s = buf[classBase + c * classStride];
                    if (s > bestScore) { bestScore = s; bestClass = c; }
                }
                if (bestScore < confThr || bestClass < 0) continue;
                candidates.Add((i, bestScore, bestClass, cx, cy, w, h));
            }

            if (candidates.Count == 0) return results;

            // 2) NMS (per-class)
            var kept = NonMaxSuppression(candidates, iouThr);

            // 3) 각 인스턴스 마스크 후처리
            foreach (var det in kept)
            {
                // letterbox 좌표 → 원본 좌표
                float x1 = det.cx - det.w / 2f;
                float y1 = det.cy - det.h / 2f;
                float x2 = det.cx + det.w / 2f;
                float y2 = det.cy + det.h / 2f;
                int orgX1 = Math.Clamp((int)((x1 - padX) / scale), 0, image.Width - 1);
                int orgY1 = Math.Clamp((int)((y1 - padY) / scale), 0, image.Height - 1);
                int orgX2 = Math.Clamp((int)((x2 - padX) / scale), 0, image.Width - 1);
                int orgY2 = Math.Clamp((int)((y2 - padY) / scale), 0, image.Height - 1);
                if (orgX2 <= orgX1 || orgY2 <= orgY1) continue;

                // 마스크 계수 추출
                var coefs = new float[maskCoefCount];
                int coefBase, coefStride;
                if (transposed) { coefBase = (4 + numClasses) * numDetections + det.idx; coefStride = numDetections; }
                else { coefBase = det.idx * numChannels + 4 + numClasses; coefStride = 1; }
                for (int k = 0; k < maskCoefCount; k++)
                    coefs[k] = buf[coefBase + k * coefStride];

                // 마스크 = coef ⋅ prototype, prototype 채널 = 32, plane = mh × mw
                var maskLin = new float[protoSize];
                for (int k = 0; k < maskCoefCount; k++)
                {
                    float coef = coefs[k];
                    int protoBaseIdx = k * protoSize;
                    for (int p = 0; p < protoSize; p++)
                        maskLin[p] += coef * protoBuf[protoBaseIdx + p];
                }
                // sigmoid
                for (int p = 0; p < protoSize; p++)
                    maskLin[p] = 1.0f / (1.0f + (float)Math.Exp(-maskLin[p]));

                // mh×mw → letterbox 크기로 리사이즈 후 letterbox에서 크롭 후 원본 크기로
                using var maskMat = new Mat(mh, mw, MatType.CV_32FC1);
                Marshal.Copy(maskLin, 0, maskMat.Data, maskLin.Length);

                using var maskUp = new Mat();
                Cv2.Resize(maskMat, maskUp, new Size(inputSize, inputSize), 0, 0, InterpolationFlags.Linear);

                // letterbox 좌표계의 박스로 크롭
                int lx1 = Math.Clamp((int)x1, 0, inputSize);
                int ly1 = Math.Clamp((int)y1, 0, inputSize);
                int lx2 = Math.Clamp((int)x2, 0, inputSize);
                int ly2 = Math.Clamp((int)y2, 0, inputSize);
                if (lx2 <= lx1 || ly2 <= ly1) continue;

                using var maskCrop = new Mat(maskUp, new Rect(lx1, ly1, lx2 - lx1, ly2 - ly1));

                int boxW = orgX2 - orgX1;
                int boxH = orgY2 - orgY1;
                using var maskOrg = new Mat();
                Cv2.Resize(maskCrop, maskOrg, new Size(boxW, boxH), 0, 0, InterpolationFlags.Linear);

                // threshold → CV_8UC1
                var maskBin = new Mat();
                Cv2.Threshold(maskOrg, maskBin, maskThr, 255, ThresholdTypes.Binary);
                maskBin.ConvertTo(maskBin, MatType.CV_8UC1);

                long maskCount = Cv2.CountNonZero(maskBin);
                string? className = LastClassNames != null && det.classId < LastClassNames.Length
                    ? LastClassNames[det.classId] : null;

                results.Add(new YoloSegInstance
                {
                    Box = new Rect(orgX1, orgY1, boxW, boxH),
                    ClassId = det.classId,
                    ClassName = className,
                    Score = det.score,
                    Mask = maskBin,
                    MaskPixelCount = maskCount
                });
            }

            return results;
        }

        private static List<(int idx, float score, int classId, float cx, float cy, float w, float h)>
            NonMaxSuppression(List<(int idx, float score, int classId, float cx, float cy, float w, float h)> cand, float iouThr)
        {
            cand.Sort((a, b) => b.score.CompareTo(a.score));
            var kept = new List<(int, float, int, float, float, float, float)>();
            var suppressed = new bool[cand.Count];
            for (int i = 0; i < cand.Count; i++)
            {
                if (suppressed[i]) continue;
                kept.Add(cand[i]);
                var ai = cand[i];
                for (int j = i + 1; j < cand.Count; j++)
                {
                    if (suppressed[j]) continue;
                    var aj = cand[j];
                    if (aj.classId != ai.classId) continue;
                    float iou = ComputeIoU(ai.cx, ai.cy, ai.w, ai.h, aj.cx, aj.cy, aj.w, aj.h);
                    if (iou > iouThr) suppressed[j] = true;
                }
            }
            return kept;
        }

        private static float ComputeIoU(float cx1, float cy1, float w1, float h1,
                                        float cx2, float cy2, float w2, float h2)
        {
            float x11 = cx1 - w1 / 2, y11 = cy1 - h1 / 2, x12 = cx1 + w1 / 2, y12 = cy1 + h1 / 2;
            float x21 = cx2 - w2 / 2, y21 = cy2 - h2 / 2, x22 = cx2 + w2 / 2, y22 = cy2 + h2 / 2;
            float ix1 = Math.Max(x11, x21), iy1 = Math.Max(y11, y21);
            float ix2 = Math.Min(x12, x22), iy2 = Math.Min(y12, y22);
            float iw = Math.Max(0, ix2 - ix1), ih = Math.Max(0, iy2 - iy1);
            float inter = iw * ih;
            float u = w1 * h1 + w2 * h2 - inter;
            return u > 0 ? inter / u : 0;
        }
    }
}
