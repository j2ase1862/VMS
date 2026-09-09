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
    /// RF-DETR 인스턴스 분할 도구 (Apache-2.0 백본).
    ///
    /// <para>
    /// <see cref="YoloSegTool"/> 와 하는 일은 같고 규약이 다릅니다. YOLO 계열(AGPL-3.0)을 피해야 하는
    /// 상용 배포에서 쓰라고 둔 것으로, 검출에서 D-FINE 을 둔 것과 같은 자리입니다.
    /// 학습은 <c>scripts/train_rfdetr_seg.py</c> 가 합니다.
    /// </para>
    ///
    /// <para><b>ONNX 규약</b> (roboflow/rf-detr 의 export 가 정하는 이름 그대로):</para>
    /// <list type="bullet">
    /// <item>input  <c>[N,3,H,W]</c> float32 RGB, ImageNet 정규화</item>
    /// <item>dets   <c>[N,Q,4]</c> 정규화 cxcywh — 0~1</item>
    /// <item>labels <c>[N,Q,C]</c> 클래스 로짓 — <b>시그모이드</b> (소프트맥스 아님).
    ///   <c>C = 클래스 수 + 1</c> 이고 <b>마지막 열이 배경</b>이다 (rf-detr 의 lwdetr.py: "background slot
    ///   (index detection_num_classes-1)"). 그 열을 빼지 않으면 배경이 최고 점수인 질의가 물체로 나온다.</item>
    /// <item>masks  <c>[N,Q,mh,mw]</c> 마스크 로짓 — 이중선형으로 키운 뒤 0 에서 자른다</item>
    /// </list>
    ///
    /// <para><b>YOLO-seg 와 다른 점 셋.</b>
    /// (1) 레터박스가 없다 — 늘려 맞추는 리사이즈라 좌표를 되돌릴 때 여백을 빼지 않는다.
    /// (2) NMS 가 없다 — DETR 계열은 집합 예측이라 중복이 잘 생기지 않는다. (질의 × 클래스) 쌍을
    ///     점수로 줄 세워 상위 몇 개만 남긴다. 질의마다 최고 클래스 하나만 고르면, 한 질의가 두 클래스에서
    ///     문턱을 넘을 때 하나가 조용히 사라진다 (rf-detr 이 같은 이유로 argmax 를 버렸다).
    /// (3) 마스크가 질의마다 온전한 한 장이다 — 프로토타입 32장에 계수를 곱하는 조립이 없다.
    /// </para>
    /// </summary>
    public class RfdetrSegTool : VisionToolBase
    {
        private string _modelPath = string.Empty;
        public string ModelPath
        {
            get => _modelPath;
            set => SetProperty(ref _modelPath, value ?? string.Empty);
        }

        private int _inputSize = 560;
        /// <summary>
        /// 모델이 받는 변의 길이. RF-DETR 은 블록 크기의 배수만 받으므로 학습 때 쓴 값을 그대로 넣습니다
        /// (ONNX metadata 의 imgsz).
        /// </summary>
        public int InputSize
        {
            get => _inputSize;
            set => SetProperty(ref _inputSize, Math.Clamp(value, 64, 2048));
        }

        private float _confidenceThreshold = 0.5f;
        public float ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => SetProperty(ref _confidenceThreshold, Math.Clamp(value, 0.01f, 1.0f));
        }

        private int _maxInstances = 100;
        /// <summary>
        /// 점수 순으로 남길 개수. 질의 수가 수천인 모델도 있어 상한이 없으면 마스크를 그 수만큼 키우게 됩니다.
        /// </summary>
        public int MaxInstances
        {
            get => _maxInstances;
            set => SetProperty(ref _maxInstances, Math.Clamp(value, 1, 1000));
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
        /// true 면 OutputImage 를 인스턴스 합집합 이진 마스크(CV_8UC1, 255=인스턴스)로 출력 —
        /// PointCloudMaskCropTool 등 마스크 소비 도구와 Image 연결용. <see cref="YoloSegTool"/> 와 같습니다.
        /// </summary>
        public bool OutputMaskImage { get => _outputMaskImage; set => SetProperty(ref _outputMaskImage, value); }

        private string[] _classNames = Array.Empty<string>();
        public string[] ClassNames
        {
            get => _classNames;
            private set => SetProperty(ref _classNames, value);
        }

        private RfdetrSegOnnxEngine? _engine;

        public RfdetrSegTool()
        {
            Name = "RF-DETR-seg";
            ToolType = "RfdetrSegTool";
        }

        private void EnsureEngine()
        {
            _engine ??= OnnxEngineCache.GetRfdetrSeg(ModelPath, InputSize);
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();
            try
            {
                if (inputImage == null || inputImage.Empty())
                {
                    result.Success = false;
                    result.Message = "입력 이미지가 없습니다.";
                    return result;
                }
                if (string.IsNullOrWhiteSpace(ModelPath))
                {
                    result.Success = false;
                    result.Message = "모델 경로가 지정되지 않았습니다.";
                    return result;
                }

                using var roi = GetROIImage(inputImage);
                EnsureEngine();

                var instances = _engine!.Segment(roi, InputSize, ConfidenceThreshold, MaxInstances);
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

                // 0건 검출 = 실패 — 자매 툴 YoloSegTool·DetectionTool 과 판정 기준을 맞춘다.
                // "아무것도 못 찾음" 이 ResultTool 집계에서 OK 로 전파되면 안 된다.
                result.Success = instances.Count > 0;
                result.Message = $"인스턴스 {instances.Count}개 (ConfThr={ConfidenceThreshold:F2}, 상한={MaxInstances})";

                foreach (var ins in instances) ins.Mask?.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"RF-DETR-seg 실패: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        private Mat BuildOverlay(Mat inputImage, List<RfdetrSegInstance> instances)
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
        private Mat BuildUnionMask(Mat inputImage, List<RfdetrSegInstance> instances)
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
            var keys = new List<string> { "Success", "InstanceCount", "ExecutionProvider" };
            for (int i = 0; i < 10; i++)
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
            var clone = new RfdetrSegTool
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
                MaxInstances = this.MaxInstances,
                ShowOverlay = this.ShowOverlay,
                OverlayOpacity = this.OverlayOpacity,
                DrawBoxes = this.DrawBoxes,
                OutputMaskImage = this.OutputMaskImage
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    /// <summary>인스턴스 하나. <see cref="Mask"/> 는 <see cref="Box"/> 크기의 CV_8UC1 (255=인스턴스).</summary>
    public class RfdetrSegInstance
    {
        public Rect Box { get; set; }
        public int ClassId { get; set; }
        public string? ClassName { get; set; }
        public float Score { get; set; }
        public Mat? Mask { get; set; }
        public long MaskPixelCount { get; set; }
    }

    /// <summary>
    /// RF-DETR 세그멘테이션 ONNX 엔진.
    ///
    /// <para>
    /// 전처리가 <see cref="YoloSegTool"/> 와 다릅니다 — 레터박스 없이 늘려 맞추고 ImageNet 통계로 정규화합니다.
    /// 학습 때와 다른 전처리를 쓰면 점수가 조용히 낮아지기만 하고 아무 오류도 나지 않으므로,
    /// 이 두 가지는 규약으로 봅니다.
    /// </para>
    /// </summary>
    public class RfdetrSegOnnxEngine : OnnxModelBase
    {
        public string[]? LastClassNames { get; private set; }

        /// <summary>
        /// 배경 열의 자리. 기본은 마지막 열이다 — rf-detr 이 <c>num_classes + 1</c> 로 머리를 만들고
        /// 마지막 칸을 배경으로 쓴다. 학습 스크립트가 ONNX 메타데이터에 <c>background_class_id</c> 를
        /// 새겨 두면 그 값을 쓴다 (체크포인트마다 다를 수 있어 라이브러리도 인자로 받는다).
        /// 음수면 배경 열이 없다는 뜻이다.
        /// </summary>
        public int BackgroundClassId { get; private set; } = int.MinValue;

        public RfdetrSegOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
            LastClassNames = ReadClassNamesFromMetadata();
            BackgroundClassId = ReadBackgroundClassId();
        }

        private int ReadBackgroundClassId()
        {
            try
            {
                var meta = _session?.ModelMetadata.CustomMetadataMap;
                if (meta != null && meta.TryGetValue("background_class_id", out var raw)
                    && int.TryParse(raw?.Trim(), out var parsed))
                    return parsed;
            }
            catch (Exception ex) { Debug.WriteLine($"[RfdetrSeg] background_class_id 읽기 실패: {ex.Message}"); }
            return int.MinValue;   // 모르면 아래에서 마지막 열로 본다
        }

        public List<RfdetrSegInstance> Segment(Mat image, int inputSize, float confThr, int maxInstances)
        {
            var results = new List<RfdetrSegInstance>();
            if (_session == null) return results;

            // 늘려 맞추는 리사이즈 + ImageNet 정규화 (레터박스 없음). 학습이 그렇게 했으므로 여기서도 그렇게 한다.
            // 베이스의 PreprocessImageNet 이 정확히 이 일을 하고, 픽셀 인덱서 대신 버퍼에 직접 써서 빠르다.
            var tensor = PreprocessImageNet(image, inputSize, inputSize);
            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);
            var byName = outputs.ToDictionary(o => o.Name, o => o);
            if (!byName.TryGetValue("dets", out var detsOut)
                || !byName.TryGetValue("labels", out var labelsOut)
                || !byName.TryGetValue("masks", out var masksOut))
            {
                // 이름이 없으면 규약이 아니다. 자리로 짐작하면 엉뚱한 텐서를 마스크로 읽는다.
                return results;
            }

            var dets = (DenseTensor<float>)detsOut.AsTensor<float>();
            var labels = (DenseTensor<float>)labelsOut.AsTensor<float>();
            var masks = (DenseTensor<float>)masksOut.AsTensor<float>();

            var dd = dets.Dimensions;
            var dl = labels.Dimensions;
            var dm = masks.Dimensions;
            if (dd.Length != 3 || dl.Length != 3 || dm.Length != 4) return results;
            if (dd[2] != 4) return results;

            int queries = dd[1];
            int numClasses = dl[2];
            int mh = dm[2], mw = dm[3];
            int maskPlane = mh * mw;
            if (queries <= 0 || numClasses <= 0 || maskPlane <= 0) return results;
            if (dl[1] != queries || dm[1] != queries) return results;

            var detBuf = dets.Buffer.Span;
            var labelBuf = labels.Buffer.Span;
            var maskBuf = masks.Buffer.Span;

            // 배경 열은 빼고 본다. 안 빼면 배경이 최고 점수인 질의가 물체로 나온다.
            int background = BackgroundClassId != int.MinValue ? BackgroundClassId : numClasses - 1;

            // (질의 × 클래스) 쌍을 점수로 줄 세운다. 질의마다 최고 하나만 고르면
            // 한 질의가 두 클래스에서 문턱을 넘을 때 하나가 조용히 사라진다.
            var candidates = new List<(int query, int classId, float score)>();
            for (int q = 0; q < queries; q++)
            {
                int labelBase = q * numClasses;
                for (int c = 0; c < numClasses; c++)
                {
                    if (c == background) continue;
                    float score = Sigmoid(labelBuf[labelBase + c]);
                    if (score >= confThr) candidates.Add((q, c, score));
                }
            }
            if (candidates.Count == 0) return results;

            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            if (candidates.Count > maxInstances) candidates.RemoveRange(maxInstances, candidates.Count - maxInstances);

            foreach (var (query, classId, score) in candidates)
            {
                // dets 는 0~1 정규화 cxcywh. 늘려 맞춘 리사이즈라 원본 크기를 바로 곱하면 된다.
                int detBase = query * 4;
                float cx = detBuf[detBase] * image.Width;
                float cy = detBuf[detBase + 1] * image.Height;
                float w = detBuf[detBase + 2] * image.Width;
                float h = detBuf[detBase + 3] * image.Height;

                int x1 = Math.Clamp((int)Math.Round(cx - w / 2f), 0, image.Width - 1);
                int y1 = Math.Clamp((int)Math.Round(cy - h / 2f), 0, image.Height - 1);
                int x2 = Math.Clamp((int)Math.Round(cx + w / 2f), 0, image.Width);
                int y2 = Math.Clamp((int)Math.Round(cy + h / 2f), 0, image.Height);
                if (x2 <= x1 || y2 <= y1) continue;
                var box = new Rect(x1, y1, x2 - x1, y2 - y1);

                // 마스크는 질의마다 온전한 한 장이다 (압축 해상도). 원본 크기로 키운 뒤 박스만 잘라 쓴다.
                var plane = new float[maskPlane];
                maskBuf.Slice(query * maskPlane, maskPlane).CopyTo(plane);

                using var maskMat = new Mat(mh, mw, MatType.CV_32FC1);
                Marshal.Copy(plane, 0, maskMat.Data, plane.Length);

                using var maskFull = new Mat();
                Cv2.Resize(maskMat, maskFull, new Size(image.Width, image.Height), 0, 0, InterpolationFlags.Linear);

                using var maskRoi = new Mat(maskFull, box);
                // 로짓이라 0 에서 자른다 (시그모이드 0.5 와 같은 자리). 시그모이드를 먼저 걸 이유가 없다.
                var maskBin = new Mat();
                Cv2.Threshold(maskRoi, maskBin, 0, 255, ThresholdTypes.Binary);
                maskBin.ConvertTo(maskBin, MatType.CV_8UC1);

                results.Add(new RfdetrSegInstance
                {
                    Box = box,
                    ClassId = classId,
                    ClassName = LastClassNames != null && classId < LastClassNames.Length ? LastClassNames[classId] : null,
                    Score = score,
                    Mask = maskBin,
                    MaskPixelCount = Cv2.CountNonZero(maskBin),
                });
            }

            return results;
        }

        private static float Sigmoid(float x) => 1f / (1f + (float)Math.Exp(-x));
    }
}
