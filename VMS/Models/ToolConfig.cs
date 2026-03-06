using System;
using System.Collections.Generic;
using VMS.PLC.Models;

namespace VMS.Models
{
    /// <summary>
    /// Serializable configuration for a vision tool
    /// </summary>
    public class ToolConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ToolType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Sequence { get; set; } = 1;
        public bool IsEnabled { get; set; } = true;

        // ROI settings
        public bool UseROI { get; set; }
        public int ROIX { get; set; }
        public int ROIY { get; set; }
        public int ROIWidth { get; set; }
        public int ROIHeight { get; set; }

        // Tool-specific parameters stored as dictionary
        public Dictionary<string, object> Parameters { get; set; } = new();

        // Connections to other tools
        public List<ToolConnectionConfig> Connections { get; set; } = new();

        // PLC result mappings (1:N)
        public List<PlcResultMapping> PlcMappings { get; set; } = new();

        // Legacy (deserialization compatibility)
        public string? ResultPlcAddress { get; set; }
        public PlcDataType ResultDataType { get; set; } = PlcDataType.Bit;
        public string? ResultDataKey { get; set; }
    }

    /// <summary>
    /// Connection between tools (e.g., position output → ROI input)
    /// </summary>
    public class ToolConnectionConfig
    {
        public string SourceToolId { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = string.Empty;
    }
}
