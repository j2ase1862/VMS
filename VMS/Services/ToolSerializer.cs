using VMS.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VMS.Services
{
    /// <summary>
    /// Serializer for vision tool configurations
    /// Handles conversion between tool-specific parameters and generic ToolConfig
    /// </summary>
    public static class ToolSerializer
    {
        // Known tool types and their parameter definitions
        private static readonly Dictionary<string, string[]> ToolParameters = new()
        {
            // Image Processing Tools
            ["GrayscaleTool"] = Array.Empty<string>(),
            ["BlurTool"] = new[] { "KernelSize", "BlurType" },
            ["ThresholdTool"] = new[] { "ThresholdValue", "MaxValue", "ThresholdType" },
            ["EdgeDetectionTool"] = new[] { "LowThreshold", "HighThreshold", "ApertureSize" },
            ["MorphologyTool"] = new[] { "Operation", "KernelSize", "KernelShape", "Iterations" },
            ["HistogramTool"] = new[] { "HistogramSize", "NormalizeOutput" },

            // Pattern Matching Tools
            ["FeatureMatchTool"] = new[]
            {
                "ScoreThreshold", "AngleStart", "AngleExtent",
                "ScaleStart", "ScaleExtent", "MaxResults",
                "ContrastInvariant", "UseAutoTune"
            },

            // Blob Analysis Tools
            ["BlobTool"] = new[]
            {
                "MinArea", "MaxArea", "ThresholdValue",
                "Polarity", "Connectivity", "MaxBlobs"
            },

            // Measurement Tools
            ["CaliperTool"] = new[]
            {
                "EdgeThreshold", "Mode", "FilterSize",
                "ContrastMode", "Polarity", "SubPixelPrecision"
            },
            ["LineFitTool"] = new[]
            {
                "EdgeThreshold", "NumPoints", "FilterSize",
                "FitMethod", "MinScore"
            },
            ["CircleFitTool"] = new[]
            {
                "EdgeThreshold", "NumPoints", "FilterSize",
                "FitMethod", "MinScore", "ExpectedRadius"
            }
        };

        /// <summary>
        /// Create a new ToolConfig for a given tool type
        /// </summary>
        public static ToolConfig CreateToolConfig(string toolType, string name = "")
        {
            var config = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = toolType,
                Name = string.IsNullOrEmpty(name) ? toolType : name,
                IsEnabled = true,
                Parameters = GetDefaultParameters(toolType)
            };

            return config;
        }

        /// <summary>
        /// Get default parameters for a tool type
        /// </summary>
        public static Dictionary<string, object> GetDefaultParameters(string toolType)
        {
            var parameters = new Dictionary<string, object>();

            switch (toolType)
            {
                case "BlurTool":
                    parameters["KernelSize"] = 5;
                    parameters["BlurType"] = "Gaussian";
                    break;

                case "ThresholdTool":
                    parameters["ThresholdValue"] = 128.0;
                    parameters["MaxValue"] = 255.0;
                    parameters["ThresholdType"] = "Binary";
                    break;

                case "EdgeDetectionTool":
                    parameters["LowThreshold"] = 50.0;
                    parameters["HighThreshold"] = 150.0;
                    parameters["ApertureSize"] = 3;
                    break;

                case "MorphologyTool":
                    parameters["Operation"] = "Erode";
                    parameters["KernelSize"] = 3;
                    parameters["KernelShape"] = "Rect";
                    parameters["Iterations"] = 1;
                    break;

                case "HistogramTool":
                    parameters["HistogramSize"] = 256;
                    parameters["NormalizeOutput"] = true;
                    break;

                case "FeatureMatchTool":
                    parameters["ScoreThreshold"] = 0.7;
                    parameters["AngleStart"] = -10.0;
                    parameters["AngleExtent"] = 20.0;
                    parameters["ScaleStart"] = 0.9;
                    parameters["ScaleExtent"] = 0.2;
                    parameters["MaxResults"] = 1;
                    parameters["ContrastInvariant"] = false;
                    parameters["UseAutoTune"] = false;
                    break;

                case "BlobTool":
                    parameters["MinArea"] = 100.0;
                    parameters["MaxArea"] = 100000.0;
                    parameters["ThresholdValue"] = 128.0;
                    parameters["Polarity"] = "DarkOnLight";
                    parameters["Connectivity"] = 8;
                    parameters["MaxBlobs"] = 10;
                    break;

                case "CaliperTool":
                    parameters["EdgeThreshold"] = 30.0;
                    parameters["Mode"] = "EdgePair";
                    parameters["FilterSize"] = 3;
                    parameters["ContrastMode"] = "Absolute";
                    parameters["Polarity"] = "Any";
                    parameters["SubPixelPrecision"] = true;
                    break;

                case "LineFitTool":
                    parameters["EdgeThreshold"] = 30.0;
                    parameters["NumPoints"] = 10;
                    parameters["FilterSize"] = 3;
                    parameters["FitMethod"] = "LeastSquares";
                    parameters["MinScore"] = 0.5;
                    break;

                case "CircleFitTool":
                    parameters["EdgeThreshold"] = 30.0;
                    parameters["NumPoints"] = 16;
                    parameters["FilterSize"] = 3;
                    parameters["FitMethod"] = "LeastSquares";
                    parameters["MinScore"] = 0.5;
                    parameters["ExpectedRadius"] = 50.0;
                    break;
            }

            return parameters;
        }

        /// <summary>
        /// Get a parameter value with type conversion
        /// </summary>
        public static T GetParameter<T>(ToolConfig config, string paramName, T defaultValue)
        {
            if (config.Parameters.TryGetValue(paramName, out var value))
            {
                try
                {
                    if (value is JsonElement jsonElement)
                    {
                        return ConvertJsonElement<T>(jsonElement, defaultValue);
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Set a parameter value
        /// </summary>
        public static void SetParameter(ToolConfig config, string paramName, object value)
        {
            config.Parameters[paramName] = value;
        }

        /// <summary>
        /// Validate a tool configuration
        /// </summary>
        public static (bool IsValid, string[] Errors) ValidateToolConfig(ToolConfig config)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(config.Id))
                errors.Add("Tool ID is required");

            if (string.IsNullOrEmpty(config.ToolType))
                errors.Add("Tool type is required");

            if (!ToolParameters.ContainsKey(config.ToolType))
                errors.Add($"Unknown tool type: {config.ToolType}");

            if (config.UseROI)
            {
                if (config.ROIWidth <= 0)
                    errors.Add("ROI width must be positive");
                if (config.ROIHeight <= 0)
                    errors.Add("ROI height must be positive");
            }

            return (errors.Count == 0, errors.ToArray());
        }

        /// <summary>
        /// Get all known tool types
        /// </summary>
        public static string[] GetKnownToolTypes()
        {
            return new[]
            {
                // Image Processing
                "GrayscaleTool",
                "BlurTool",
                "ThresholdTool",
                "EdgeDetectionTool",
                "MorphologyTool",
                "HistogramTool",
                // Pattern Matching
                "FeatureMatchTool",
                // Blob Analysis
                "BlobTool",
                // Measurement
                "CaliperTool",
                "LineFitTool",
                "CircleFitTool"
            };
        }

        private static T ConvertJsonElement<T>(JsonElement element, T defaultValue)
        {
            try
            {
                var targetType = typeof(T);

                if (targetType == typeof(double))
                    return (T)(object)element.GetDouble();
                if (targetType == typeof(int))
                    return (T)(object)element.GetInt32();
                if (targetType == typeof(bool))
                    return (T)(object)element.GetBoolean();
                if (targetType == typeof(string))
                    return (T)(object)(element.GetString() ?? string.Empty);

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
