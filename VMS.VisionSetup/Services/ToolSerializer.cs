using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using OpenCvSharp;
using System;
using System.Collections.Generic;
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
                Parameters = new Dictionary<string, object>()
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
                    config.Parameters["InvertPolarity"] = blob.InvertPolarity;
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

                default:
                    // Unknown tool type - save what we can
                    break;
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
            // Note: Id is read-only, generated at construction time
            // For deserialization, we need to set it via reflection or constructor
            tool.Name = config.Name;
            tool.IsEnabled = config.IsEnabled;
            tool.X = config.X;
            tool.Y = config.Y;
            tool.UseROI = config.UseROI;
            tool.ROI = new Rect(config.ROIX, config.ROIY, config.ROIWidth, config.ROIHeight);
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
            if (p.TryGetValue("InvertPolarity", out var ip))
                tool.InvertPolarity = GetBool(ip);
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
