using System;
using System.Collections.Generic;

namespace VMS.Models
{
    /// <summary>
    /// Recipe - top-level container for product inspection configuration
    /// Contains all steps and tool configurations for inspecting a specific product
    /// </summary>
    public class Recipe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public string Author { get; set; } = string.Empty;

        // Cameras used in this recipe (by ID from global registry)
        public List<string> UsedCameraIds { get; set; } = new();

        // Inspection steps organized by camera
        public List<InspectionStep> Steps { get; set; } = new();

        // Pass/Fail criteria
        public PassFailCriteria Criteria { get; set; } = new();
    }

    /// <summary>
    /// Pass/Fail criteria configuration for a recipe
    /// </summary>
    public class PassFailCriteria
    {
        public bool RequireAllToolsPass { get; set; } = true;
        public Dictionary<string, ToolPassCriteria> ToolCriteria { get; set; } = new();
    }

    /// <summary>
    /// Pass/Fail criteria for a specific tool
    /// </summary>
    public class ToolPassCriteria
    {
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public double? MinScore { get; set; }
        public bool? MustFind { get; set; }
    }

    /// <summary>
    /// Summary information for recipe list display
    /// </summary>
    public class RecipeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime ModifiedAt { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }
}
