using System.Collections.Generic;
using System.ComponentModel;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.Result;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 연결 입력 목록(Source Results · Source Geometries)은 실행 직전에 채워진다 —
    /// 설정 패널이 실행 뒤에도 "No source … connected" 로 남던 문제(VS-02 · 22 · 25 · 27) 회귀 방지.
    /// </summary>
    public class ToolSettingsSourceRefreshTests
    {
        private static List<string?> Track(INotifyPropertyChanged vm)
        {
            var names = new List<string?>();
            vm.PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        [Fact]
        public void Result_RaisesSourceResults_AfterExecution()
        {
            var tool = new ResultTool();
            var vm = new ResultToolSettingsViewModel(tool);
            var names = Track(vm);

            tool.SourceResults.Add(new SourceToolResult { ToolName = "Blob", Success = true });
            tool.LastResult = new VisionResult { Success = true };

            Assert.Contains(nameof(ResultToolSettingsViewModel.SourceResults), names);
            Assert.Single(vm.SourceResults);
        }

        [Fact]
        public void Ensemble_RaisesSourceResults_AfterExecution()
        {
            var tool = new EnsembleTool();
            var vm = new EnsembleToolSettingsViewModel(tool);
            var names = Track(vm);

            tool.SourceResults.Add(new SourceToolResultEx { ToolName = "Detection", Success = true });
            tool.LastResult = new VisionResult { Success = true };

            Assert.Contains(nameof(EnsembleToolSettingsViewModel.SourceResults), names);
            Assert.Single(vm.SourceResults);
        }

        [Fact]
        public void Geometry_RaisesSourceGeometries_AfterExecution()
        {
            var tool = new GeometryTool();
            var vm = new GeometryToolSettingsViewModel(tool);
            var names = Track(vm);

            tool.LastResult = new VisionResult { Success = true };

            Assert.Contains(nameof(GeometryToolSettingsViewModel.SourceGeometries), names);
        }

        [Fact]
        public void Geometry3D_RaisesSourceGeometries_AfterExecution()
        {
            var tool = new Geometry3DTool();
            var vm = new Geometry3DToolSettingsViewModel(tool);
            var names = Track(vm);

            tool.LastResult = new VisionResult { Success = true };

            Assert.Contains(nameof(Geometry3DToolSettingsViewModel.SourceGeometries), names);
        }
    }
}
