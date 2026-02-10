using System;
using System.Collections.Generic;

namespace VMS.Models
{
    /// <summary>
    /// Camera window layout configuration
    /// </summary>
    public class LayoutConfiguration
    {
        public List<CameraWindowLayout> CameraLayouts { get; set; } = new();
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Individual camera window layout
    /// </summary>
    public class CameraWindowLayout
    {
        public string CameraId { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 300;
    }
}
