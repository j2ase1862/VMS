using System;
using System.Collections.Generic;

namespace VMS.Models
{
    /// <summary>
    /// Inspection step - represents a single acquisition with associated tools
    /// Each step corresponds to a robot position with specific camera settings
    /// </summary>
    public class InspectionStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public int Sequence { get; set; } = 1;
        public string CameraId { get; set; } = string.Empty;

        // VisionSetup 에서 편집하는 필드 — 본 앱이 레시피를 재저장할 때 유실되지 않도록 보존
        public string Description { get; set; } = string.Empty;
        public int? RobotNodeIndex { get; set; }

        // Acquisition settings for this step
        public double Exposure { get; set; } = 5000;
        public double Gain { get; set; } = 1.0;
        public int LightingChannel { get; set; }
        public int LightingIntensity { get; set; } = 100;

        // Tools to execute on captured image
        public List<ToolConfig> Tools { get; set; } = new();
    }
}
