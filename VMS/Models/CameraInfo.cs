using System;

namespace VMS.Models
{
    /// <summary>
    /// Global camera registry entry - represents a physical camera in the system
    /// </summary>
    public class CameraInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1200;
        public string ConnectionString { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Container for camera registry file
    /// </summary>
    public class CameraRegistry
    {
        public List<CameraInfo> Cameras { get; set; } = new();
    }
}
