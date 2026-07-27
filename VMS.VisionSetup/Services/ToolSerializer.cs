using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using VMS.VisionSetup.VisionTools.CodeReading;
using VMS.VisionSetup.VisionTools.Identification;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.Result;
using VMS.PLC.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// VisionTool ↔ ToolConfig 직렬화/역직렬화 서비스
    /// </summary>
    public static class ToolSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #region Serialize (VisionToolBase → ToolConfig)

        /// <summary>
        /// VisionToolBase를 직렬화 가능한 ToolConfig로 변환
        /// </summary>
        public static ToolConfig SerializeTool(VisionToolBase tool)
        {
            var config = new ToolConfig
            {
                Id = tool.Id,
                ToolType = tool.ToolType,
                Name = tool.Name,
                IsEnabled = tool.IsEnabled,
                X = tool.X,
                Y = tool.Y,
                UseROI = tool.UseROI,
                ROIX = tool.ROIX,
                ROIY = tool.ROIY,
                ROIWidth = tool.ROIWidth,
                ROIHeight = tool.ROIHeight,
                ROIAngle = tool.ROIAngle,
                ROICenterX = tool.ROICenterX,
                ROICenterY = tool.ROICenterY,
                Parameters = new Dictionary<string, object>(),
                PlcMappings = tool.PlcMappings.Select(m => new PlcResultMapping
                {
                    ResultKey = m.ResultKey,
                    DeviceId = m.DeviceId,
                    PlcAddress = m.PlcAddress,
                    DataType = m.DataType
                }).ToList()
            };

            // 도구 타입별 파라미터 직렬화
            switch (tool)
            {
                case GrayscaleTool:
                    // GrayscaleTool has no additional parameters
                    break;

                case BlurTool blur:
                    config.Parameters["BlurType"] = blur.BlurType.ToString();
                    config.Parameters["KernelSize"] = blur.KernelSize;
                    config.Parameters["SigmaX"] = blur.SigmaX;
                    config.Parameters["SigmaY"] = blur.SigmaY;
                    config.Parameters["SigmaColor"] = blur.SigmaColor;
                    config.Parameters["SigmaSpace"] = blur.SigmaSpace;
                    break;

                case VisionTools.SurfaceAnalysis.PhotometricStereoTool ps:
                    config.Parameters["OutputType"] = ps.OutputType.ToString();
                    config.Parameters["CurvatureGain"] = ps.CurvatureGain;
                    config.Parameters["ShadowThreshold"] = ps.ShadowThreshold;
                    config.Parameters["HighlightThreshold"] = ps.HighlightThreshold;
                    // 조명 목록(방향 + 이미지 경로)은 JSON 문자열로 저장
                    config.Parameters["Lights"] = System.Text.Json.JsonSerializer.Serialize(ps.Lights);
                    break;

                case ThresholdTool threshold:
                    config.Parameters["ThresholdValue"] = threshold.ThresholdValue;
                    config.Parameters["MaxValue"] = threshold.MaxValue;
                    config.Parameters["ThresholdType"] = threshold.ThresholdType.ToString();
                    config.Parameters["UseOtsu"] = threshold.UseOtsu;
                    config.Parameters["UseAdaptive"] = threshold.UseAdaptive;
                    config.Parameters["AdaptiveMethod"] = threshold.AdaptiveMethod.ToString();
                    config.Parameters["BlockSize"] = threshold.BlockSize;
                    config.Parameters["CValue"] = threshold.CValue;
                    break;

                case EdgeDetectionTool edge:
                    config.Parameters["Method"] = edge.Method.ToString();
                    config.Parameters["CannyThreshold1"] = edge.CannyThreshold1;
                    config.Parameters["CannyThreshold2"] = edge.CannyThreshold2;
                    config.Parameters["CannyApertureSize"] = edge.CannyApertureSize;
                    config.Parameters["L2Gradient"] = edge.L2Gradient;
                    config.Parameters["SobelKernelSize"] = edge.SobelKernelSize;
                    config.Parameters["Dx"] = edge.Dx;
                    config.Parameters["Dy"] = edge.Dy;
                    break;

                case MorphologyTool morph:
                    config.Parameters["Operation"] = morph.Operation.ToString();
                    config.Parameters["KernelShape"] = morph.KernelShape.ToString();
                    config.Parameters["KernelWidth"] = morph.KernelWidth;
                    config.Parameters["KernelHeight"] = morph.KernelHeight;
                    config.Parameters["Iterations"] = morph.Iterations;
                    break;

                case HistogramTool hist:
                    config.Parameters["Operation"] = hist.Operation.ToString();
                    config.Parameters["ClipLimit"] = hist.ClipLimit;
                    config.Parameters["TileGridWidth"] = hist.TileGridWidth;
                    config.Parameters["TileGridHeight"] = hist.TileGridHeight;
                    break;

                case FeatureMatchTool match:
                    config.Parameters["CannyLow"] = match.CannyLow;
                    config.Parameters["CannyHigh"] = match.CannyHigh;
                    config.Parameters["AngleStart"] = match.AngleStart;
                    config.Parameters["AngleExtent"] = match.AngleExtent;
                    config.Parameters["AngleStep"] = match.AngleStep;
                    config.Parameters["MinScale"] = match.MinScale;
                    config.Parameters["MaxScale"] = match.MaxScale;
                    config.Parameters["ScaleStep"] = match.ScaleStep;
                    config.Parameters["ScoreThreshold"] = match.ScoreThreshold;
                    config.Parameters["NumLevels"] = match.NumLevels;
                    config.Parameters["Greediness"] = match.Greediness;
                    config.Parameters["MaxModelPoints"] = match.MaxModelPoints;
                    config.Parameters["UseSearchRegion"] = match.UseSearchRegion;
                    config.Parameters["SearchRegionX"] = match.SearchRegionX;
                    config.Parameters["SearchRegionY"] = match.SearchRegionY;
                    config.Parameters["SearchRegionWidth"] = match.SearchRegionWidth;
                    config.Parameters["SearchRegionHeight"] = match.SearchRegionHeight;
                    config.Parameters["UseContrastInvariant"] = match.UseContrastInvariant;
                    config.Parameters["CurvatureWeight"] = match.CurvatureWeight;
                    config.Parameters["IsAutoTuneEnabled"] = match.IsAutoTuneEnabled;

                    // Serialize trained models (TemplateImage as base64 PNG)
                    var modelsList = new List<Dictionary<string, object>>();
                    foreach (var model in match.Models)
                    {
                        var modelData = new Dictionary<string, object>
                        {
                            ["Name"] = model.Name,
                            ["IsEnabled"] = model.IsEnabled
                        };

                        if (model.TemplateImage != null && !model.TemplateImage.Empty())
                        {
                            Cv2.ImEncode(".png", model.TemplateImage, out var pngBytes);
                            modelData["TemplateImageBase64"] = Convert.ToBase64String(pngBytes);
                        }

                        modelsList.Add(modelData);
                    }
                    if (modelsList.Count > 0)
                        config.Parameters["Models"] = modelsList;
                    break;

                case BlobTool blob:
                    config.Parameters["UseInternalThreshold"] = blob.UseInternalThreshold;
                    config.Parameters["ThresholdValue"] = blob.ThresholdValue;
                    config.Parameters["SegmentationPolarity"] = blob.SegmentationPolarity.ToString();
                    config.Parameters["MinArea"] = blob.MinArea;
                    config.Parameters["MaxArea"] = blob.MaxArea;
                    config.Parameters["MinPerimeter"] = blob.MinPerimeter;
                    config.Parameters["MaxPerimeter"] = blob.MaxPerimeter;
                    config.Parameters["MinCircularity"] = blob.MinCircularity;
                    config.Parameters["MaxCircularity"] = blob.MaxCircularity;
                    config.Parameters["MinAspectRatio"] = blob.MinAspectRatio;
                    config.Parameters["MaxAspectRatio"] = blob.MaxAspectRatio;
                    config.Parameters["MinConvexity"] = blob.MinConvexity;
                    config.Parameters["MaxBlobCount"] = blob.MaxBlobCount;
                    config.Parameters["SortBy"] = blob.SortBy.ToString();
                    config.Parameters["SortDescending"] = blob.SortDescending;
                    config.Parameters["RetrievalMode"] = blob.RetrievalMode.ToString();
                    config.Parameters["ApproximationMode"] = blob.ApproximationMode.ToString();
                    config.Parameters["DrawContours"] = blob.DrawContours;
                    config.Parameters["DrawBoundingBox"] = blob.DrawBoundingBox;
                    config.Parameters["DrawCenterPoint"] = blob.DrawCenterPoint;
                    config.Parameters["DrawLabels"] = blob.DrawLabels;
                    config.Parameters["EnableJudgment"] = blob.EnableJudgment;
                    config.Parameters["UseAreaJudgment"] = blob.UseAreaJudgment;
                    config.Parameters["ExpectedArea"] = blob.ExpectedArea;
                    config.Parameters["AreaTolerancePlus"] = blob.AreaTolerancePlus;
                    config.Parameters["AreaToleranceMinus"] = blob.AreaToleranceMinus;
                    config.Parameters["UseCountJudgment"] = blob.UseCountJudgment;
                    config.Parameters["CountMode"] = blob.CountMode.ToString();
                    config.Parameters["ExpectedCount"] = blob.ExpectedCount;
                    config.Parameters["ExpectedCountMax"] = blob.ExpectedCountMax;
                    break;

                case CaliperTool caliper:
                    config.Parameters["StartPointX"] = caliper.StartPoint.X;
                    config.Parameters["StartPointY"] = caliper.StartPoint.Y;
                    config.Parameters["EndPointX"] = caliper.EndPoint.X;
                    config.Parameters["EndPointY"] = caliper.EndPoint.Y;
                    config.Parameters["SearchWidth"] = caliper.SearchWidth;
                    config.Parameters["Polarity"] = caliper.Polarity.ToString();
                    config.Parameters["EdgeThreshold"] = caliper.EdgeThreshold;
                    config.Parameters["FilterHalfWidth"] = caliper.FilterHalfWidth;
                    config.Parameters["Mode"] = caliper.Mode.ToString();
                    config.Parameters["ExpectedWidth"] = caliper.ExpectedWidth;
                    config.Parameters["WidthTolerance"] = caliper.WidthTolerance;
                    config.Parameters["MaxEdges"] = caliper.MaxEdges;
                    config.Parameters["ScorerMode"] = caliper.ScorerMode.ToString();
                    config.Parameters["ExpectedPosition"] = caliper.ExpectedPosition;
                    config.Parameters["ContrastWeight"] = caliper.ContrastWeight;
                    config.Parameters["PositionWeight"] = caliper.PositionWeight;
                    config.Parameters["PositionSigma"] = caliper.PositionSigma;
                    config.Parameters["PolarityWeight"] = caliper.PolarityWeight;
                    break;

                case LineFitTool lineFit:
                    config.Parameters["StartPointX"] = lineFit.StartPoint.X;
                    config.Parameters["StartPointY"] = lineFit.StartPoint.Y;
                    config.Parameters["EndPointX"] = lineFit.EndPoint.X;
                    config.Parameters["EndPointY"] = lineFit.EndPoint.Y;
                    config.Parameters["NumCalipers"] = lineFit.NumCalipers;
                    config.Parameters["SearchLength"] = lineFit.SearchLength;
                    config.Parameters["SearchWidth"] = lineFit.SearchWidth;
                    config.Parameters["Polarity"] = lineFit.Polarity.ToString();
                    config.Parameters["EdgeThreshold"] = lineFit.EdgeThreshold;
                    config.Parameters["FilterHalfWidth"] = lineFit.FilterHalfWidth;
                    config.Parameters["FitMethod"] = lineFit.FitMethod.ToString();
                    config.Parameters["RansacThreshold"] = lineFit.RansacThreshold;
                    config.Parameters["MinFoundCalipers"] = lineFit.MinFoundCalipers;
                    break;

                case CircleFitTool circleFit:
                    config.Parameters["CenterPointX"] = circleFit.CenterPoint.X;
                    config.Parameters["CenterPointY"] = circleFit.CenterPoint.Y;
                    config.Parameters["ExpectedRadius"] = circleFit.ExpectedRadius;
                    config.Parameters["NumCalipers"] = circleFit.NumCalipers;
                    config.Parameters["SearchLength"] = circleFit.SearchLength;
                    config.Parameters["SearchWidth"] = circleFit.SearchWidth;
                    config.Parameters["StartAngle"] = circleFit.StartAngle;
                    config.Parameters["EndAngle"] = circleFit.EndAngle;
                    config.Parameters["Polarity"] = circleFit.Polarity.ToString();
                    config.Parameters["EdgeThreshold"] = circleFit.EdgeThreshold;
                    config.Parameters["FitMethod"] = circleFit.FitMethod.ToString();
                    config.Parameters["RansacThreshold"] = circleFit.RansacThreshold;
                    config.Parameters["MinFoundCalipers"] = circleFit.MinFoundCalipers;
                    break;

                case HeightSlicerTool heightSlicer:
                    config.Parameters["MinZ"] = heightSlicer.MinZ;
                    config.Parameters["MaxZ"] = heightSlicer.MaxZ;
                    break;

                case CodeReaderTool codeReader:
                    config.Parameters["CodeReaderMode"] = codeReader.CodeReaderMode.ToString();
                    config.Parameters["MaxCodeCount"] = codeReader.MaxCodeCount;
                    config.Parameters["TryHarder"] = codeReader.TryHarder;
                    config.Parameters["UseLocalization"] = codeReader.UseLocalization;
                    config.Parameters["EnableVerification"] = codeReader.EnableVerification;
                    config.Parameters["ExpectedText"] = codeReader.ExpectedText;
                    config.Parameters["UseRegexMatch"] = codeReader.UseRegexMatch;
                    config.Parameters["DrawOverlay"] = codeReader.DrawOverlay;
                    config.Parameters["ParseGs1"] = codeReader.ParseGs1;
                    config.Parameters["EnableQualityGrading"] = codeReader.EnableQualityGrading;
                    config.Parameters["MinPassGrade"] = codeReader.MinPassGrade.ToString();
                    break;

                case GeometryTool geom:
                    config.Parameters["Operation"] = geom.Operation.ToString();
                    break;

                case PlaneFitTool planeFit:
                    config.Parameters["FitMethod"] = planeFit.FitMethod.ToString();
                    config.Parameters["RansacIterations"] = planeFit.RansacIterations;
                    config.Parameters["RansacThreshold"] = planeFit.RansacThreshold;
                    config.Parameters["SampleStride"] = planeFit.SampleStride;
                    break;

                case Geometry3DTool geom3D:
                    config.Parameters["Operation"] = geom3D.Operation.ToString();
                    config.Parameters["UseManualPoints"] = geom3D.UseManualPoints;
                    config.Parameters["PointAX"] = geom3D.PointA.X;
                    config.Parameters["PointAY"] = geom3D.PointA.Y;
                    config.Parameters["PointBX"] = geom3D.PointB.X;
                    config.Parameters["PointBY"] = geom3D.PointB.Y;
                    break;

                case OCRTool ocr:
                    config.Parameters["OcrEngine"] = ocr.OcrEngine.ToString();
                    config.Parameters["Language"] = ocr.Language.ToString();
                    config.Parameters["PageSegMode"] = ocr.PageSegMode.ToString();
                    config.Parameters["EngineMode"] = ocr.EngineMode.ToString();
                    config.Parameters["CharacterWhitelist"] = ocr.CharacterWhitelist;
                    config.Parameters["ConfidenceThreshold"] = ocr.ConfidenceThreshold;
                    config.Parameters["AutoPreprocess"] = ocr.AutoPreprocess;
                    config.Parameters["InvertImage"] = ocr.InvertImage;
                    config.Parameters["TargetTextHeight"] = ocr.TargetTextHeight;
                    config.Parameters["DenoiseLevel"] = ocr.DenoiseLevel;
                    config.Parameters["DotMatrixMode"] = ocr.DotMatrixMode;
                    config.Parameters["EnableVerification"] = ocr.EnableVerification;
                    config.Parameters["ExpectedText"] = ocr.ExpectedText;
                    config.Parameters["UseRegexMatch"] = ocr.UseRegexMatch;
                    config.Parameters["DrawOverlay"] = ocr.DrawOverlay;
                    config.Parameters["TessdataPath"] = ocr.TessdataPath;
                    config.Parameters["MaxSideLen"] = ocr.MaxSideLen;
                    config.Parameters["CustomDetModelPath"] = ocr.CustomDetModelPath;
                    config.Parameters["CustomRecModelPath"] = ocr.CustomRecModelPath;
                    config.Parameters["CustomDictPath"] = ocr.CustomDictPath;
                    config.Parameters["FormatPreset"] = ocr.FormatPreset.ToString();
                    config.Parameters["CustomOutputFormat"] = ocr.CustomOutputFormat;
                    break;

                case VisionTools.ImageProcessing.ImageEnhanceTool enh:
                    config.Parameters["Mode"] = enh.Mode.ToString();
                    config.Parameters["Amount"] = enh.Amount;
                    config.Parameters["BlurKernelSize"] = enh.BlurKernelSize;
                    config.Parameters["Threshold"] = enh.Threshold;
                    break;

                case VisionTools.ImageProcessing.PolarUnwrapTool pol:
                    config.Parameters["CenterX"] = pol.CenterX;
                    config.Parameters["CenterY"] = pol.CenterY;
                    config.Parameters["InnerRadius"] = pol.InnerRadius;
                    config.Parameters["OuterRadius"] = pol.OuterRadius;
                    config.Parameters["StartAngleDeg"] = pol.StartAngleDeg;
                    config.Parameters["Direction"] = pol.Direction.ToString();
                    config.Parameters["OutputWidth"] = pol.OutputWidth;
                    config.Parameters["OutputHeight"] = pol.OutputHeight;
                    break;

                case OCVTool ocv:
                    config.Parameters["MinCharHeight"] = ocv.MinCharHeight;
                    config.Parameters["MaxCharHeight"] = ocv.MaxCharHeight;
                    config.Parameters["MinCharWidth"] = ocv.MinCharWidth;
                    config.Parameters["InvertImage"] = ocv.InvertImage;
                    config.Parameters["MatchThreshold"] = ocv.MatchThreshold;
                    config.Parameters["ExpectedText"] = ocv.ExpectedText;
                    config.Parameters["DrawOverlay"] = ocv.DrawOverlay;
                    config.Parameters["UseSearchRegion"] = ocv.UseSearchRegion;
                    config.Parameters["SearchRegionX"] = ocv.SearchRegionX;
                    config.Parameters["SearchRegionY"] = ocv.SearchRegionY;
                    config.Parameters["SearchRegionWidth"] = ocv.SearchRegionWidth;
                    config.Parameters["SearchRegionHeight"] = ocv.SearchRegionHeight;
                    config.Parameters["FontLibraryJson"] = ocv.FontLibrary.ToJson();
                    break;

                case DetectionTool detection:
                    config.Parameters["ModelPath"] = detection.ModelPath;
                    config.Parameters["InputSize"] = detection.InputSize;
                    config.Parameters["ConfidenceThreshold"] = detection.ConfidenceThreshold;
                    config.Parameters["IouThreshold"] = detection.IouThreshold;
                    config.Parameters["ClassNamesText"] = detection.ClassNamesText;
                    config.Parameters["DrawOverlay"] = detection.DrawOverlay;
                    config.Parameters["UseClahe"] = detection.UseClahe;
                    config.Parameters["ClaheClipLimit"] = detection.ClaheClipLimit;
                    config.Parameters["ClaheTileGridSize"] = detection.ClaheTileGridSize;
                    config.Parameters["UsePerClassThresholds"] = detection.UsePerClassThresholds;
                    config.Parameters["UseSahi"] = detection.UseSahi;
                    config.Parameters["SahiTileSize"] = detection.SahiTileSize;
                    config.Parameters["SahiOverlapRatio"] = detection.SahiOverlapRatio;
                    config.Parameters["UseDotAnalysis"] = detection.UseDotAnalysis;
                    config.Parameters["DotDetectionMethod"] = detection.DotDetectionMethod.ToString();
                    config.Parameters["MinDotArea"] = detection.MinDotArea;
                    config.Parameters["MaxDotArea"] = detection.MaxDotArea;
                    config.Parameters["DotCircularityThreshold"] = detection.DotCircularityThreshold;
                    config.Parameters["MinDotDistance"] = detection.MinDotDistance;
                    config.Parameters["DotPatternMetric"] = detection.DotPatternMetric.ToString();
                    config.Parameters["DotPreprocessMode"] = detection.DotPreprocessMode.ToString();
                    config.Parameters["DotClaheClipLimit"] = detection.DotClaheClipLimit;
                    config.Parameters["DotMorphKernelSize"] = detection.DotMorphKernelSize;
                    config.Parameters["DotThresholdMode"] = detection.DotThresholdMode.ToString();
                    config.Parameters["DotAdaptiveBlockSize"] = detection.DotAdaptiveBlockSize;
                    config.Parameters["DotAdaptiveC"] = detection.DotAdaptiveC;
                    // 클래스별 임계값은 "ClassName:threshold" 문자열 리스트로 직렬화
                    config.Parameters["ClassThresholds"] = string.Join(";",
                        detection.ClassThresholds.Select(c =>
                            $"{c.ClassId}|{c.ClassName}|{c.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                    // Dot 기대값: "ClassId|ClassName|ExpectedCount|CountTolerance|RefAngle|AngleTolerance|CheckAngle"
                    config.Parameters["DotExpectations"] = string.Join(";",
                        detection.DotExpectations.Select(e =>
                        {
                            var inv = System.Globalization.CultureInfo.InvariantCulture;
                            return $"{e.ClassId}|{e.ClassName}|{e.ExpectedCount}|{e.CountTolerance}|" +
                                   $"{e.ReferenceAngle.ToString(inv)}|{e.AngleTolerance.ToString(inv)}|{e.CheckAngle}";
                        }));
                    break;

                case ClassifyTool classify:
                    config.Parameters["ModelPath"] = classify.ModelPath;
                    config.Parameters["InputWidth"] = classify.InputWidth;
                    config.Parameters["InputHeight"] = classify.InputHeight;
                    config.Parameters["ConfidenceThreshold"] = classify.ConfidenceThreshold;
                    config.Parameters["ClassNamesText"] = classify.ClassNamesText;
                    config.Parameters["UseImageNetNormalization"] = classify.UseImageNetNormalization;
                    config.Parameters["DrawOverlay"] = classify.DrawOverlay;
                    break;

                case AnomalyTool anomaly:
                    config.Parameters["ModelPath"] = anomaly.ModelPath;
                    config.Parameters["InputSize"] = anomaly.InputSize;
                    config.Parameters["AnomalyThreshold"] = anomaly.AnomalyThreshold;
                    config.Parameters["DrawOverlay"] = anomaly.DrawOverlay;
                    config.Parameters["ShowHeatmap"] = anomaly.ShowHeatmap;
                    config.Parameters["HeatmapOpacity"] = anomaly.HeatmapOpacity;
                    config.Parameters["CalibrationFolder"] = anomaly.CalibrationFolder;
                    config.Parameters["CalibrationSigma"] = anomaly.CalibrationSigma;
                    break;

                case ResultTool resultTool:
                    config.Parameters["JudgmentMode"] = resultTool.JudgmentMode.ToString();
                    break;

                case EnsembleTool ensemble:
                    config.Parameters["Mode"] = ensemble.Mode.ToString();
                    config.Parameters["DetectionWeight"] = ensemble.DetectionWeight;
                    config.Parameters["AnomalyWeight"] = ensemble.AnomalyWeight;
                    config.Parameters["WeightedThreshold"] = ensemble.WeightedThreshold;
                    config.Parameters["DrawOverlay"] = ensemble.DrawOverlay;
                    break;

                case ShapeMatchTool shape:
                    config.Parameters["AngleStep"] = shape.AngleStep;
                    config.Parameters["MinScale"] = shape.MinScale;
                    config.Parameters["MaxScale"] = shape.MaxScale;
                    config.Parameters["ScaleStep"] = shape.ScaleStep;
                    config.Parameters["ScoreThreshold"] = shape.ScoreThreshold;
                    config.Parameters["NumPyramidLevels"] = shape.NumPyramidLevels;
                    config.Parameters["TopCandidates"] = shape.TopCandidates;
                    config.Parameters["MaxInstances"] = shape.MaxInstances;
                    config.Parameters["NmsDistanceFactor"] = shape.NmsDistanceFactor;
                    config.Parameters["UseSearchRegion"] = shape.UseSearchRegion;
                    config.Parameters["SearchRegionX"] = shape.SearchRegionX;
                    config.Parameters["SearchRegionY"] = shape.SearchRegionY;
                    config.Parameters["SearchRegionWidth"] = shape.SearchRegionWidth;
                    config.Parameters["SearchRegionHeight"] = shape.SearchRegionHeight;
                    if (shape.TemplatePngBytes != null && shape.TemplatePngBytes.Length > 0)
                        config.Parameters["TemplatePngBase64"] = Convert.ToBase64String(shape.TemplatePngBytes);
                    break;

                case SegmentationTool seg:
                    config.Parameters["ModelPath"] = seg.ModelPath;
                    config.Parameters["InputSize"] = seg.InputSize;
                    config.Parameters["UseImageNetNormalization"] = seg.UseImageNetNormalization;
                    config.Parameters["ShowOverlay"] = seg.ShowOverlay;
                    config.Parameters["OverlayOpacity"] = seg.OverlayOpacity;
                    config.Parameters["BackgroundClass"] = seg.BackgroundClass;
                    break;

                case YoloSegTool ys:
                    config.Parameters["ModelPath"] = ys.ModelPath;
                    config.Parameters["InputSize"] = ys.InputSize;
                    config.Parameters["ConfidenceThreshold"] = ys.ConfidenceThreshold;
                    config.Parameters["IouThreshold"] = ys.IouThreshold;
                    config.Parameters["MaskThreshold"] = ys.MaskThreshold;
                    config.Parameters["ShowOverlay"] = ys.ShowOverlay;
                    config.Parameters["OverlayOpacity"] = ys.OverlayOpacity;
                    config.Parameters["DrawBoxes"] = ys.DrawBoxes;
                    config.Parameters["OutputMaskImage"] = ys.OutputMaskImage;
                    break;

                case VisionTools.PointCloud.PointCloudFilterTool pcf:
                    config.Parameters["EnableVoxelGrid"] = pcf.EnableVoxelGrid;
                    config.Parameters["VoxelSize"] = pcf.VoxelSize;
                    config.Parameters["EnableSor"] = pcf.EnableSor;
                    config.Parameters["SorK"] = pcf.SorK;
                    config.Parameters["SorStddev"] = pcf.SorStddev;
                    break;

                case VisionTools.PointCloud.PointCloudRegistrationTool pcr:
                    config.Parameters["ReferencePath"] = pcr.ReferencePath;
                    config.Parameters["MaxIterations"] = pcr.MaxIterations;
                    config.Parameters["Tolerance"] = pcr.Tolerance;
                    config.Parameters["ApplyTransformToSource"] = pcr.ApplyTransformToSource;
                    config.Parameters["EnableCoarseAlignment"] = pcr.EnableCoarseAlignment;
                    config.Parameters["ConfidenceDistanceMm"] = pcr.ConfidenceDistanceMm;
                    break;

                case VisionTools.PointCloud.PointCloudDeviationTool pcd:
                    config.Parameters["ReferencePath"] = pcd.ReferencePath;
                    config.Parameters["ToleranceMm"] = pcd.ToleranceMm;
                    config.Parameters["HeatmapRangeMm"] = pcd.HeatmapRangeMm;
                    config.Parameters["MaxDefectRatioPercent"] = pcd.MaxDefectRatioPercent;
                    config.Parameters["OutputMode"] = pcd.OutputMode.ToString();
                    break;

                case VisionTools.PointCloud.PointCloudClusterTool pcc:
                    config.Parameters["Tolerance"] = pcc.Tolerance;
                    config.Parameters["MinPoints"] = pcc.MinPoints;
                    config.Parameters["MaxPoints"] = pcc.MaxPoints;
                    config.Parameters["MaxReportedClusters"] = pcc.MaxReportedClusters;
                    config.Parameters["OutputMode"] = pcc.OutputMode.ToString();
                    config.Parameters["ScaleMode"] = pcc.ScaleMode.ToString();
                    config.Parameters["XyScale"] = pcc.XyScale;
                    config.Parameters["DrawOverlay"] = pcc.DrawOverlay;
                    break;

                case VisionTools.PointCloud.PointCloudMaskCropTool pcm:
                    config.Parameters["InvertMask"] = pcm.InvertMask;
                    config.Parameters["MinMaskValue"] = pcm.MinMaskValue;
                    config.Parameters["DilatePixels"] = pcm.DilatePixels;
                    config.Parameters["SkipInvalidZ"] = pcm.SkipInvalidZ;
                    config.Parameters["CombineMode"] = pcm.CombineMode.ToString();
                    break;

                case VisionTools.Calibration.ImageRectifyTool rectify:
                    config.Parameters["Undistort"] = rectify.Undistort;
                    config.Parameters["ApplyHomography"] = rectify.ApplyHomography;
                    break;

                case VisionTools.Color.ColorMatchTool cmTool:
                    config.Parameters["ColorTolerance"] = cmTool.ColorTolerance;
                    config.Parameters["MorphKernelSize"] = cmTool.MorphKernelSize;
                    config.Parameters["ShowOverlay"] = cmTool.ShowOverlay;
                    config.Parameters["OverlayOpacity"] = cmTool.OverlayOpacity;
                    config.Parameters["UseSearchRegion"] = cmTool.UseSearchRegion;
                    config.Parameters["SearchRegionX"] = cmTool.SearchRegionX;
                    config.Parameters["SearchRegionY"] = cmTool.SearchRegionY;
                    config.Parameters["SearchRegionWidth"] = cmTool.SearchRegionWidth;
                    config.Parameters["SearchRegionHeight"] = cmTool.SearchRegionHeight;
                    var cmModelList = new List<Dictionary<string, object>>();
                    foreach (var m in cmTool.Models)
                    {
                        cmModelList.Add(new Dictionary<string, object>
                        {
                            ["Name"] = m.Name,
                            ["IsEnabled"] = m.IsEnabled,
                            ["MeanL"] = m.MeanL,
                            ["MeanA"] = m.MeanA,
                            ["MeanB"] = m.MeanB
                        });
                    }
                    config.Parameters["Models"] = cmModelList;
                    int cmSelIdx = cmTool.SelectedModel != null ? cmTool.Models.IndexOf(cmTool.SelectedModel) : 0;
                    config.Parameters["SelectedModelIndex"] = cmSelIdx;
                    break;

                case VisionTools.Color.ColorExtractTool cx:
                    config.Parameters["MorphKernelSize"] = cx.MorphKernelSize;
                    config.Parameters["InvertMask"] = cx.InvertMask;
                    config.Parameters["ShowOverlay"] = cx.ShowOverlay;
                    config.Parameters["OverlayOpacity"] = cx.OverlayOpacity;
                    config.Parameters["UseSearchRegion"] = cx.UseSearchRegion;
                    config.Parameters["SearchRegionX"] = cx.SearchRegionX;
                    config.Parameters["SearchRegionY"] = cx.SearchRegionY;
                    config.Parameters["SearchRegionWidth"] = cx.SearchRegionWidth;
                    config.Parameters["SearchRegionHeight"] = cx.SearchRegionHeight;
                    // Models 컬렉션 직렬화 (Dictionary 리스트)
                    var modelList = new List<Dictionary<string, object>>();
                    foreach (var m in cx.Models)
                    {
                        modelList.Add(new Dictionary<string, object>
                        {
                            ["Name"] = m.Name,
                            ["IsEnabled"] = m.IsEnabled,
                            ["HueMin"] = m.HueMin,
                            ["HueMax"] = m.HueMax,
                            ["SaturationMin"] = m.SaturationMin,
                            ["SaturationMax"] = m.SaturationMax,
                            ["ValueMin"] = m.ValueMin,
                            ["ValueMax"] = m.ValueMax
                        });
                    }
                    config.Parameters["Models"] = modelList;
                    int selectedIdx = cx.SelectedModel != null ? cx.Models.IndexOf(cx.SelectedModel) : 0;
                    config.Parameters["SelectedModelIndex"] = selectedIdx;
                    break;

                default:
                    // Unknown tool type - save what we can
                    break;
            }

            // Web 파라미터 연동 (LinkedParamCodes)
            if (tool.LinkedParamCodes != null && tool.LinkedParamCodes.Count > 0)
                config.LinkedParamCodes = new Dictionary<string, int>(tool.LinkedParamCodes);

            // Fixture 기준 좌표 저장 (Coordinates 연결에 의한 ROI 오프셋 기준점)
            if (tool.HasFixtureBaseROI)
            {
                config.Parameters["_FixtureRefX"] = tool.FixtureRefX;
                config.Parameters["_FixtureRefY"] = tool.FixtureRefY;
                config.Parameters["_FixtureRefAngle"] = tool.FixtureRefAngle;
                config.Parameters["_FixtureBaseROIX"] = tool.FixtureBaseROI.X;
                config.Parameters["_FixtureBaseROIY"] = tool.FixtureBaseROI.Y;
                config.Parameters["_FixtureBaseROIW"] = tool.FixtureBaseROI.Width;
                config.Parameters["_FixtureBaseROIH"] = tool.FixtureBaseROI.Height;
            }

            return config;
        }

        #endregion

        #region Deserialize (ToolConfig → VisionToolBase)

        /// <summary>
        /// ToolConfig를 VisionToolBase 인스턴스로 복원
        /// </summary>
        public static VisionToolBase? DeserializeTool(ToolConfig config)
        {
            VisionToolBase? tool = config.ToolType switch
            {
                "GrayscaleTool" => DeserializeGrayscaleTool(config),
                "BlurTool" => DeserializeBlurTool(config),
                "ThresholdTool" => DeserializeThresholdTool(config),
                "EdgeDetectionTool" => DeserializeEdgeDetectionTool(config),
                "MorphologyTool" => DeserializeMorphologyTool(config),
                "HistogramTool" => DeserializeHistogramTool(config),
                "FeatureMatchTool" => DeserializeFeatureMatchTool(config),
                "BlobTool" => DeserializeBlobTool(config),
                "CaliperTool" => DeserializeCaliperTool(config),
                "LineFitTool" => DeserializeLineFitTool(config),
                "CircleFitTool" => DeserializeCircleFitTool(config),
                "HeightSlicerTool" => DeserializeHeightSlicerTool(config),
                "CodeReaderTool" => DeserializeCodeReaderTool(config),
                "GeometryTool" => DeserializeGeometryTool(config),
                "PlaneFitTool" => DeserializePlaneFitTool(config),
                "Geometry3DTool" => DeserializeGeometry3DTool(config),
                "OCRTool" => DeserializeOCRTool(config),
                "OCVTool" => DeserializeOCVTool(config),
                "ImageEnhanceTool" => DeserializeImageEnhanceTool(config),
                "PolarUnwrapTool" => DeserializePolarUnwrapTool(config),
                "DetectionTool" => DeserializeDetectionTool(config),
                "ClassifyTool" => DeserializeClassifyTool(config),
                "AnomalyTool" => DeserializeAnomalyTool(config),
                "EnsembleTool" => DeserializeEnsembleTool(config),
                "ResultTool" => DeserializeResultTool(config),
                "ShapeMatchTool" => DeserializeShapeMatchTool(config),
                "SegmentationTool" => DeserializeSegmentationTool(config),
                "YoloSegTool" => DeserializeYoloSegTool(config),
                "ImageRectifyTool" => DeserializeImageRectifyTool(config),
                "PointCloudFilterTool" => DeserializePointCloudFilterTool(config),
                "PointCloudRegistrationTool" => DeserializePointCloudRegistrationTool(config),
                "PointCloudClusterTool" => DeserializePointCloudClusterTool(config),
                "PointCloudDeviationTool" => DeserializePointCloudDeviationTool(config),
                "PointCloudMaskCropTool" => DeserializePointCloudMaskCropTool(config),
                "ColorExtractTool" => DeserializeColorExtractTool(config),
                "ColorMatchTool" => DeserializeColorMatchTool(config),
                "PhotometricStereoTool" => DeserializePhotometricStereoTool(config),
                _ => null
            };

            if (tool != null)
            {
                ApplyBaseProperties(tool, config);
            }

            return tool;
        }

        private static void ApplyBaseProperties(VisionToolBase tool, ToolConfig config)
        {
            tool.Id = config.Id;
            tool.Name = config.Name;
            tool.IsEnabled = config.IsEnabled;
            tool.X = config.X;
            tool.Y = config.Y;
            tool.UseROI = config.UseROI;
            tool.ROI = new Rect(config.ROIX, config.ROIY, config.ROIWidth, config.ROIHeight);
            tool.ROIAngle = config.ROIAngle;
            tool.ROICenterX = config.ROICenterX;
            tool.ROICenterY = config.ROICenterY;

            // PLC 매핑 복원 (1:N)
            if (config.PlcMappings != null && config.PlcMappings.Count > 0)
            {
                foreach (var m in config.PlcMappings)
                {
                    tool.PlcMappings.Add(new PlcResultMapping
                    {
                        ResultKey = m.ResultKey,
                        // Phase B — DeviceId 누락(기존 레시피) 시 기본 "MainPLC" 로 폴백.
                        DeviceId = string.IsNullOrWhiteSpace(m.DeviceId) ? "MainPLC" : m.DeviceId,
                        PlcAddress = m.PlcAddress,
                        DataType = m.DataType
                    });
                }
            }
            else if (!string.IsNullOrEmpty(config.ResultPlcAddress))
            {
                // 레거시 단일 매핑 → 1항목 마이그레이션
                tool.PlcMappings.Add(new PlcResultMapping
                {
                    ResultKey = config.ResultDataKey ?? "Success",
                    DeviceId = "MainPLC",
                    PlcAddress = config.ResultPlcAddress,
                    DataType = config.ResultDataType
                });
            }

            // Web 파라미터 연동 복원 (LinkedParamCodes)
            if (config.LinkedParamCodes != null && config.LinkedParamCodes.Count > 0)
                tool.LinkedParamCodes = new Dictionary<string, int>(config.LinkedParamCodes);

            // Fixture 기준 좌표 복원 (레시피에 저장된 경우)
            if (config.Parameters.TryGetValue("_FixtureRefX", out var frx))
            {
                tool.HasFixtureBaseROI = true;
                tool.FixtureRefX = GetDouble(frx);
                if (config.Parameters.TryGetValue("_FixtureRefY", out var fry))
                    tool.FixtureRefY = GetDouble(fry);
                if (config.Parameters.TryGetValue("_FixtureRefAngle", out var fra))
                    tool.FixtureRefAngle = GetDouble(fra);
                if (config.Parameters.TryGetValue("_FixtureBaseROIX", out var brx) &&
                    config.Parameters.TryGetValue("_FixtureBaseROIY", out var bry) &&
                    config.Parameters.TryGetValue("_FixtureBaseROIW", out var brw) &&
                    config.Parameters.TryGetValue("_FixtureBaseROIH", out var brh))
                {
                    tool.FixtureBaseROI = new Rect(GetInt(brx), GetInt(bry), GetInt(brw), GetInt(brh));
                }
                else
                {
                    // fallback: 현재 ROI를 base로 사용
                    tool.FixtureBaseROI = tool.ROI;
                }
            }
        }

        private static GrayscaleTool DeserializeGrayscaleTool(ToolConfig config)
        {
            return new GrayscaleTool();
        }

        private static BlurTool DeserializeBlurTool(ToolConfig config)
        {
            var tool = new BlurTool();
            var p = config.Parameters;

            if (p.TryGetValue("BlurType", out var blurType))
                tool.BlurType = Enum.Parse<BlurType>(GetString(blurType));
            if (p.TryGetValue("KernelSize", out var kernelSize))
                tool.KernelSize = GetInt(kernelSize);
            if (p.TryGetValue("SigmaX", out var sigmaX))
                tool.SigmaX = GetDouble(sigmaX);
            if (p.TryGetValue("SigmaY", out var sigmaY))
                tool.SigmaY = GetDouble(sigmaY);
            if (p.TryGetValue("SigmaColor", out var sigmaColor))
                tool.SigmaColor = GetDouble(sigmaColor);
            if (p.TryGetValue("SigmaSpace", out var sigmaSpace))
                tool.SigmaSpace = GetDouble(sigmaSpace);

            return tool;
        }

        private static VisionTools.SurfaceAnalysis.PhotometricStereoTool DeserializePhotometricStereoTool(ToolConfig config)
        {
            var tool = new VisionTools.SurfaceAnalysis.PhotometricStereoTool();
            var p = config.Parameters;

            if (p.TryGetValue("OutputType", out var outputType))
                tool.OutputType = Enum.Parse<VisionTools.SurfaceAnalysis.PsOutputType>(GetString(outputType));
            if (p.TryGetValue("CurvatureGain", out var curvatureGain))
                tool.CurvatureGain = GetDouble(curvatureGain);
            if (p.TryGetValue("ShadowThreshold", out var shadowThreshold))
                tool.ShadowThreshold = GetInt(shadowThreshold);
            if (p.TryGetValue("HighlightThreshold", out var highlightThreshold))
                tool.HighlightThreshold = GetInt(highlightThreshold);

            if (p.TryGetValue("Lights", out var lights))
            {
                var json = GetString(lights);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var list = System.Text.Json.JsonSerializer
                        .Deserialize<List<VisionTools.SurfaceAnalysis.LightSample>>(json);
                    if (list != null)
                    {
                        tool.Lights.Clear();
                        foreach (var l in list) tool.Lights.Add(l);
                    }
                }
            }

            return tool;
        }

        private static ThresholdTool DeserializeThresholdTool(ToolConfig config)
        {
            var tool = new ThresholdTool();
            var p = config.Parameters;

            if (p.TryGetValue("ThresholdValue", out var thresholdValue))
                tool.ThresholdValue = GetDouble(thresholdValue);
            if (p.TryGetValue("MaxValue", out var maxValue))
                tool.MaxValue = GetDouble(maxValue);
            if (p.TryGetValue("ThresholdType", out var thresholdType))
                tool.ThresholdType = Enum.Parse<ThresholdType>(GetString(thresholdType));
            if (p.TryGetValue("UseOtsu", out var useOtsu))
                tool.UseOtsu = GetBool(useOtsu);
            if (p.TryGetValue("UseAdaptive", out var useAdaptive))
                tool.UseAdaptive = GetBool(useAdaptive);
            if (p.TryGetValue("AdaptiveMethod", out var adaptiveMethod))
                tool.AdaptiveMethod = Enum.Parse<AdaptiveThresholdTypes>(GetString(adaptiveMethod));
            if (p.TryGetValue("BlockSize", out var blockSize))
                tool.BlockSize = GetInt(blockSize);
            if (p.TryGetValue("CValue", out var cValue))
                tool.CValue = GetDouble(cValue);

            return tool;
        }

        private static EdgeDetectionTool DeserializeEdgeDetectionTool(ToolConfig config)
        {
            var tool = new EdgeDetectionTool();
            var p = config.Parameters;

            if (p.TryGetValue("Method", out var method))
                tool.Method = Enum.Parse<EdgeDetectionMethod>(GetString(method));
            if (p.TryGetValue("CannyThreshold1", out var ct1))
                tool.CannyThreshold1 = GetDouble(ct1);
            if (p.TryGetValue("CannyThreshold2", out var ct2))
                tool.CannyThreshold2 = GetDouble(ct2);
            if (p.TryGetValue("CannyApertureSize", out var cas))
                tool.CannyApertureSize = GetInt(cas);
            if (p.TryGetValue("L2Gradient", out var l2))
                tool.L2Gradient = GetBool(l2);
            if (p.TryGetValue("SobelKernelSize", out var sks))
                tool.SobelKernelSize = GetInt(sks);
            if (p.TryGetValue("Dx", out var dx))
                tool.Dx = GetInt(dx);
            if (p.TryGetValue("Dy", out var dy))
                tool.Dy = GetInt(dy);

            return tool;
        }

        private static MorphologyTool DeserializeMorphologyTool(ToolConfig config)
        {
            var tool = new MorphologyTool();
            var p = config.Parameters;

            if (p.TryGetValue("Operation", out var operation))
                tool.Operation = Enum.Parse<MorphologyOperation>(GetString(operation));
            if (p.TryGetValue("KernelShape", out var kernelShape))
                tool.KernelShape = Enum.Parse<MorphShapes>(GetString(kernelShape));
            if (p.TryGetValue("KernelWidth", out var kw))
                tool.KernelWidth = GetInt(kw);
            if (p.TryGetValue("KernelHeight", out var kh))
                tool.KernelHeight = GetInt(kh);
            if (p.TryGetValue("Iterations", out var iter))
                tool.Iterations = GetInt(iter);

            return tool;
        }

        private static HistogramTool DeserializeHistogramTool(ToolConfig config)
        {
            var tool = new HistogramTool();
            var p = config.Parameters;

            if (p.TryGetValue("Operation", out var operation))
                tool.Operation = Enum.Parse<HistogramOperation>(GetString(operation));
            if (p.TryGetValue("ClipLimit", out var clipLimit))
                tool.ClipLimit = GetDouble(clipLimit);
            if (p.TryGetValue("TileGridWidth", out var tgw))
                tool.TileGridWidth = GetInt(tgw);
            if (p.TryGetValue("TileGridHeight", out var tgh))
                tool.TileGridHeight = GetInt(tgh);

            return tool;
        }

        private static FeatureMatchTool DeserializeFeatureMatchTool(ToolConfig config)
        {
            var tool = new FeatureMatchTool();
            var p = config.Parameters;

            if (p.TryGetValue("CannyLow", out var cannyLow))
                tool.CannyLow = GetDouble(cannyLow);
            if (p.TryGetValue("CannyHigh", out var cannyHigh))
                tool.CannyHigh = GetDouble(cannyHigh);
            if (p.TryGetValue("AngleStart", out var angleStart))
                tool.AngleStart = GetDouble(angleStart);
            if (p.TryGetValue("AngleExtent", out var angleExtent))
                tool.AngleExtent = GetDouble(angleExtent);
            if (p.TryGetValue("AngleStep", out var angleStep))
                tool.AngleStep = GetDouble(angleStep);
            if (p.TryGetValue("MinScale", out var minScale))
                tool.MinScale = GetDouble(minScale);
            if (p.TryGetValue("MaxScale", out var maxScale))
                tool.MaxScale = GetDouble(maxScale);
            if (p.TryGetValue("ScaleStep", out var scaleStep))
                tool.ScaleStep = GetDouble(scaleStep);
            if (p.TryGetValue("ScoreThreshold", out var scoreThreshold))
                tool.ScoreThreshold = GetDouble(scoreThreshold);
            if (p.TryGetValue("NumLevels", out var numLevels))
                tool.NumLevels = GetInt(numLevels);
            if (p.TryGetValue("Greediness", out var greediness))
                tool.Greediness = GetDouble(greediness);
            if (p.TryGetValue("MaxModelPoints", out var maxModelPoints))
                tool.MaxModelPoints = GetInt(maxModelPoints);
            if (p.TryGetValue("UseSearchRegion", out var useSearchRegion))
                tool.UseSearchRegion = GetBool(useSearchRegion);
            if (p.TryGetValue("SearchRegionX", out var srx))
                tool.SearchRegionX = GetInt(srx);
            if (p.TryGetValue("SearchRegionY", out var sry))
                tool.SearchRegionY = GetInt(sry);
            if (p.TryGetValue("SearchRegionWidth", out var srw))
                tool.SearchRegionWidth = GetInt(srw);
            if (p.TryGetValue("SearchRegionHeight", out var srh))
                tool.SearchRegionHeight = GetInt(srh);
            if (p.TryGetValue("UseContrastInvariant", out var uci))
                tool.UseContrastInvariant = GetBool(uci);
            if (p.TryGetValue("CurvatureWeight", out var cw))
                tool.CurvatureWeight = GetDouble(cw);
            if (p.TryGetValue("IsAutoTuneEnabled", out var iate))
                tool.IsAutoTuneEnabled = GetBool(iate);

            // Restore trained models from serialized data
            if (p.TryGetValue("Models", out var modelsObj))
            {
                var modelEntries = GetModelList(modelsObj);
                foreach (var entry in modelEntries)
                {
                    string modelName = "";
                    bool modelEnabled = true;
                    Mat? templateImage = null;

                    if (entry.TryGetValue("Name", out var nameVal))
                        modelName = GetString(nameVal);
                    if (entry.TryGetValue("IsEnabled", out var enabledVal))
                        modelEnabled = GetBool(enabledVal);

                    if (entry.TryGetValue("TemplateImageBase64", out var b64Val))
                    {
                        var b64 = GetString(b64Val);
                        if (!string.IsNullOrEmpty(b64))
                        {
                            var pngBytes = Convert.FromBase64String(b64);
                            templateImage = Cv2.ImDecode(pngBytes, ImreadModes.Unchanged);
                        }
                    }

                    if (templateImage != null && !templateImage.Empty())
                    {
                        tool.TrainPattern(templateImage, null);
                        templateImage.Dispose();
                        var lastModel = tool.Models.LastOrDefault();
                        if (lastModel != null)
                        {
                            lastModel.Name = modelName;
                            lastModel.IsEnabled = modelEnabled;
                        }
                    }
                }
            }

            return tool;
        }

        private static BlobTool DeserializeBlobTool(ToolConfig config)
        {
            var tool = new BlobTool();
            var p = config.Parameters;

            if (p.TryGetValue("UseInternalThreshold", out var uit))
                tool.UseInternalThreshold = GetBool(uit);
            if (p.TryGetValue("ThresholdValue", out var tv))
                tool.ThresholdValue = GetDouble(tv);
            if (p.TryGetValue("SegmentationPolarity", out var sp))
                tool.SegmentationPolarity = Enum.Parse<SegmentationPolarity>(GetString(sp));
            else if (p.TryGetValue("InvertPolarity", out var ip))
                tool.SegmentationPolarity = GetBool(ip)
                    ? SegmentationPolarity.DarkOnLight
                    : SegmentationPolarity.LightOnDark;
            if (p.TryGetValue("MinArea", out var minArea))
                tool.MinArea = GetDouble(minArea);
            if (p.TryGetValue("MaxArea", out var maxArea))
                tool.MaxArea = GetDouble(maxArea);
            if (p.TryGetValue("MinPerimeter", out var minPerimeter))
                tool.MinPerimeter = GetDouble(minPerimeter);
            if (p.TryGetValue("MaxPerimeter", out var maxPerimeter))
                tool.MaxPerimeter = GetDouble(maxPerimeter);
            if (p.TryGetValue("MinCircularity", out var minCirc))
                tool.MinCircularity = GetDouble(minCirc);
            if (p.TryGetValue("MaxCircularity", out var maxCirc))
                tool.MaxCircularity = GetDouble(maxCirc);
            if (p.TryGetValue("MinAspectRatio", out var minAr))
                tool.MinAspectRatio = GetDouble(minAr);
            if (p.TryGetValue("MaxAspectRatio", out var maxAr))
                tool.MaxAspectRatio = GetDouble(maxAr);
            if (p.TryGetValue("MinConvexity", out var minConv))
                tool.MinConvexity = GetDouble(minConv);
            if (p.TryGetValue("MaxBlobCount", out var maxBlobCount))
                tool.MaxBlobCount = GetInt(maxBlobCount);
            if (p.TryGetValue("SortBy", out var sortBy))
                tool.SortBy = Enum.Parse<BlobSortBy>(GetString(sortBy));
            if (p.TryGetValue("SortDescending", out var sortDesc))
                tool.SortDescending = GetBool(sortDesc);
            if (p.TryGetValue("RetrievalMode", out var retrievalMode))
                tool.RetrievalMode = Enum.Parse<RetrievalModes>(GetString(retrievalMode));
            if (p.TryGetValue("ApproximationMode", out var approxMode))
                tool.ApproximationMode = Enum.Parse<ContourApproximationModes>(GetString(approxMode));
            if (p.TryGetValue("DrawContours", out var dc))
                tool.DrawContours = GetBool(dc);
            if (p.TryGetValue("DrawBoundingBox", out var dbb))
                tool.DrawBoundingBox = GetBool(dbb);
            if (p.TryGetValue("DrawCenterPoint", out var dcp))
                tool.DrawCenterPoint = GetBool(dcp);
            if (p.TryGetValue("DrawLabels", out var dl))
                tool.DrawLabels = GetBool(dl);
            if (p.TryGetValue("EnableJudgment", out var ej))
                tool.EnableJudgment = GetBool(ej);
            if (p.TryGetValue("UseAreaJudgment", out var uaj))
                tool.UseAreaJudgment = GetBool(uaj);
            if (p.TryGetValue("ExpectedArea", out var ea))
                tool.ExpectedArea = GetDouble(ea);
            if (p.TryGetValue("AreaTolerancePlus", out var atp))
                tool.AreaTolerancePlus = GetDouble(atp);
            else if (p.TryGetValue("AreaTolerance", out var at))
                tool.AreaTolerancePlus = GetDouble(at);
            if (p.TryGetValue("AreaToleranceMinus", out var atm))
                tool.AreaToleranceMinus = GetDouble(atm);
            else if (p.TryGetValue("AreaTolerance", out var at2))
                tool.AreaToleranceMinus = GetDouble(at2);
            if (p.TryGetValue("UseCountJudgment", out var ucj))
                tool.UseCountJudgment = GetBool(ucj);
            if (p.TryGetValue("CountMode", out var cm))
                tool.CountMode = Enum.Parse<CountJudgmentMode>(GetString(cm));
            if (p.TryGetValue("ExpectedCount", out var ec))
                tool.ExpectedCount = GetInt(ec);
            if (p.TryGetValue("ExpectedCountMax", out var ecm))
                tool.ExpectedCountMax = GetInt(ecm);

            return tool;
        }

        private static CaliperTool DeserializeCaliperTool(ToolConfig config)
        {
            var tool = new CaliperTool();
            var p = config.Parameters;

            double spx = 0, spy = 0, epx = 100, epy = 0;
            if (p.TryGetValue("StartPointX", out var spxVal)) spx = GetDouble(spxVal);
            if (p.TryGetValue("StartPointY", out var spyVal)) spy = GetDouble(spyVal);
            if (p.TryGetValue("EndPointX", out var epxVal)) epx = GetDouble(epxVal);
            if (p.TryGetValue("EndPointY", out var epyVal)) epy = GetDouble(epyVal);
            tool.StartPoint = new Point2d(spx, spy);
            tool.EndPoint = new Point2d(epx, epy);

            if (p.TryGetValue("SearchWidth", out var sw))
                tool.SearchWidth = GetDouble(sw);
            if (p.TryGetValue("Polarity", out var polarity))
                tool.Polarity = Enum.Parse<EdgePolarity>(GetString(polarity));
            if (p.TryGetValue("EdgeThreshold", out var et))
                tool.EdgeThreshold = GetDouble(et);
            if (p.TryGetValue("FilterHalfWidth", out var fhw))
                tool.FilterHalfWidth = GetInt(fhw);
            if (p.TryGetValue("Mode", out var mode))
                tool.Mode = Enum.Parse<CaliperMode>(GetString(mode));
            if (p.TryGetValue("ExpectedWidth", out var ew))
                tool.ExpectedWidth = GetDouble(ew);
            if (p.TryGetValue("WidthTolerance", out var wt))
                tool.WidthTolerance = GetDouble(wt);
            if (p.TryGetValue("MaxEdges", out var me))
                tool.MaxEdges = GetInt(me);
            if (p.TryGetValue("ScorerMode", out var sm))
                tool.ScorerMode = Enum.Parse<ScorerMode>(GetString(sm));
            if (p.TryGetValue("ExpectedPosition", out var ep))
                tool.ExpectedPosition = GetDouble(ep);
            if (p.TryGetValue("ContrastWeight", out var cw))
                tool.ContrastWeight = GetDouble(cw);
            if (p.TryGetValue("PositionWeight", out var pw))
                tool.PositionWeight = GetDouble(pw);
            if (p.TryGetValue("PositionSigma", out var ps))
                tool.PositionSigma = GetDouble(ps);
            if (p.TryGetValue("PolarityWeight", out var polW))
                tool.PolarityWeight = GetDouble(polW);

            return tool;
        }

        private static LineFitTool DeserializeLineFitTool(ToolConfig config)
        {
            var tool = new LineFitTool();
            var p = config.Parameters;

            double spx = 0, spy = 100, epx = 200, epy = 100;
            if (p.TryGetValue("StartPointX", out var spxVal)) spx = GetDouble(spxVal);
            if (p.TryGetValue("StartPointY", out var spyVal)) spy = GetDouble(spyVal);
            if (p.TryGetValue("EndPointX", out var epxVal)) epx = GetDouble(epxVal);
            if (p.TryGetValue("EndPointY", out var epyVal)) epy = GetDouble(epyVal);
            tool.StartPoint = new Point2d(spx, spy);
            tool.EndPoint = new Point2d(epx, epy);

            if (p.TryGetValue("NumCalipers", out var nc))
                tool.NumCalipers = GetInt(nc);
            if (p.TryGetValue("SearchLength", out var sl))
                tool.SearchLength = GetDouble(sl);
            if (p.TryGetValue("SearchWidth", out var sw))
                tool.SearchWidth = GetDouble(sw);
            if (p.TryGetValue("Polarity", out var polarity))
                tool.Polarity = Enum.Parse<EdgePolarity>(GetString(polarity));
            if (p.TryGetValue("EdgeThreshold", out var et))
                tool.EdgeThreshold = GetDouble(et);
            if (p.TryGetValue("FilterHalfWidth", out var fhw))
                tool.FilterHalfWidth = GetInt(fhw);
            if (p.TryGetValue("FitMethod", out var fm))
                tool.FitMethod = Enum.Parse<LineFitMethod>(GetString(fm));
            if (p.TryGetValue("RansacThreshold", out var rt))
                tool.RansacThreshold = GetDouble(rt);
            if (p.TryGetValue("MinFoundCalipers", out var mfc))
                tool.MinFoundCalipers = GetInt(mfc);

            return tool;
        }

        private static CircleFitTool DeserializeCircleFitTool(ToolConfig config)
        {
            var tool = new CircleFitTool();
            var p = config.Parameters;

            double cpx = 200, cpy = 200;
            if (p.TryGetValue("CenterPointX", out var cpxVal)) cpx = GetDouble(cpxVal);
            if (p.TryGetValue("CenterPointY", out var cpyVal)) cpy = GetDouble(cpyVal);
            tool.CenterPoint = new Point2d(cpx, cpy);

            if (p.TryGetValue("ExpectedRadius", out var er))
                tool.ExpectedRadius = GetDouble(er);
            if (p.TryGetValue("NumCalipers", out var nc))
                tool.NumCalipers = GetInt(nc);
            if (p.TryGetValue("SearchLength", out var sl))
                tool.SearchLength = GetDouble(sl);
            if (p.TryGetValue("SearchWidth", out var sw))
                tool.SearchWidth = GetDouble(sw);
            if (p.TryGetValue("StartAngle", out var sa))
                tool.StartAngle = GetDouble(sa);
            if (p.TryGetValue("EndAngle", out var ea))
                tool.EndAngle = GetDouble(ea);
            if (p.TryGetValue("Polarity", out var polarity))
                tool.Polarity = Enum.Parse<EdgePolarity>(GetString(polarity));
            if (p.TryGetValue("EdgeThreshold", out var et))
                tool.EdgeThreshold = GetDouble(et);
            if (p.TryGetValue("FitMethod", out var fm))
                tool.FitMethod = Enum.Parse<CircleFitMethod>(GetString(fm));
            if (p.TryGetValue("RansacThreshold", out var rt))
                tool.RansacThreshold = GetDouble(rt);
            if (p.TryGetValue("MinFoundCalipers", out var mfc))
                tool.MinFoundCalipers = GetInt(mfc);

            return tool;
        }

        private static HeightSlicerTool DeserializeHeightSlicerTool(ToolConfig config)
        {
            var tool = new HeightSlicerTool();
            var p = config.Parameters;

            if (p.TryGetValue("MinZ", out var minZ))
                tool.MinZ = (float)GetDouble(minZ);
            if (p.TryGetValue("MaxZ", out var maxZ))
                tool.MaxZ = (float)GetDouble(maxZ);

            return tool;
        }

        private static CodeReaderTool DeserializeCodeReaderTool(ToolConfig config)
        {
            var tool = new CodeReaderTool();
            var p = config.Parameters;

            if (p.TryGetValue("CodeReaderMode", out var crm))
                tool.CodeReaderMode = Enum.Parse<CodeReaderMode>(GetString(crm));
            if (p.TryGetValue("MaxCodeCount", out var mcc))
                tool.MaxCodeCount = GetInt(mcc);
            if (p.TryGetValue("TryHarder", out var th))
                tool.TryHarder = GetBool(th);
            if (p.TryGetValue("UseLocalization", out var ulc))
                tool.UseLocalization = GetBool(ulc);
            if (p.TryGetValue("EnableVerification", out var ev))
                tool.EnableVerification = GetBool(ev);
            if (p.TryGetValue("ExpectedText", out var et))
                tool.ExpectedText = GetString(et);
            if (p.TryGetValue("UseRegexMatch", out var urm))
                tool.UseRegexMatch = GetBool(urm);
            if (p.TryGetValue("DrawOverlay", out var dov))
                tool.DrawOverlay = GetBool(dov);
            if (p.TryGetValue("ParseGs1", out var pgs))
                tool.ParseGs1 = GetBool(pgs);
            if (p.TryGetValue("EnableQualityGrading", out var eqg))
                tool.EnableQualityGrading = GetBool(eqg);
            if (p.TryGetValue("MinPassGrade", out var mpg) &&
                Enum.TryParse<CodeQualityGrade>(GetString(mpg), true, out var grade))
                tool.MinPassGrade = grade;

            return tool;
        }

        private static GeometryTool DeserializeGeometryTool(ToolConfig config)
        {
            var tool = new GeometryTool();
            var p = config.Parameters;

            if (p.TryGetValue("Operation", out var op))
                tool.Operation = Enum.Parse<GeometryOperation>(GetString(op));

            return tool;
        }

        private static PlaneFitTool DeserializePlaneFitTool(ToolConfig config)
        {
            var tool = new PlaneFitTool();
            var p = config.Parameters;

            if (p.TryGetValue("FitMethod", out var fm))
                tool.FitMethod = Enum.Parse<PlaneFitMethod>(GetString(fm));
            if (p.TryGetValue("RansacIterations", out var ri))
                tool.RansacIterations = GetInt(ri);
            if (p.TryGetValue("RansacThreshold", out var rt))
                tool.RansacThreshold = GetDouble(rt);
            if (p.TryGetValue("SampleStride", out var ss))
                tool.SampleStride = GetInt(ss);

            return tool;
        }

        private static Geometry3DTool DeserializeGeometry3DTool(ToolConfig config)
        {
            var tool = new Geometry3DTool();
            var p = config.Parameters;

            if (p.TryGetValue("Operation", out var op))
                tool.Operation = Enum.Parse<Geometry3DOperation>(GetString(op));
            if (p.TryGetValue("UseManualPoints", out var ump))
                tool.UseManualPoints = GetBool(ump);
            if (p.TryGetValue("PointAX", out var pax) && p.TryGetValue("PointAY", out var pay))
                tool.PointA = new OpenCvSharp.Point2d(GetDouble(pax), GetDouble(pay));
            if (p.TryGetValue("PointBX", out var pbx) && p.TryGetValue("PointBY", out var pby))
                tool.PointB = new OpenCvSharp.Point2d(GetDouble(pbx), GetDouble(pby));

            return tool;
        }

        private static OCRTool DeserializeOCRTool(ToolConfig config)
        {
            var tool = new OCRTool();
            var p = config.Parameters;

            if (p.TryGetValue("OcrEngine", out var oe))
            {
                string engineStr = GetString(oe);
                // 하위 호환: 기존 레시피의 "PaddleOCR" → "PPOcrOnnx"
                if (engineStr == "PaddleOCR") engineStr = "PPOcrOnnx";
                tool.OcrEngine = Enum.Parse<OcrEngineType>(engineStr);
            }
            if (p.TryGetValue("Language", out var lang))
                tool.Language = Enum.Parse<OcrLanguage>(GetString(lang));
            if (p.TryGetValue("PageSegMode", out var psm))
                tool.PageSegMode = Enum.Parse<OcrPageSegMode>(GetString(psm));
            if (p.TryGetValue("EngineMode", out var em))
                tool.EngineMode = Enum.Parse<OcrEngineMode>(GetString(em));
            if (p.TryGetValue("CharacterWhitelist", out var cw))
                tool.CharacterWhitelist = GetString(cw);
            if (p.TryGetValue("ConfidenceThreshold", out var ct))
                tool.ConfidenceThreshold = GetDouble(ct);
            if (p.TryGetValue("AutoPreprocess", out var ap))
                tool.AutoPreprocess = GetBool(ap);
            if (p.TryGetValue("InvertImage", out var inv))
                tool.InvertImage = GetBool(inv);
            if (p.TryGetValue("TargetTextHeight", out var tth))
                tool.TargetTextHeight = GetInt(tth);
            if (p.TryGetValue("DenoiseLevel", out var dnl))
                tool.DenoiseLevel = GetInt(dnl);
            if (p.TryGetValue("DotMatrixMode", out var dmm))
                tool.DotMatrixMode = GetBool(dmm);
            if (p.TryGetValue("EnableVerification", out var ev))
                tool.EnableVerification = GetBool(ev);
            if (p.TryGetValue("ExpectedText", out var et))
                tool.ExpectedText = GetString(et);
            if (p.TryGetValue("UseRegexMatch", out var urm))
                tool.UseRegexMatch = GetBool(urm);
            if (p.TryGetValue("DrawOverlay", out var dov))
                tool.DrawOverlay = GetBool(dov);
            if (p.TryGetValue("TessdataPath", out var tp))
                tool.TessdataPath = GetString(tp);
            if (p.TryGetValue("MaxSideLen", out var msl))
                tool.MaxSideLen = GetInt(msl);
            if (p.TryGetValue("CustomDetModelPath", out var cdm))
                tool.CustomDetModelPath = GetString(cdm);
            if (p.TryGetValue("CustomRecModelPath", out var crm))
                tool.CustomRecModelPath = GetString(crm);
            if (p.TryGetValue("CustomDictPath", out var cdp))
                tool.CustomDictPath = GetString(cdp);
            if (p.TryGetValue("FormatPreset", out var fp) &&
                Enum.TryParse<OcrOutputFormatPreset>(GetString(fp), true, out var preset))
                tool.FormatPreset = preset;
            if (p.TryGetValue("CustomOutputFormat", out var cof))
                tool.CustomOutputFormat = GetString(cof);

            return tool;
        }

        private static VisionTools.ImageProcessing.ImageEnhanceTool DeserializeImageEnhanceTool(ToolConfig config)
        {
            var tool = new VisionTools.ImageProcessing.ImageEnhanceTool();
            var p = config.Parameters;
            if (p.TryGetValue("Mode", out var m) &&
                Enum.TryParse<VisionTools.ImageProcessing.ImageEnhanceMode>(GetString(m), true, out var mode))
                tool.Mode = mode;
            if (p.TryGetValue("Amount", out var a)) tool.Amount = GetDouble(a);
            if (p.TryGetValue("BlurKernelSize", out var bk)) tool.BlurKernelSize = GetInt(bk);
            if (p.TryGetValue("Threshold", out var th)) tool.Threshold = GetInt(th);
            return tool;
        }

        private static VisionTools.ImageProcessing.PolarUnwrapTool DeserializePolarUnwrapTool(ToolConfig config)
        {
            var tool = new VisionTools.ImageProcessing.PolarUnwrapTool();
            var p = config.Parameters;
            if (p.TryGetValue("CenterX", out var cx)) tool.CenterX = GetDouble(cx);
            if (p.TryGetValue("CenterY", out var cy)) tool.CenterY = GetDouble(cy);
            if (p.TryGetValue("InnerRadius", out var ir)) tool.InnerRadius = GetDouble(ir);
            if (p.TryGetValue("OuterRadius", out var or)) tool.OuterRadius = GetDouble(or);
            if (p.TryGetValue("StartAngleDeg", out var sa)) tool.StartAngleDeg = GetDouble(sa);
            if (p.TryGetValue("Direction", out var d) &&
                Enum.TryParse<VisionTools.ImageProcessing.PolarUnwrapDirection>(GetString(d), true, out var dir))
                tool.Direction = dir;
            if (p.TryGetValue("OutputWidth", out var ow)) tool.OutputWidth = GetInt(ow);
            if (p.TryGetValue("OutputHeight", out var oh)) tool.OutputHeight = GetInt(oh);
            return tool;
        }

        private static OCVTool DeserializeOCVTool(ToolConfig config)
        {
            var tool = new OCVTool();
            var p = config.Parameters;

            if (p.TryGetValue("MinCharHeight", out var mnh)) tool.MinCharHeight = GetInt(mnh);
            if (p.TryGetValue("MaxCharHeight", out var mxh)) tool.MaxCharHeight = GetInt(mxh);
            if (p.TryGetValue("MinCharWidth", out var mnw)) tool.MinCharWidth = GetInt(mnw);
            if (p.TryGetValue("InvertImage", out var inv)) tool.InvertImage = GetBool(inv);
            if (p.TryGetValue("MatchThreshold", out var mt)) tool.MatchThreshold = GetDouble(mt);
            if (p.TryGetValue("ExpectedText", out var et)) tool.ExpectedText = GetString(et);
            if (p.TryGetValue("DrawOverlay", out var dov)) tool.DrawOverlay = GetBool(dov);
            if (p.TryGetValue("UseSearchRegion", out var usr)) tool.UseSearchRegion = GetBool(usr);
            int sx = p.TryGetValue("SearchRegionX", out var srx) ? GetInt(srx) : 0;
            int sy = p.TryGetValue("SearchRegionY", out var sry) ? GetInt(sry) : 0;
            int sw = p.TryGetValue("SearchRegionWidth", out var srw) ? GetInt(srw) : 0;
            int sh = p.TryGetValue("SearchRegionHeight", out var srh) ? GetInt(srh) : 0;
            tool.SearchRegion = new Rect(sx, sy, sw, sh);
            if (p.TryGetValue("FontLibraryJson", out var fl))
            {
                var loaded = FontLibrary.FromJson(GetString(fl));
                foreach (var t in loaded.Templates)
                    if (t.TemplatePng != null) tool.FontLibrary.Add(t.Char, t.TemplatePng);
            }
            return tool;
        }

        private static DetectionTool DeserializeDetectionTool(ToolConfig config)
        {
            var tool = new DetectionTool();
            var p = config.Parameters;

            if (p.TryGetValue("ModelPath", out var mp))
                tool.ModelPath = GetString(mp);
            if (p.TryGetValue("InputSize", out var isz))
                tool.InputSize = GetInt(isz);
            if (p.TryGetValue("ConfidenceThreshold", out var ct))
                tool.ConfidenceThreshold = GetDouble(ct);
            if (p.TryGetValue("IouThreshold", out var iou))
                tool.IouThreshold = GetDouble(iou);
            if (p.TryGetValue("ClassNamesText", out var cn))
                tool.ClassNamesText = GetString(cn);
            if (p.TryGetValue("DrawOverlay", out var dov))
                tool.DrawOverlay = GetBool(dov);
            if (p.TryGetValue("UseClahe", out var uc))
                tool.UseClahe = GetBool(uc);
            if (p.TryGetValue("ClaheClipLimit", out var ccl))
                tool.ClaheClipLimit = GetDouble(ccl);
            if (p.TryGetValue("ClaheTileGridSize", out var ctg))
                tool.ClaheTileGridSize = GetInt(ctg);
            if (p.TryGetValue("UsePerClassThresholds", out var upc))
                tool.UsePerClassThresholds = GetBool(upc);
            if (p.TryGetValue("UseSahi", out var us))
                tool.UseSahi = GetBool(us);
            if (p.TryGetValue("SahiTileSize", out var sts))
                tool.SahiTileSize = GetInt(sts);
            if (p.TryGetValue("SahiOverlapRatio", out var sor))
                tool.SahiOverlapRatio = GetDouble(sor);
            if (p.TryGetValue("ClassThresholds", out var cthStr))
            {
                var s = GetString(cthStr);
                if (!string.IsNullOrEmpty(s))
                {
                    // ClassThresholds는 ModelPath 설정 시 모델 메타데이터에서 자동 채워지므로
                    // 저장된 값으로 덮어쓴다 (사용자 커스텀 임계값 복원).
                    tool.ClassThresholds.Clear();
                    foreach (var entry in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = entry.Split('|');
                        if (parts.Length == 3 &&
                            int.TryParse(parts[0], out int cid) &&
                            double.TryParse(parts[2], System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double th))
                        {
                            tool.ClassThresholds.Add(new VisionTools.DeepLearning.ClassThresholdEntry
                            {
                                ClassId = cid,
                                ClassName = parts[1],
                                Threshold = th
                            });
                        }
                    }
                }
            }

            // Dot Cluster Analysis
            if (p.TryGetValue("UseDotAnalysis", out var uda)) tool.UseDotAnalysis = GetBool(uda);
            if (p.TryGetValue("DotDetectionMethod", out var ddm) &&
                Enum.TryParse<VisionTools.DeepLearning.DotDetectionMethod>(GetString(ddm), true, out var m))
                tool.DotDetectionMethod = m;
            if (p.TryGetValue("MinDotArea", out var mna)) tool.MinDotArea = GetInt(mna);
            if (p.TryGetValue("MaxDotArea", out var mxa)) tool.MaxDotArea = GetInt(mxa);
            if (p.TryGetValue("DotCircularityThreshold", out var dct)) tool.DotCircularityThreshold = GetDouble(dct);
            if (p.TryGetValue("MinDotDistance", out var mnd)) tool.MinDotDistance = GetInt(mnd);
            if (p.TryGetValue("DotPatternMetric", out var dpm) &&
                Enum.TryParse<VisionTools.DeepLearning.DotPatternMetric>(GetString(dpm), true, out var pm))
                tool.DotPatternMetric = pm;
            if (p.TryGetValue("DotPreprocessMode", out var dpp) &&
                Enum.TryParse<VisionTools.DeepLearning.DotPreprocessMode>(GetString(dpp), true, out var pp))
                tool.DotPreprocessMode = pp;
            if (p.TryGetValue("DotClaheClipLimit", out var dccl)) tool.DotClaheClipLimit = GetDouble(dccl);
            if (p.TryGetValue("DotMorphKernelSize", out var dmk)) tool.DotMorphKernelSize = GetInt(dmk);
            if (p.TryGetValue("DotThresholdMode", out var dtm) &&
                Enum.TryParse<VisionTools.DeepLearning.DotThresholdMode>(GetString(dtm), true, out var tm))
                tool.DotThresholdMode = tm;
            if (p.TryGetValue("DotAdaptiveBlockSize", out var dabs)) tool.DotAdaptiveBlockSize = GetInt(dabs);
            if (p.TryGetValue("DotAdaptiveC", out var dac)) tool.DotAdaptiveC = GetDouble(dac);
            if (p.TryGetValue("DotExpectations", out var deStr))
            {
                var s = GetString(deStr);
                if (!string.IsNullOrEmpty(s))
                {
                    tool.DotExpectations.Clear();
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    foreach (var entry in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = entry.Split('|');
                        if (parts.Length == 7 &&
                            int.TryParse(parts[0], out int cid) &&
                            int.TryParse(parts[2], out int exp) &&
                            int.TryParse(parts[3], out int ctol) &&
                            double.TryParse(parts[4], System.Globalization.NumberStyles.Any, inv, out double refAng) &&
                            double.TryParse(parts[5], System.Globalization.NumberStyles.Any, inv, out double angTol) &&
                            bool.TryParse(parts[6], out bool checkAng))
                        {
                            tool.DotExpectations.Add(new VisionTools.DeepLearning.DotClusterExpectation
                            {
                                ClassId = cid,
                                ClassName = parts[1],
                                ExpectedCount = exp,
                                CountTolerance = ctol,
                                ReferenceAngle = refAng,
                                AngleTolerance = angTol,
                                CheckAngle = checkAng
                            });
                        }
                    }
                }
            }

            return tool;
        }

        private static ClassifyTool DeserializeClassifyTool(ToolConfig config)
        {
            var tool = new ClassifyTool();
            var p = config.Parameters;

            if (p.TryGetValue("ModelPath", out var mp))
                tool.ModelPath = GetString(mp);
            if (p.TryGetValue("InputWidth", out var iw))
                tool.InputWidth = GetInt(iw);
            if (p.TryGetValue("InputHeight", out var ih))
                tool.InputHeight = GetInt(ih);
            if (p.TryGetValue("ConfidenceThreshold", out var ct))
                tool.ConfidenceThreshold = GetDouble(ct);
            if (p.TryGetValue("ClassNamesText", out var cn))
                tool.ClassNamesText = GetString(cn);
            if (p.TryGetValue("UseImageNetNormalization", out var uin))
                tool.UseImageNetNormalization = GetBool(uin);
            if (p.TryGetValue("DrawOverlay", out var dov))
                tool.DrawOverlay = GetBool(dov);

            return tool;
        }

        private static AnomalyTool DeserializeAnomalyTool(ToolConfig config)
        {
            var tool = new AnomalyTool();
            var p = config.Parameters;

            if (p.TryGetValue("ModelPath", out var mp))
                tool.ModelPath = GetString(mp);
            if (p.TryGetValue("InputSize", out var isz))
                tool.InputSize = GetInt(isz);
            if (p.TryGetValue("AnomalyThreshold", out var at))
                tool.AnomalyThreshold = GetDouble(at);
            if (p.TryGetValue("DrawOverlay", out var dov))
                tool.DrawOverlay = GetBool(dov);
            if (p.TryGetValue("ShowHeatmap", out var sh))
                tool.ShowHeatmap = GetBool(sh);
            if (p.TryGetValue("HeatmapOpacity", out var ho))
                tool.HeatmapOpacity = GetDouble(ho);
            if (p.TryGetValue("CalibrationFolder", out var cf))
                tool.CalibrationFolder = GetString(cf);
            if (p.TryGetValue("CalibrationSigma", out var cs))
                tool.CalibrationSigma = GetDouble(cs);

            return tool;
        }

        private static ResultTool DeserializeResultTool(ToolConfig config)
        {
            var tool = new ResultTool();
            var p = config.Parameters;

            if (p.TryGetValue("JudgmentMode", out var jm))
                tool.JudgmentMode = Enum.Parse<ResultJudgmentMode>(GetString(jm));

            return tool;
        }

        private static EnsembleTool DeserializeEnsembleTool(ToolConfig config)
        {
            var tool = new EnsembleTool();
            var p = config.Parameters;

            if (p.TryGetValue("Mode", out var mode))
                tool.Mode = Enum.Parse<EnsembleMode>(GetString(mode));
            if (p.TryGetValue("DetectionWeight", out var dw))
                tool.DetectionWeight = GetDouble(dw);
            if (p.TryGetValue("AnomalyWeight", out var aw))
                tool.AnomalyWeight = GetDouble(aw);
            if (p.TryGetValue("WeightedThreshold", out var wt))
                tool.WeightedThreshold = GetDouble(wt);
            if (p.TryGetValue("DrawOverlay", out var dov))
                tool.DrawOverlay = GetBool(dov);

            return tool;
        }

        private static ShapeMatchTool DeserializeShapeMatchTool(ToolConfig config)
        {
            var tool = new ShapeMatchTool();
            var p = config.Parameters;

            if (p.TryGetValue("AngleStep", out var astep)) tool.AngleStep = GetDouble(astep);
            if (p.TryGetValue("MinScale", out var mns)) tool.MinScale = GetDouble(mns);
            if (p.TryGetValue("MaxScale", out var mxs)) tool.MaxScale = GetDouble(mxs);
            if (p.TryGetValue("ScaleStep", out var sstep)) tool.ScaleStep = GetDouble(sstep);
            if (p.TryGetValue("ScoreThreshold", out var st)) tool.ScoreThreshold = GetDouble(st);
            if (p.TryGetValue("NumPyramidLevels", out var npl)) tool.NumPyramidLevels = GetInt(npl);
            if (p.TryGetValue("TopCandidates", out var tc)) tool.TopCandidates = GetInt(tc);
            if (p.TryGetValue("MaxInstances", out var mi)) tool.MaxInstances = GetInt(mi);
            if (p.TryGetValue("NmsDistanceFactor", out var ndf)) tool.NmsDistanceFactor = GetDouble(ndf);
            if (p.TryGetValue("UseSearchRegion", out var usr)) tool.UseSearchRegion = GetBool(usr);
            int sx = p.TryGetValue("SearchRegionX", out var srx) ? GetInt(srx) : 0;
            int sy = p.TryGetValue("SearchRegionY", out var sry) ? GetInt(sry) : 0;
            int sw = p.TryGetValue("SearchRegionWidth", out var srw) ? GetInt(srw) : 0;
            int sh = p.TryGetValue("SearchRegionHeight", out var srh) ? GetInt(srh) : 0;
            tool.SearchRegion = new Rect(sx, sy, sw, sh);

            if (p.TryGetValue("TemplatePngBase64", out var b64))
            {
                try
                {
                    var s = GetString(b64);
                    if (!string.IsNullOrEmpty(s))
                        tool.TemplatePngBytes = Convert.FromBase64String(s);
                }
                catch { /* corrupt template — ignore, user can retrain */ }
            }
            return tool;
        }

        private static YoloSegTool DeserializeYoloSegTool(ToolConfig config)
        {
            var tool = new YoloSegTool();
            var p = config.Parameters;
            if (p.TryGetValue("ModelPath", out var mp)) tool.ModelPath = GetString(mp);
            if (p.TryGetValue("InputSize", out var ins)) tool.InputSize = GetInt(ins);
            if (p.TryGetValue("ConfidenceThreshold", out var ct)) tool.ConfidenceThreshold = (float)GetDouble(ct);
            if (p.TryGetValue("IouThreshold", out var iou)) tool.IouThreshold = (float)GetDouble(iou);
            if (p.TryGetValue("MaskThreshold", out var mt)) tool.MaskThreshold = (float)GetDouble(mt);
            if (p.TryGetValue("ShowOverlay", out var sho)) tool.ShowOverlay = GetBool(sho);
            if (p.TryGetValue("OverlayOpacity", out var oo)) tool.OverlayOpacity = GetDouble(oo);
            if (p.TryGetValue("DrawBoxes", out var db)) tool.DrawBoxes = GetBool(db);
            return tool;
        }

        private static SegmentationTool DeserializeSegmentationTool(ToolConfig config)
        {
            var tool = new SegmentationTool();
            var p = config.Parameters;
            if (p.TryGetValue("ModelPath", out var mp)) tool.ModelPath = GetString(mp);
            if (p.TryGetValue("InputSize", out var ins)) tool.InputSize = GetInt(ins);
            if (p.TryGetValue("UseImageNetNormalization", out var uin)) tool.UseImageNetNormalization = GetBool(uin);
            if (p.TryGetValue("ShowOverlay", out var sho)) tool.ShowOverlay = GetBool(sho);
            if (p.TryGetValue("OverlayOpacity", out var oo)) tool.OverlayOpacity = GetDouble(oo);
            if (p.TryGetValue("BackgroundClass", out var bc)) tool.BackgroundClass = GetInt(bc);
            return tool;
        }

        private static VisionTools.Calibration.ImageRectifyTool DeserializeImageRectifyTool(ToolConfig config)
        {
            var tool = new VisionTools.Calibration.ImageRectifyTool();
            var p = config.Parameters;
            if (p.TryGetValue("Undistort", out var u)) tool.Undistort = GetBool(u);
            if (p.TryGetValue("ApplyHomography", out var ah)) tool.ApplyHomography = GetBool(ah);
            return tool;
        }

        private static VisionTools.PointCloud.PointCloudFilterTool DeserializePointCloudFilterTool(ToolConfig config)
        {
            var tool = new VisionTools.PointCloud.PointCloudFilterTool();
            var p = config.Parameters;
            if (p.TryGetValue("EnableVoxelGrid", out var ev)) tool.EnableVoxelGrid = GetBool(ev);
            if (p.TryGetValue("VoxelSize", out var vs)) tool.VoxelSize = (float)GetDouble(vs);
            if (p.TryGetValue("EnableSor", out var es)) tool.EnableSor = GetBool(es);
            if (p.TryGetValue("SorK", out var sk)) tool.SorK = GetInt(sk);
            if (p.TryGetValue("SorStddev", out var ss)) tool.SorStddev = GetDouble(ss);
            return tool;
        }

        private static VisionTools.PointCloud.PointCloudRegistrationTool DeserializePointCloudRegistrationTool(ToolConfig config)
        {
            var tool = new VisionTools.PointCloud.PointCloudRegistrationTool();
            var p = config.Parameters;
            if (p.TryGetValue("ReferencePath", out var rp)) tool.ReferencePath = GetString(rp);
            if (p.TryGetValue("MaxIterations", out var mi)) tool.MaxIterations = GetInt(mi);
            if (p.TryGetValue("Tolerance", out var tol)) tool.Tolerance = (float)GetDouble(tol);
            if (p.TryGetValue("ApplyTransformToSource", out var atts)) tool.ApplyTransformToSource = GetBool(atts);
            if (p.TryGetValue("EnableCoarseAlignment", out var eca)) tool.EnableCoarseAlignment = GetBool(eca);
            if (p.TryGetValue("ConfidenceDistanceMm", out var cdm)) tool.ConfidenceDistanceMm = (float)GetDouble(cdm);
            return tool;
        }

        private static VisionTools.PointCloud.PointCloudDeviationTool DeserializePointCloudDeviationTool(ToolConfig config)
        {
            var tool = new VisionTools.PointCloud.PointCloudDeviationTool();
            var p = config.Parameters;
            if (p.TryGetValue("ReferencePath", out var rp)) tool.ReferencePath = GetString(rp);
            if (p.TryGetValue("ToleranceMm", out var tol)) tool.ToleranceMm = (float)GetDouble(tol);
            if (p.TryGetValue("HeatmapRangeMm", out var hr)) tool.HeatmapRangeMm = (float)GetDouble(hr);
            if (p.TryGetValue("MaxDefectRatioPercent", out var mdr)) tool.MaxDefectRatioPercent = (float)GetDouble(mdr);
            if (p.TryGetValue("OutputMode", out var om))
                tool.OutputMode = Enum.Parse<VisionTools.PointCloud.PointCloudDeviationTool.DeviationOutputMode>(GetString(om));
            return tool;
        }

        private static VisionTools.PointCloud.PointCloudClusterTool DeserializePointCloudClusterTool(ToolConfig config)
        {
            var tool = new VisionTools.PointCloud.PointCloudClusterTool();
            var p = config.Parameters;
            if (p.TryGetValue("Tolerance", out var tol)) tool.Tolerance = (float)GetDouble(tol);
            if (p.TryGetValue("MinPoints", out var mn)) tool.MinPoints = GetInt(mn);
            if (p.TryGetValue("MaxPoints", out var mx)) tool.MaxPoints = GetInt(mx);
            if (p.TryGetValue("MaxReportedClusters", out var mrc)) tool.MaxReportedClusters = GetInt(mrc);
            if (p.TryGetValue("OutputMode", out var om))
                tool.OutputMode = Enum.Parse<VisionTools.PointCloud.PointCloudClusterTool.ClusterOutputMode>(GetString(om));
            if (p.TryGetValue("ScaleMode", out var sm))
                tool.ScaleMode = Enum.Parse<VisionTools.PointCloud.PointCloudClusterTool.DimensionScaleMode>(GetString(sm));
            if (p.TryGetValue("XyScale", out var xs)) tool.XyScale = (float)GetDouble(xs);
            if (p.TryGetValue("DrawOverlay", out var dov)) tool.DrawOverlay = GetBool(dov);
            return tool;
        }

        private static VisionTools.PointCloud.PointCloudMaskCropTool DeserializePointCloudMaskCropTool(ToolConfig config)
        {
            var tool = new VisionTools.PointCloud.PointCloudMaskCropTool();
            var p = config.Parameters;
            if (p.TryGetValue("InvertMask", out var im)) tool.InvertMask = GetBool(im);
            if (p.TryGetValue("MinMaskValue", out var mv)) tool.MinMaskValue = GetInt(mv);
            if (p.TryGetValue("DilatePixels", out var dp)) tool.DilatePixels = GetInt(dp);
            if (p.TryGetValue("SkipInvalidZ", out var sz)) tool.SkipInvalidZ = GetBool(sz);
            if (p.TryGetValue("CombineMode", out var cm))
                tool.CombineMode = Enum.Parse<VisionTools.PointCloud.PointCloudMaskCropTool.CropCombineMode>(GetString(cm));
            return tool;
        }

        private static VisionTools.Color.ColorMatchTool DeserializeColorMatchTool(ToolConfig config)
        {
            var tool = new VisionTools.Color.ColorMatchTool();
            var p = config.Parameters;
            if (p.TryGetValue("ColorTolerance", out var ct)) tool.ColorTolerance = GetDouble(ct);
            if (p.TryGetValue("MorphKernelSize", out var mk)) tool.MorphKernelSize = GetInt(mk);
            if (p.TryGetValue("ShowOverlay", out var sho)) tool.ShowOverlay = GetBool(sho);
            if (p.TryGetValue("OverlayOpacity", out var oo)) tool.OverlayOpacity = GetDouble(oo);
            if (p.TryGetValue("UseSearchRegion", out var usr)) tool.UseSearchRegion = GetBool(usr);
            int sx = p.TryGetValue("SearchRegionX", out var srx) ? GetInt(srx) : 0;
            int sy = p.TryGetValue("SearchRegionY", out var sry) ? GetInt(sry) : 0;
            int sw = p.TryGetValue("SearchRegionWidth", out var srw) ? GetInt(srw) : 0;
            int sh = p.TryGetValue("SearchRegionHeight", out var srh) ? GetInt(srh) : 0;
            tool.SearchRegion = new Rect(sx, sy, sw, sh);

            if (p.TryGetValue("Models", out var modelsObj))
            {
                var loaded = new List<VisionTools.Color.ColorMatchModel>();
                if (modelsObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in je.EnumerateArray())
                    {
                        loaded.Add(new VisionTools.Color.ColorMatchModel
                        {
                            Name = item.TryGetProperty("Name", out var n) ? n.GetString() ?? "Model" : "Model",
                            IsEnabled = !item.TryGetProperty("IsEnabled", out var ie) || ie.GetBoolean(),
                            MeanL = item.TryGetProperty("MeanL", out var ml) ? ml.GetInt32() : 0,
                            MeanA = item.TryGetProperty("MeanA", out var ma) ? ma.GetInt32() : 128,
                            MeanB = item.TryGetProperty("MeanB", out var mb) ? mb.GetInt32() : 128
                        });
                    }
                }
                if (loaded.Count > 0)
                {
                    tool.Models.Clear();
                    foreach (var m in loaded) tool.Models.Add(m);
                    int selIdx = p.TryGetValue("SelectedModelIndex", out var sid) ? GetInt(sid) : 0;
                    tool.SelectedModel = tool.Models[Math.Clamp(selIdx, 0, tool.Models.Count - 1)];
                }
            }
            return tool;
        }

        private static VisionTools.Color.ColorExtractTool DeserializeColorExtractTool(ToolConfig config)
        {
            var tool = new VisionTools.Color.ColorExtractTool();
            var p = config.Parameters;
            if (p.TryGetValue("MorphKernelSize", out var mk)) tool.MorphKernelSize = GetInt(mk);
            if (p.TryGetValue("InvertMask", out var inv)) tool.InvertMask = GetBool(inv);
            if (p.TryGetValue("ShowOverlay", out var sho)) tool.ShowOverlay = GetBool(sho);
            if (p.TryGetValue("OverlayOpacity", out var oo)) tool.OverlayOpacity = GetDouble(oo);
            if (p.TryGetValue("UseSearchRegion", out var usr)) tool.UseSearchRegion = GetBool(usr);
            int sx = p.TryGetValue("SearchRegionX", out var srx) ? GetInt(srx) : 0;
            int sy = p.TryGetValue("SearchRegionY", out var sry) ? GetInt(sry) : 0;
            int sw = p.TryGetValue("SearchRegionWidth", out var srw) ? GetInt(srw) : 0;
            int sh = p.TryGetValue("SearchRegionHeight", out var srh) ? GetInt(srh) : 0;
            tool.SearchRegion = new Rect(sx, sy, sw, sh);

            // Models 컬렉션 복원
            if (p.TryGetValue("Models", out var modelsObj))
            {
                var loaded = new List<VisionTools.Color.ColorModel>();
                if (modelsObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in je.EnumerateArray())
                    {
                        loaded.Add(new VisionTools.Color.ColorModel
                        {
                            Name = item.TryGetProperty("Name", out var n) ? n.GetString() ?? "Model" : "Model",
                            IsEnabled = !item.TryGetProperty("IsEnabled", out var ie) || ie.GetBoolean(),
                            HueMin = item.TryGetProperty("HueMin", out var hmn) ? hmn.GetInt32() : 0,
                            HueMax = item.TryGetProperty("HueMax", out var hmx) ? hmx.GetInt32() : 179,
                            SaturationMin = item.TryGetProperty("SaturationMin", out var smn) ? smn.GetInt32() : 50,
                            SaturationMax = item.TryGetProperty("SaturationMax", out var smx) ? smx.GetInt32() : 255,
                            ValueMin = item.TryGetProperty("ValueMin", out var vmn) ? vmn.GetInt32() : 50,
                            ValueMax = item.TryGetProperty("ValueMax", out var vmx) ? vmx.GetInt32() : 255
                        });
                    }
                }
                if (loaded.Count > 0)
                {
                    tool.Models.Clear();
                    foreach (var m in loaded) tool.Models.Add(m);
                    int selIdx = p.TryGetValue("SelectedModelIndex", out var sid) ? GetInt(sid) : 0;
                    tool.SelectedModel = tool.Models[Math.Clamp(selIdx, 0, tool.Models.Count - 1)];
                }
            }
            else
            {
                // 구버전 호환 — 단일 HSV 필드만 있는 경우
                if (p.TryGetValue("HueMin", out var hmn)) tool.HueMin = GetInt(hmn);
                if (p.TryGetValue("HueMax", out var hmx)) tool.HueMax = GetInt(hmx);
                if (p.TryGetValue("SaturationMin", out var smn)) tool.SaturationMin = GetInt(smn);
                if (p.TryGetValue("SaturationMax", out var smx)) tool.SaturationMax = GetInt(smx);
                if (p.TryGetValue("ValueMin", out var vmn)) tool.ValueMin = GetInt(vmn);
                if (p.TryGetValue("ValueMax", out var vmx)) tool.ValueMax = GetInt(vmx);
            }
            return tool;
        }

        #endregion

        #region Helper Methods

        private static string GetString(object value)
        {
            if (value is JsonElement je)
                return je.GetString() ?? string.Empty;
            return value?.ToString() ?? string.Empty;
        }

        private static int GetInt(object value)
        {
            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number)
                    return je.GetInt32();
                if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var result))
                    return result;
            }
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (int.TryParse(value?.ToString(), out var parsed))
                return parsed;
            return 0;
        }

        private static double GetDouble(object value)
        {
            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number)
                    return je.GetDouble();
                if (je.ValueKind == JsonValueKind.String && double.TryParse(je.GetString(), out var result))
                    return result;
            }
            if (value is double d) return d;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is float f) return f;
            if (double.TryParse(value?.ToString(), out var parsed))
                return parsed;
            return 0.0;
        }

        private static bool GetBool(object value)
        {
            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True) return true;
                if (je.ValueKind == JsonValueKind.False) return false;
                if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var result))
                    return result;
            }
            if (value is bool b) return b;
            if (bool.TryParse(value?.ToString(), out var parsed))
                return parsed;
            return false;
        }

        /// <summary>
        /// Extracts a list of dictionaries from a serialized Models array.
        /// Handles both JsonElement (from file) and List&lt;Dictionary&gt; (from memory).
        /// </summary>
        private static List<Dictionary<string, object>> GetModelList(object value)
        {
            var result = new List<Dictionary<string, object>>();

            if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in item.EnumerateObject())
                        {
                            dict[prop.Name] = prop.Value;
                        }
                        result.Add(dict);
                    }
                }
            }
            else if (value is List<Dictionary<string, object>> list)
            {
                result = list;
            }

            return result;
        }

        #endregion
    }
}
