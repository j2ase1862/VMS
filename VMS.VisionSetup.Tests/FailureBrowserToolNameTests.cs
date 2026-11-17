using System.Collections.Generic;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services.BatchTesting;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>VS-17: Failure Browser 의 TOOL RESULTS 칩은 도구 ID(GUID) 대신 도구 이름을 보인다.</summary>
    public class FailureBrowserToolNameTests
    {
        [Fact]
        public void ToolRows_ShowToolName_FallBackToId()
        {
            var item = new BatchImageResult { ImagePath = @"C:\none\a.png" };
            item.ToolResults["id-1"] = new VisionResult { Success = false, Message = "x" };
            item.ToolResults["id-2"] = new VisionResult { Success = true };
            item.ToolNames["id-1"] = "Hole Blob";

            var vm = new FailureBrowserViewModel(new List<BatchImageResult> { item }, null);

            var rows = vm.ToolRows.ToDictionary(r => r.ToolId, r => r.ToolName);
            Assert.Equal("Hole Blob", rows["id-1"]);
            Assert.Equal("id-2", rows["id-2"]);
        }
    }
}
