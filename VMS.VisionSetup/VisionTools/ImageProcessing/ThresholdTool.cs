using VMS.VisionSetup.Attributes;
using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// 이진화(Threshold) 도구
    /// (Cognex VisionPro CogAcqFifo의 Threshold 또는 CogImageSharpnessTool 대체)
    /// </summary>
    public class ThresholdTool : VisionToolBase
    {
        private double _thresholdValue = 128;
        [TunableParam(
            Description = "이진화 임계값. 픽셀이 이 값보다 크면 흰색, 작으면 검정으로 변환. 이미지 평균 밝기 근처가 합리적.",
            Tier = TuningTier.ImageDependent,
            Min = 0, Max = 255, DefaultHint = "128")]
        public double ThresholdValue
        {
            get => _thresholdValue;
            set => SetProperty(ref _thresholdValue, Math.Clamp(value, 0, 255));
        }

        private double _maxValue = 255;
        public double MaxValue
        {
            get => _maxValue;
            set => SetProperty(ref _maxValue, Math.Clamp(value, 0, 255));
        }

        private ThresholdType _thresholdType = ThresholdType.Binary;
        [TunableParam(
            Description = "이진화 방향. Binary: 임계값보다 크면 흰색. BinaryInv: 임계값보다 크면 검정(어두운 객체 검출 시).",
            Tier = TuningTier.Semantic, DefaultHint = "Binary")]
        public ThresholdType ThresholdType
        {
            get => _thresholdType;
            set => SetProperty(ref _thresholdType, value);
        }

        private bool _useOtsu = false;
        [TunableParam(
            Description = "true면 Otsu 알고리즘으로 임계값 자동 결정. 히스토그램이 bimodal(이중 봉우리)일 때 권장.",
            Tier = TuningTier.Semantic, DefaultHint = "false")]
        public bool UseOtsu
        {
            get => _useOtsu;
            set => SetProperty(ref _useOtsu, value);
        }

        private bool _useAdaptive = false;
        [TunableParam(
            Description = "true면 지역적 적응형 이진화. 조명이 불균일하거나 그라데이션이 있을 때 사용. UseOtsu와 동시 사용 불가.",
            Tier = TuningTier.Semantic, DefaultHint = "false")]
        public bool UseAdaptive
        {
            get => _useAdaptive;
            set => SetProperty(ref _useAdaptive, value);
        }

        private AdaptiveThresholdTypes _adaptiveMethod = AdaptiveThresholdTypes.GaussianC;
        [TunableParam(
            Description = "적응형 이진화 가중치 계산 방법. MeanC: 단순 평균. GaussianC: 가우시안 가중 평균(권장).",
            Tier = TuningTier.Semantic, DependsOn = "UseAdaptive", DefaultHint = "GaussianC")]
        public AdaptiveThresholdTypes AdaptiveMethod
        {
            get => _adaptiveMethod;
            set => SetProperty(ref _adaptiveMethod, value);
        }

        private int _blockSize = 11;
        [TunableParam(
            Description = "적응형 이진화의 지역 윈도우 크기(홀수). 작은 객체는 5~11, 큰 객체는 21~51.",
            Tier = TuningTier.DomainCommon, Min = 3, Max = 99,
            DependsOn = "UseAdaptive", DefaultHint = "11")]
        public int BlockSize
        {
            get => _blockSize;
            set => SetProperty(ref _blockSize, value % 2 == 0 ? value + 1 : value);
        }

        private double _cValue = 2;
        [TunableParam(
            Description = "적응형 이진화의 평균에서 빼는 상수. 노이즈에 강건하게 하려면 값을 키움(보통 2~10).",
            Tier = TuningTier.ImageDependent, Min = -20, Max = 20,
            DependsOn = "UseAdaptive", DefaultHint = "2")]
        public double CValue
        {
            get => _cValue;
            set => SetProperty(ref _cValue, value);
        }

        public ThresholdTool()
        {
            Name = "Threshold";
            ToolType = "ThresholdTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                Mat grayImage = new Mat();
                Mat outputImage = new Mat();

                // Grayscale 변환 (필요시)
                if (workImage.Channels() > 1)
                    Cv2.CvtColor(workImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    grayImage = workImage.Clone();

                double calculatedThreshold = ThresholdValue;

                if (UseAdaptive)
                {
                    // Adaptive Threshold
                    var threshType = ThresholdType == ThresholdType.BinaryInv
                        ? ThresholdTypes.BinaryInv
                        : ThresholdTypes.Binary;

                    Cv2.AdaptiveThreshold(grayImage, outputImage, MaxValue, AdaptiveMethod, threshType, BlockSize, CValue);
                    result.Data["Method"] = "Adaptive";
                    result.Data["AdaptiveMethod"] = AdaptiveMethod.ToString();
                }
                else if (UseOtsu)
                {
                    // Otsu's Binarization
                    var threshType = ConvertThresholdType(ThresholdType) | ThresholdTypes.Otsu;
                    calculatedThreshold = Cv2.Threshold(grayImage, outputImage, 0, MaxValue, threshType);
                    result.Data["Method"] = "Otsu";
                    result.Data["CalculatedThreshold"] = calculatedThreshold;
                }
                else
                {
                    // 일반 Threshold
                    Cv2.Threshold(grayImage, outputImage, ThresholdValue, MaxValue, ConvertThresholdType(ThresholdType));
                    result.Data["Method"] = "Fixed";
                }

                // 흰색 픽셀 수 계산
                int whitePixels = Cv2.CountNonZero(outputImage);

                // ROI가 사용된 경우 원본 이미지 크기로 결과 적용
                Mat finalOutput;
                if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    finalOutput = ApplyROIResult(inputImage, outputImage);
                    outputImage.Dispose();
                }
                else
                {
                    finalOutput = outputImage;
                }

                result.Success = true;
                result.Message = $"Threshold 처리 완료 (값: {calculatedThreshold:F1})";
                result.OutputImage = finalOutput;
                result.Data["ThresholdValue"] = calculatedThreshold;
                result.Data["ThresholdType"] = ThresholdType.ToString();
                result.Data["WhitePixelCount"] = whitePixels;
                result.Data["WhitePixelRatio"] = (double)whitePixels / (finalOutput.Width * finalOutput.Height);
                if (UseROI)
                {
                    result.Data["ROI"] = $"X:{ROI.X}, Y:{ROI.Y}, W:{ROI.Width}, H:{ROI.Height}";
                }

                grayImage.Dispose();
                if (workImage != inputImage)
                    workImage.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Threshold 처리 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        private ThresholdTypes ConvertThresholdType(ThresholdType type)
        {
            return type switch
            {
                ThresholdType.Binary => ThresholdTypes.Binary,
                ThresholdType.BinaryInv => ThresholdTypes.BinaryInv,
                ThresholdType.Trunc => ThresholdTypes.Trunc,
                ThresholdType.ToZero => ThresholdTypes.Tozero,
                ThresholdType.ToZeroInv => ThresholdTypes.TozeroInv,
                _ => ThresholdTypes.Binary
            };
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success", "ThresholdValue", "WhitePixelCount", "WhitePixelRatio" };
        }

        public override VisionToolBase Clone()
        {
            var clone = new ThresholdTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ThresholdValue = this.ThresholdValue,
                MaxValue = this.MaxValue,
                ThresholdType = this.ThresholdType,
                UseOtsu = this.UseOtsu,
                UseAdaptive = this.UseAdaptive,
                AdaptiveMethod = this.AdaptiveMethod,
                BlockSize = this.BlockSize,
                CValue = this.CValue
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    public enum ThresholdType
    {
        Binary,
        BinaryInv,
        Trunc,
        ToZero,
        ToZeroInv
    }
}
